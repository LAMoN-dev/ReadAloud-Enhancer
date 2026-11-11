using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace Intex {
 // Pomiary są metadanymi kontrolek. Moduł nie odczytuje ani nie zapisuje treści rozmowy.
 public sealed class ReadAloudControl {
  public string Key;public bool Enabled,Visible,Off;
 }
 public sealed class ResponseFrame {
  public string Context,Conversation,UnavailableReason;public bool Generating,SendAvailable,NativeReading,WindowVisible;
  public List<ReadAloudControl> Reads=new List<ReadAloudControl>();
 }
 // Reguły wyzwalania są niezależne od UIA, aby dało się testować je bez uruchamiania głosu.
 public sealed class TriggerCoordinator {
  ResponseFrame previous;HashSet<string> baseline;string pending;DateTime pendingSince,started;
  readonly HashSet<string> attempted=new HashSet<string>();
  public bool HasPending {get{return pending!=null;}}
  public string Status {get;private set;}
  static HashSet<string> Keys(ResponseFrame f){return new HashSet<string>(f.Reads.Select(x=>x.Key));}
  void Reset(ResponseFrame f,string reason){previous=f;baseline=null;pending=null;Status=reason;}
  public string Observe(ResponseFrame f,DateTime now,bool blocked){
   if(f==null||String.IsNullOrEmpty(f.Context)){Reset(null,"Auto Read aloud: "+(f==null||String.IsNullOrEmpty(f.UnavailableReason)?"brak jednoznacznej rozmowy.":f.UnavailableReason));return null;}
   if(previous==null||previous.Context!=f.Context){Reset(f,"Auto Read aloud: punkt odniesienia ustawiony; czekam na nowe generowanie.");return null;}
   if(previous.Conversation!=f.Conversation&&!(String.IsNullOrEmpty(previous.Conversation)&&previous.Reads.Count==0&&f.Generating)){
    Reset(f,"Auto Read aloud: zmiana kontenera rozmowy; pomijam odczyt.");return null;
   }
   if(!f.WindowVisible){Reset(null,"Auto Read aloud: okno ukryte lub zminimalizowane; obserwacja wstrzymana.");return null;}
   var keys=Keys(f);
   if(baseline!=null&&!baseline.IsSubsetOf(keys)){Reset(f,"Auto Read aloud: przebudowa lub zmiana rozmowy; pomijam odczyt.");return null;}
   if(f.Generating){
    if(!previous.Generating){baseline=Keys(previous);started=now;pending=null;Status="Auto Read aloud: obserwuję generowanie.";}
    if(baseline!=null&&(now-started).TotalMinutes>10){Reset(f,"Auto Read aloud: przekroczono czas obserwacji odpowiedzi.");return null;}
    previous=f;return null;
   }
   if(baseline!=null&&pending==null){
    // Krótkie przejście bez Send nie jest ukończoną odpowiedzią.
    if(!f.SendAvailable){previous=f;return null;}
    var added=f.Reads.Where(x=>!baseline.Contains(x.Key)).ToArray();
    if(added.Length!=1){Reset(f,"Auto Read aloud: brak jednego nowego przycisku; pomijam odczyt.");return null;}
    pending=added[0].Key;pendingSince=now;Status="Auto Read aloud: odpowiedź gotowa; sprawdzam możliwość odczytu.";
   }
   if(pending!=null){
    var added=f.Reads.Where(x=>!baseline.Contains(x.Key)).ToArray();
    if(added.Length!=1||added[0].Key!=pending){Reset(f,"Auto Read aloud: kontrolki odpowiedzi zmieniły się; pomijam odczyt.");return null;}
    if(!added[0].Off){Reset(f,"Auto Read aloud: odczyt tej odpowiedzi już uruchomiono ręcznie.");return null;}
    if((now-pendingSince).TotalMinutes>5){Reset(f,"Auto Read aloud: oczekiwanie wygasło; odczyt pominięty.");return null;}
    if(blocked||f.NativeReading){Status="Auto Read aloud: czekam na zakończenie bieżącego odtwarzania.";}
    else if(!added[0].Enabled||!added[0].Visible){Status="Auto Read aloud: przycisk niewidoczny lub nieaktywny; czekam.";}
    else if(f.SendAvailable&&(now-pendingSince).TotalMilliseconds>=750){
     string key=pending;string identity=f.Context+"|"+f.Conversation+"|"+key;
     // Rezerwacja przed wywołaniem: także błąd o niepewnym wyniku nie powoduje ponowienia.
     bool first=attempted.Add(identity);Reset(f,first?"Auto Read aloud: zlecam jeden natywny odczyt.":"Auto Read aloud: tę kontrolkę już obsłużono.");return first?key:null;
    }
   }
   previous=f;return null;
  }
 }

 public sealed class AutoReadAloud : IDisposable {
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
  readonly object gate=new object();CancellationTokenSource stop;Thread worker;
  public volatile bool Blocked=true;
  public event Action<string> Changed;
  public string DiagnosticPath;
  string lastStatus;
  static string Key(AutomationElement e){return String.Join(".",e.GetRuntimeId());}
  void Log(string message){if(String.IsNullOrEmpty(DiagnosticPath))return;try{lock(gate){if(File.Exists(DiagnosticPath)&&new FileInfo(DiagnosticPath).Length>262144){string old=DiagnosticPath+".previous";if(File.Exists(old))File.Delete(old);File.Move(DiagnosticPath,old);}File.AppendAllText(DiagnosticPath,DateTime.UtcNow.ToString("o")+" "+message+Environment.NewLine);}}catch(IOException){}catch(UnauthorizedAccessException){}}
  void Publish(string message){if(lastStatus==message)return;lastStatus=message;Log(message);var handler=Changed;if(handler!=null)handler(message);}
  public void BindForeground(){
   Stop();IntPtr handle=GetForegroundWindow();
   var cancellation=new CancellationTokenSource();lock(gate){stop=cancellation;}
   worker=new Thread(()=>{try{Run(handle,cancellation.Token);}finally{lock(gate){if(Object.ReferenceEquals(stop,cancellation))stop=null;cancellation.Dispose();}}}){IsBackground=true,Name="INTEX Auto Read aloud UIA"};worker.SetApartmentState(ApartmentState.MTA);worker.Start();
  }
  public void Stop(){lock(gate){if(stop!=null){stop.Cancel();stop=null;}}}
  public void Dispose(){Stop();}
  static int ValidateWindow(IntPtr handle){
   uint pid;GetWindowThreadProcessId(handle,out pid);
   using(var process=Process.GetProcessById((int)pid)){
    string path=process.MainModule.FileName;
    if(!process.ProcessName.Equals("ChatGPT",StringComparison.OrdinalIgnoreCase)||
     (path.IndexOf(@"\WindowsApps\OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)<0&&path.IndexOf(@"\WindowsApps\OpenAI.ChatGPT_",StringComparison.OrdinalIgnoreCase)<0))throw new InvalidOperationException("Wskaż okno aplikacji ChatGPT na Windows.");
   }
   return (int)pid;
  }
  sealed class UiaFrame {
   public ResponseFrame Data;public Dictionary<string,AutomationElement> Buttons=new Dictionary<string,AutomationElement>();
  }
  static UiaFrame Read(AutomationElement root,IntPtr handle){
   var result=new UiaFrame{Data=new ResponseFrame{WindowVisible=IsWindowVisible(handle)&&!IsIconic(handle)}};
   var documents=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Document));
   var roots=new List<AutomationElement>();foreach(AutomationElement document in documents)if(document.Current.AutomationId=="RootWebArea")roots.Add(document);
   if(roots.Count!=1){result.Data.UnavailableReason="liczba dokumentów RootWebArea: "+roots.Count+"; oczekiwano 1.";return result;}
   result.Data.Context=handle.ToInt64()+"|"+Key(roots[0]);
   var groups=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Group));
   var transcripts=new List<AutomationElement>();
   foreach(AutomationElement group in groups)if((group.Current.ClassName??"").Split(' ').Contains("thread-scroll-container"))transcripts.Add(group);
   if(transcripts.Count>1){result.Data.Context=null;result.Data.UnavailableReason="więcej niż jeden kontener rozmowy.";return result;}
   // Pusta rozmowa może jeszcze nie mieć kontenera transkryptu.
   result.Data.Conversation=transcripts.Count==1?Key(transcripts[0]):"";
   var buttons=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
   int send=0,stop=0;
   foreach(AutomationElement button in buttons){
    var current=button.Current;string name=current.Name??"";object pattern;
    if(name=="Stop"&&current.IsEnabled&&!current.IsOffscreen)stop++;
    if(name=="Send"&&!current.IsOffscreen)send++;
    if(name=="Stop reading aloud")result.Data.NativeReading=true;
    if((name=="Read aloud"||name=="Stop reading aloud")&&button.TryGetCurrentPattern(TogglePattern.Pattern,out pattern)){
     string key=Key(button);result.Buttons[key]=button;
     result.Data.Reads.Add(new ReadAloudControl{Key=key,Enabled=current.IsEnabled,Visible=!current.IsOffscreen,Off=((TogglePattern)pattern).Current.ToggleState==ToggleState.Off});
    }
   }
   result.Data.Generating=stop==1;result.Data.SendAvailable=send==1&&stop==0;return result;
  }
  static bool AnyNativeReading(int pid){
   // Odczyt w drugim oknie tego samego klienta również blokuje nowe uruchomienie.
   var windows=AutomationElement.RootElement.FindAll(TreeScope.Children,new PropertyCondition(AutomationElement.ProcessIdProperty,pid));
   foreach(AutomationElement window in windows)if(window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,"Stop reading aloud"))!=null)return true;
   return false;
  }
  // Diagnostyka tylko do odczytu: bez treści odpowiedzi i bez aktywacji przycisków.
  public static void Probe(string report){
   var results=new List<object>();
   foreach(var process in Process.GetProcessesByName("ChatGPT"))using(process){
    var windows=AutomationElement.RootElement.FindAll(TreeScope.Children,new PropertyCondition(AutomationElement.ProcessIdProperty,process.Id));
    foreach(AutomationElement window in windows){IntPtr handle=new IntPtr(window.Current.NativeWindowHandle);try{
     ValidateWindow(handle);var f=Read(window,handle).Data;
     results.Add(new{pid=process.Id,handle=handle.ToInt64(),clientVersion=process.MainModule.FileVersionInfo.FileVersion,expectedTitle=window.Current.Name=="ChatGPT",validContext=!String.IsNullOrEmpty(f.Context),hasTranscript=!String.IsNullOrEmpty(f.Conversation),reason=f.UnavailableReason,generating=f.Generating,sendAvailable=f.SendAvailable,nativeReading=f.NativeReading,windowVisible=f.WindowVisible,readButtons=f.Reads.Count,visibleReadButtons=f.Reads.Count(x=>x.Visible),enabledReadButtons=f.Reads.Count(x=>x.Enabled)});
    }catch(Exception ex){results.Add(new{handle=handle.ToInt64(),error=ex.GetType().Name});}}
   }
   if(report!=null)Json.Save(report,new{dateUtc=DateTime.UtcNow.ToString("o"),windows=results,realToggleCalls=0});
  }
  void Run(IntPtr handle,CancellationToken token){
   AutomationElement root=null;StructureChangedEventHandler changed=null;
   try{
    int pid=ValidateWindow(handle);root=AutomationElement.FromHandle(handle);
    if(root.Current.Name!="ChatGPT")throw new InvalidOperationException("Wybrane okno nie jest oknem rozmowy ChatGPT.");
    var coordinator=new TriggerCoordinator();int dirty=1;
    changed=(sender,args)=>Interlocked.Exchange(ref dirty,1);
    Automation.AddStructureChangedEventHandler(root,TreeScope.Subtree,changed);
    Publish("Auto Read aloud: połączono z oknem "+handle.ToInt64()+"; czekam na nowe generowanie.");
    var clock=Stopwatch.StartNew();long lastPoll=-1000;string lastFrame=null;
    while(!token.IsCancellationRequested){
     if(!IsWindow(handle))throw new InvalidOperationException("Okno ChatGPT zamknięto. Wskaż je ponownie w Opcjach.");
     if(Interlocked.Exchange(ref dirty,0)==0&&clock.ElapsedMilliseconds-lastPoll<500){if(token.WaitHandle.WaitOne(100))break;continue;}
     lastPoll=clock.ElapsedMilliseconds;
     UiaFrame frame;
     try{frame=Read(root,handle);}catch(ElementNotAvailableException){coordinator.Observe(null,DateTime.UtcNow,true);continue;}
     if(token.IsCancellationRequested)break;
     bool busy=Blocked||(coordinator.HasPending&&AnyNativeReading(pid));
     string metadata="UIA: context="+(!String.IsNullOrEmpty(frame.Data.Context))+", transcript="+(!String.IsNullOrEmpty(frame.Data.Conversation))+", generating="+frame.Data.Generating+", send="+frame.Data.SendAvailable+", reads="+frame.Data.Reads.Count+", visibleReads="+frame.Data.Reads.Count(x=>x.Visible)+", windowVisible="+frame.Data.WindowVisible+", blocked="+busy+", native="+frame.Data.NativeReading;
     if(metadata!=lastFrame){lastFrame=metadata;Log(metadata);}
     string candidate=coordinator.Observe(frame.Data,DateTime.UtcNow,busy);
     Publish(coordinator.Status);
     if(candidate!=null){
      // Ponowny odczyt tuż przed akcją. Nie ma automatycznej ponownej próby.
      var fresh=Read(root,handle);AutomationElement button;object pattern;
      bool unchanged=fresh.Data.Context==frame.Data.Context&&fresh.Data.Conversation==frame.Data.Conversation&&new HashSet<string>(fresh.Buttons.Keys).SetEquals(frame.Buttons.Keys);
      if(!unchanged||fresh.Data.Generating||!fresh.Data.SendAvailable||!fresh.Data.WindowVisible||AnyNativeReading(pid)||!fresh.Buttons.TryGetValue(candidate,out button)){
       Publish("Auto Read aloud: warunki zmieniły się przed wywołaniem; pominięto bez ponawiania.");continue;
      }
      if(button.Current.IsOffscreen||!button.Current.IsEnabled||!button.TryGetCurrentPattern(TogglePattern.Pattern,out pattern)||((TogglePattern)pattern).Current.ToggleState!=ToggleState.Off){Publish("Auto Read aloud: przycisk nie jest gotowy; pominięto bez ponawiania.");continue;}
      if(token.IsCancellationRequested||Blocked)continue;
      ((TogglePattern)pattern).Toggle();
      Publish("Auto Read aloud: wykonano jedno Toggle; brak automatycznego ponawiania.");
     }
     if(token.WaitHandle.WaitOne(250))break;
    }
   }catch(Exception ex){if(!token.IsCancellationRequested)Publish("Auto Read aloud zatrzymane: "+ex.GetType().Name+" — "+ex.Message+" Wskaż okno ponownie w Opcjach.");}
   finally{if(root!=null&&changed!=null)try{Automation.RemoveStructureChangedEventHandler(root,changed);}catch{} }
  }
 }
}

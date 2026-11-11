using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("INTEX ReadAloud Enhancer")]
[assembly: AssemblyVersion("0.12.0.0")]
[assembly: AssemblyFileVersion("0.12.0.0")]
namespace Intex {
 static class Program {
  static string Arg(string[] args,string name,string fallback){int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
  [STAThread] static int Main(string[] args){
   string report=Arg(args,"--report",null);string root=Path.GetFullPath(Arg(args,"--data-root",AppDomain.CurrentDomain.BaseDirectory));
   if(root.Length>3)root=root.TrimEnd(Path.DirectorySeparatorChar);
   bool titlebar=args.Contains("--titlebar");
   try{
    if(args.Contains("--auto-probe")){AutoReadAloud.Probe(report);return 0;}
    if(args.Contains("--probe")){
     var scanner=new Scanner();var watch=Stopwatch.StartNew();var first=scanner.Scan();watch.Stop();double initial=watch.Elapsed.TotalMilliseconds;long bytes=scanner.Reader.ParsedBytes;watch.Restart();var second=scanner.Scan();watch.Stop();
     var result=new{version="v0.12",platform="Windows",date=DateTime.Now.ToString("o"),candidates=first.Count,firstScanMs=initial,repeatScanMs=watch.Elapsed.TotalMilliseconds,repeatParsedBytes=scanner.Reader.ParsedBytes-bytes,streams=second.Select(c=>new{name=c.Name,format=c.Format,seconds=c.Seconds,bytes=c.Length})};
     if(report!=null)Json.Save(report,result);return 0;
    }
    if(args.Contains("--compatibility")){
     int records=0,pinned=0,failed=0;string index=Path.Combine(root,"history-index");if(Directory.Exists(index))foreach(var p in Directory.EnumerateFiles(index,"*.json")){if(!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(p),"^[0-9a-f]{32}$"))continue;try{var e=Json.Read<Entry>(p);if(e.Id!=Path.GetFileNameWithoutExtension(p)||e.Format!="AAC"&&e.Format!="MP3")throw new IOException();if(!String.IsNullOrEmpty(e.Mp3File))HistoryStore.Owned(Path.Combine(root,"history"),e.Mp3File);records++;if(e.Pinned)pinned++;}catch{failed++;}}
     var s=HistoryStore.LoadSettings(root);if(report!=null)Json.Save(report,new{ok=failed==0,records=records,pinned=pinned,failed=failed,theme=s.theme,minimizeTo=s.minimizeTo,retentionDays=s.retentionDays,titleBar=s.titleBar});return failed==0?0:1;
    }
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
    if(args.Contains("--self-test")){Tests.Run(root,report);return 0;}
    if(args.Contains("--ui-test")){Tests.Ui(root,titlebar,report);return 0;}
    if(args.Contains("--tray-test")){Tests.Tray(root,titlebar,report);return 0;}
    if(args.Contains("--watch-test")){Tests.Watch(root,titlebar,report);return 0;}
    if(args.Contains("--auto-trigger-test")){AutoReadAloudTests.Run(report);return 0;}
    string key;using(var hash=SHA256.Create())key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(root.TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant()))).Replace("-","");
    using(var mutex=new Mutex(false,"Local\\INTEXReadAloud_"+key))
    using(var activate=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\INTEXReadAloud_Activate_"+key))
    using(var acknowledged=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\INTEXReadAloud_Ack_"+key)){
     bool held=false;try{try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}
      if(!held){activate.Set();acknowledged.WaitOne(2500);return 0;}
      using(var window=new MainWindow(root,titlebar)){
       bool activationHost=args.Contains("--activation-host-test");if(activationHost){window.TestMode=true;window.Preferences.minimizeTo="Tray";window.Shown+=(s,e)=>{window.MinimizeWindow();if(report!=null)Json.Save(report+".ready",new{ready=true,hidden=!window.Visible});};window.ActivatedBySecondInstance+=()=>{if(report!=null)Json.Save(report,new{ok=window.Visible&&window.ShowInTaskbar,restored=window.Visible,taskbar=window.ShowInTaskbar,version=window.Version});window.Close();};}
       var createdHandle=window.Handle;window.ListenForActivation(activate,acknowledged);Application.Run(window);
      }return 0;
     }finally{if(held)mutex.ReleaseMutex();}
    }
   }catch(Exception ex){if(report!=null){Json.Save(report,new{ok=false,error=ex.ToString()});}else MessageBox.Show(ex.Message,"INTEX ReadAloud Enhancer — błąd",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
  }
 }
 static class Tests {
  static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
  static string Temp(){var p=Path.Combine(Path.GetTempPath(),"intex-csharp-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
  static void DeleteTemp(string path){if(!path.StartsWith(Path.GetTempPath(),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(path).StartsWith("intex-csharp-test-"))throw new IOException("Niebezpieczny folder testu.");Directory.Delete(path,true);}
  static int IconPixels(Icon icon,Color expected){int count=0;using(icon)using(var bitmap=icon.ToBitmap())for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++){var pixel=bitmap.GetPixel(x,y);if(pixel.A>128&&pixel.R==expected.R&&pixel.G==expected.G&&pixel.B==expected.B)count++;}return count;}
  static void Fixture(string root,string target){
   var start=new ProcessStartInfo(Path.Combine(root,"tools","ffmpeg.exe"),"-nostdin -hide_banner -loglevel error -y -f lavfi -i sine=frequency=440:duration=6 -ar 24000 -ac 1 -c:a aac -f adts \""+target+"\""){UseShellExecute=false,CreateNoWindow=true};using(var p=Process.Start(start)){Check(p.WaitForExit(30000)&&p.ExitCode==0,"Tworzenie AAC nie powiodlo sie.");}
  }
  public static void Run(string root,string report){
   var temp=Temp();try{
    var source=Path.Combine(temp,"sample.aac");Fixture(root,source);var reader=new AudioReader();var info=reader.Inspect(source);Check(info.Format=="AAC"&&!info.Invalid&&info.Seconds>5.9,"Odczyt AAC.");
    long parsed=reader.ParsedBytes;reader.Inspect(source);Check(reader.ParsedBytes==parsed,"Powtorna analiza nie korzysta z cache.");
    var firstBytes=File.ReadAllBytes(source);using(var f=new FileStream(source,FileMode.Append))f.Write(firstBytes,0,firstBytes.Length);
    var longer=reader.Inspect(source);Check(longer.Seconds>11.9&&reader.ParsedBytes-parsed==firstBytes.Length,"Analiza przyrostowa.");
    using(var f=new FileStream(source,FileMode.Append)){f.WriteByte(255);f.WriteByte(249);f.WriteByte(88);}
    var partial=reader.Inspect(source);Check(partial.CompleteBytes==firstBytes.Length*2&&partial.Bytes==partial.CompleteBytes+3&&!partial.Invalid,"Niepelna ramka AAC.");
    var store=new HistoryStore(temp);store.Initialize();var scanner=new Scanner();var candidate=scanner.FromFile(source);var e=store.Capture(candidate,scanner.Reader,CancellationToken.None);Check(e!=null&&e.Partial,"Przechwytywanie.");
    string raw=HistoryStore.Owned(store.Raw,e.Id+".aac");string target=HistoryStore.Owned(store.History,"test.mp3");Converter.Convert(root,raw,target,"AAC",CancellationToken.None).GetAwaiter().GetResult();var mp3=new AudioReader().Parse(target);Check(!mp3.Invalid&&mp3.Format=="MP3"&&Math.Abs(mp3.Seconds-longer.Seconds)<.2,"Konwersja i dlugosc MP3.");
    e.Mp3File="test.mp3";e.ExportedBytes=e.CopiedBytes;e.Pinned=true;e.SavedAtUtc=DateTime.UtcNow.AddDays(-60).ToString("o");store.Save(e);
    var loaded=new HistoryStore(temp);loaded.Initialize();Check(loaded.Entries[e.Id].Pinned&&loaded.Entries[e.Id].CopiedBytes==e.CopiedBytes,"Zgodnosc indeksu.");
    loaded.Cleanup(null,null);Check(File.Exists(target),"Chronione nagranie wygaslo.");loaded.Pin(e.Id,false);loaded.Cleanup(null,null);Check(!File.Exists(target)&&loaded.Entries.Count==0,"Retencja.");Check(loaded.Capture(candidate,scanner.Reader,CancellationToken.None)==null,"Ponowne pobranie wygaslej kopii.");
    bool rejected=false;try{HistoryStore.Owned(store.History,"..\\outside.mp3");}catch(IOException){rejected=true;}Check(rejected,"Zabezpieczenie sciezki.");
    File.WriteAllText(Path.Combine(temp,"intex-settings.json"),"{\"theme\":\"INTEX\",\"minimizeTo\":\"Tray\",\"retentionDays\":0,\"custom\":\"keep\"}");loaded=new HistoryStore(temp);loaded.Initialize();Check(loaded.Settings.retentionDays==0&&loaded.Settings.minimizeTo=="Tray"&&!loaded.Settings.titleBar,"Odczyt ustawien PowerShell.");loaded.Settings.titleBar=true;loaded.SaveSettings(loaded.Settings);var savedSettings=HistoryStore.LoadSettings(temp);Check(File.ReadAllText(Path.Combine(temp,"intex-settings.json")).Contains("keep")&&savedSettings.titleBar,"Zachowanie dodatkowych ustawien i belki.");
    Check(!savedSettings.autoReadAloud,"Starsze ustawienia wlaczyly Auto Read aloud.");loaded.Settings.autoReadAloud=true;loaded.SaveSettings(loaded.Settings);Check(HistoryStore.LoadSettings(temp).autoReadAloud,"Zapis opcji Auto Read aloud.");
    Check(savedSettings.watchMode=="Off","Starsze ustawienia samoczynnie wlaczyly Watch.");
    foreach(var mode in new[]{ContinuousWatchMode.Watch,ContinuousWatchMode.WatchAndPlay,ContinuousWatchMode.Off}){
     loaded.Settings.watchMode=mode.ToString();loaded.SaveSettings(loaded.Settings);
     using(var window=new MainWindow(temp,false,new string[0],root)){Check(window.WatchMode==mode&&window.WatchMenuChecked==(mode==ContinuousWatchMode.Watch)&&window.WatchPlayMenuChecked==(mode==ContinuousWatchMode.WatchAndPlay)&&window.QueueCount==0,"Nie odtworzono trybu Watch po ponownym utworzeniu okna.");}
    }
    loaded.Settings.watchMode="invalid";loaded.SaveSettings(loaded.Settings);Check(HistoryStore.LoadSettings(temp).watchMode=="Off","Nieprawidlowy tryb powinien wylaczac obserwacje.");
    // Zmiana trybu i natychmiastowe zamknięcie muszą opróżnić kolejkę zapisu.
    var shutdown=new Engine(temp,new string[0],root);shutdown.Start();var shutdownSettings=HistoryStore.LoadSettings(temp);shutdownSettings.watchMode="WatchAndPlay";shutdown.Command(()=>shutdown.Store.SaveSettings(shutdownSettings));Task.Run(()=>shutdown.Stop()).GetAwaiter().GetResult();Check(HistoryStore.LoadSettings(temp).watchMode=="WatchAndPlay","Zamkniecie zgubilo oczekujacy zapis trybu.");
    if(report!=null)Json.Save(report,new{ok=true,version="v0.12",parser=true,incremental=true,partialFrame=true,mp3Conversion=true,indexReload=true,pinRetention=true,expiryKeys=true,settingsCompatibility=true,titleBarSetting=true,autoTriggerSetting=true,watchModeRestart=true,watchModeInvalidFallback=true,watchModeShutdownFlush=true,pathGuard=true});
   }finally{DeleteTemp(temp);}
  }
  public static void Ui(string root,bool titlebar,string report){
   string temp=Temp();Exception failure=null;int ticks=0;double maxGap=0;DateTime previous=DateTime.UtcNow;
   var cache=Path.Combine(temp,"Cache_Data");Directory.CreateDirectory(cache);var source=Path.Combine(cache,"f_abcd");Fixture(root,source);var c=new Scanner().FromFile(source);
   using(var f=new MainWindow(temp,titlebar,new[]{cache},root)){f.TestMode=true;f.Player.Muted();var heartbeat=new System.Windows.Forms.Timer{Interval=25};heartbeat.Tick+=(s,e)=>{ticks++;maxGap=Math.Max(maxGap,(DateTime.UtcNow-previous).TotalMilliseconds);previous=DateTime.UtcNow;};
    f.Shown+=async(s,e)=>{try{
     previous=DateTime.UtcNow;heartbeat.Start();var convert=Converter.Convert(root,source,Path.Combine(cache,"ui.mp3"),"AAC",CancellationToken.None);await Task.Delay(400);await convert;
     Check(ticks>=8&&maxGap<350,"Petla okna nie reaguje w tle.");
     var historyReady=new TaskCompletionSource<Entry>();f.ReceivedSnapshot+=s2=>{var entry=s2.Entries.FirstOrDefault(x=>!String.IsNullOrEmpty(x.Mp3File));if(entry!=null)historyReady.TrySetResult(entry);};f.Engine.Start();
     Check(await Task.WhenAny(historyReady.Task,Task.Delay(15000))==historyReady.Task,"Automatyczny zapis historii nie zakonczyl sie.");
     var saved=await historyReady.Task;Check(System.Text.RegularExpressions.Regex.IsMatch(saved.Mp3File,"_\\d{4}-\\d{2}-\\d{2}_\\d{2}-\\d{2}-\\d{2}-\\d{3}\\.mp3$"),"Data i godzina automatycznego zapisu.");Check(f.Streams.Items.Count==1&&f.History.Items.Count==1,"Aktualizacja list z procesu tla.");
     // Check layout one variable at a time, without changing user data.
     foreach(var theme in new[]{"Dark","Light","INTEX"}){f.ApplyTheme(f,theme);await Task.Delay(50);Check(f.Seek.Height>=30&&!f.Seek.RectangleToScreen(f.Seek.ClientRectangle).IntersectsWith(f.Back.RectangleToScreen(f.Back.ClientRectangle)),"Uklad paska i przyciskow.");}
     Check(f.NextButton.Top==f.Back.Top&&f.NextButton.Right<=f.ClientSize.Width-10,"Przycisk Nastepny strumien nie miesci sie w jednym rzedzie.");
     f.ApplyTitleBar(false,false);Check(!f.TitleBar&&f.FormBorderStyle==FormBorderStyle.None,"Nie wlaczono belki INTEX.");f.ApplyTitleBar(true,false);Check(f.TitleBar&&f.FormBorderStyle==FormBorderStyle.Sizable,"Nie wlaczono systemowej belki.");f.ApplyTitleBar(titlebar,false);
     Check(f.FooterCenter.Text=="LAMoN 2026 AS"&&f.FooterVersion.Text.EndsWith("v0.12"),"Stopka.");
     await f.PlayCandidate(c,false,true);f.Player.Toggle();f.Player.Position=4;Check(Math.Abs(f.Player.Position-4)<.5,"Przewijanie do przodu.");f.Player.Position=1;Check(Math.Abs(f.Player.Position-1)<.5,"Przewijanie do tylu.");f.Player.Position=f.Player.Duration;f.Player.Position=0;Check(f.Player.Position<.5,"Powrot po koncu.");f.RefreshPlayback();Check(f.Seek.Enabled&&f.Back.Enabled,"Sterowanie nieaktywne.");
     await f.PlayHistory(saved);Check(f.Engine.ActiveHistoryId==saved.Id&&f.Player.Ready,"Klikniecie zapisanego streamu.");
     f.Preferences.minimizeTo="Tray";f.MinimizeWindow();Check(!f.Visible&&!f.ShowInTaskbar,"Minimalizacja do traya.");f.RestoreWindow();Check(f.Visible&&f.ShowInTaskbar,"Przywracanie z traya.");
     using(var bmp=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));if(report!=null)bmp.Save(report+".png");}
     if(report!=null)f.TestOptionsPreview(report+".options.png");
     await f.Engine.Stop();f.Player.Stop();
     for(int i=0;i<80;i++)f.Streams.Items.Add(new ListViewItem(new[]{"2026-11-01 07:00:00","15:24","8.50","Test wyglądu "+i}));
     f.Streams.Columns[3].Width=700;f.Streams.EnsureVisible(60);Check(f.Streams.TopItem.Index>0,"Lista nie przewija dlugiej historii.");
     var keyMethod=typeof(ThemeSeekBar).GetMethod("OnKeyDown",BindingFlags.NonPublic|BindingFlags.Instance);f.Seek.Enabled=true;f.Seek.Maximum=100;f.Seek.Value=50;keyMethod.Invoke(f.Seek,new object[]{new KeyEventArgs(Keys.Right)});Check(f.Seek.Value==51,"Nowy suwak nie reaguje na klawisz w prawo.");keyMethod.Invoke(f.Seek,new object[]{new KeyEventArgs(Keys.Home)});Check(f.Seek.Value==0,"Nowy suwak nie reaguje na Home.");keyMethod.Invoke(f.Seek,new object[]{new KeyEventArgs(Keys.End)});Check(f.Seek.Value==100,"Nowy suwak nie reaguje na End.");
     foreach(var theme in new[]{"Dark","Light","INTEX"}){
      f.Preferences.theme=theme;f.ApplyTheme(f,theme);await Task.Delay(100);
      if(report!=null){using(var bmp=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(report+"."+theme+".png");}f.TestOptionsPreview(report+"."+theme+".options.png");}
     }
    }catch(Exception ex){failure=ex;}finally{heartbeat.Stop();heartbeat.Dispose();f.Close();}};
    Application.Run(f);
   }
   try{if(failure!=null)throw failure;if(report!=null)Json.Save(report,new{ok=true,version="v0.12",uiTicks=ticks,maxUiGapMs=maxGap,backgroundHistory=true,timestamp=true,streamClick=true,historyClick=true,seek=true,returnAfterEnd=true,footer=true,themes=true,tray=true,nextButton=true,titleBarToggle=true});}finally{DeleteTemp(temp);}
  }
  public static void Tray(string root,bool titlebar,string report){
   string temp=Temp();Exception failure=null;int greenPixels=0,redPixels=0,yellowPixels=0;bool continued=false;
   string cache=Path.Combine(temp,"Cache_Data");Directory.CreateDirectory(cache);string old=Path.Combine(cache,"f_abcd");Fixture(root,old);File.SetLastWriteTimeUtc(old,DateTime.UtcNow.AddDays(-1));
   using(var f=new MainWindow(temp,titlebar,new[]{cache},root)){f.TestMode=true;f.Player.Muted();
    f.Shown+=async(s,e)=>{try{
     f.Preferences.minimizeTo="Tray";f.MinimizeWindow();int wait=SystemInformation.DoubleClickTime+150;
     greenPixels=IconPixels(f.TrayIconImage(),TrayObservation.Green);Check(greenPixels>100&&f.CurrentSignal==SignalState.Idle&&f.Indicator.State==SignalState.Idle,"Domyslna ikona nie jest zielona #001A06.");
     f.TestTrayClick(MouseButtons.Right,1);await Task.Delay(wait);Check(!f.ObservationArmed&&!f.TrayIsRed&&!f.Visible,"Prawy klik nie powinien uzbrajac.");
     f.TestTrayClick(MouseButtons.Left,1);await Task.Delay(wait);Check(f.ObservationArmed&&f.TrayIsRed&&!f.Visible&&f.Indicator.State==SignalState.Observing,"Pojedynczy klik nie uzbroil obserwacji w trayu.");
     redPixels=IconPixels(f.TrayIconImage(),TrayObservation.Red);Check(redPixels>100,"Ikona nie jest czerwona.");
     var first=new TaskCompletionSource<bool>();var detected=new TaskCompletionSource<bool>();bool waitingForNew=false;
     f.ReceivedSnapshot+=snap=>{if(snap.Candidates.Count>=1)first.TrySetResult(true);if(waitingForNew&&!f.ObservationArmed)detected.TrySetResult(true);};f.Engine.Start();
     Check(await Task.WhenAny(first.Task,Task.Delay(15000))==first.Task,"Nie otrzymano starego cache.");await Task.Delay(100);Check(f.ObservationArmed&&f.TrayIsRed,"Stary stream wylaczyl obserwacje.");
     string newer=Path.Combine(cache,"f_abce");File.Copy(old,newer);File.SetLastWriteTimeUtc(newer,DateTime.UtcNow);waitingForNew=true;
     Check(await Task.WhenAny(detected.Task,Task.Delay(15000))==detected.Task,"Nie wykryto nowego odczytu.");Check(f.CurrentSignal==SignalState.Active&&f.Indicator.State==SignalState.Active&&!f.Visible,"Po wykryciu nie ustawiono zoltego stanu.");
     yellowPixels=IconPixels(f.TrayIconImage(),TrayObservation.Yellow);Check(yellowPixels>100,"Ikona po wykryciu nie jest zolta.");
     DateTime deadline=DateTime.UtcNow.AddSeconds(10);while(!f.Player.Ready&&DateTime.UtcNow<deadline)await Task.Delay(100);
     Check(f.Player.Ready&&f.Player.File.Candidate.Path==newer,"Nie odtworzono nowego odczytu.");long originalBytes=f.Player.File.Bytes;double originalDuration=f.Player.Duration;
     deadline=DateTime.UtcNow.AddSeconds(10);while(!f.Player.AtEnd&&DateTime.UtcNow<deadline)await Task.Delay(100);Check(f.Player.AtEnd&&f.Player.WantPlay,"Odtwarzacz nie zachowal zamiaru gry po koncu fragmentu.");
     byte[] more=File.ReadAllBytes(old);using(var append=new FileStream(newer,FileMode.Append,FileAccess.Write,FileShare.ReadWrite))append.Write(more,0,more.Length);File.SetLastWriteTimeUtc(newer,DateTime.UtcNow);
     deadline=DateTime.UtcNow.AddSeconds(15);while((f.Player.File==null||f.Player.File.Bytes<=originalBytes||!f.Player.Playing)&&DateTime.UtcNow<deadline)await Task.Delay(100);
     continued=f.Player.File!=null&&f.Player.File.Bytes>originalBytes&&f.Player.Duration>originalDuration+5&&f.Player.Playing;Check(continued,"Odtwarzanie nie zostalo wznowione po powiekszeniu streamu.");
     deadline=DateTime.UtcNow.AddSeconds(15);while(f.CurrentSignal!=SignalState.Stable&&DateTime.UtcNow<deadline)await Task.Delay(100);Check(f.CurrentSignal==SignalState.Stable&&f.Indicator.State==SignalState.Stable,"Brak zielonego stanu po stabilizacji streamu.");
     greenPixels=IconPixels(f.TrayIconImage(),TrayObservation.Green);Check(greenPixels>100,"Ikona stabilnego streamu nie jest zielona.");f.Player.Stop();
     f.TestTrayClick(MouseButtons.Left,1);f.TestTrayDoubleClick();f.TestTrayClick(MouseButtons.Left,1);await Task.Delay(wait);
     Check(f.Visible&&f.ShowInTaskbar&&f.TrayVisible&&!f.ObservationArmed&&!f.TrayIsRed,"Dwuklik ukryl ikone albo uzbroil obserwacje.");
     await Task.Delay(SystemInformation.DoubleClickTime);f.TestTrayClick(MouseButtons.Left,1);await Task.Delay(wait);Check(f.TrayVisible&&f.ObservationArmed&&f.TrayIsRed,"Ikona po przywroceniu nie reaguje na pojedynczy klik.");
    }catch(Exception ex){failure=ex;}finally{f.Close();}};
    Application.Run(f);
   }
   try{if(failure!=null)throw failure;if(report!=null)Json.Save(report,new{ok=true,version="v0.12",singleClickArms=true,greenPixels=greenPixels,redPixels=redPixels,yellowPixels=yellowPixels,rightClickDoesNotArm=true,oldStreamIgnored=true,newStreamDetected=true,playbackContinued=continued,stableSignal=true,trayPersistsAfterRestore=true,singleClickWorksWhenVisible=true,doubleClickRestoresOnly=true});}finally{DeleteTemp(temp);}
  }
  public static void Watch(string root,bool titlebar,string report){
   string temp=Temp();Exception failure=null;bool watchSilent=false,watchArchived=false,historyProtected=false,queuedWithoutInterrupt=false,stopHeld=false,nextWorked=false,naturalAdvance=false;
   string cache=Path.Combine(temp,"Cache_Data");Directory.CreateDirectory(cache);string fixture=Path.Combine(cache,"fixture.aac");Fixture(root,fixture);
   string old=Path.Combine(cache,"f_1000");File.Copy(fixture,old);File.SetLastWriteTimeUtc(old,DateTime.UtcNow.AddDays(-1));
   string historyFolder=Path.Combine(temp,"history");Directory.CreateDirectory(historyFolder);string manualMp3=Path.Combine(historyFolder,"manual.mp3");Converter.Convert(root,fixture,manualMp3,"AAC",CancellationToken.None).GetAwaiter().GetResult();var manualEntry=new Entry{Id=Guid.NewGuid().ToString("N"),Mp3File="manual.mp3",Seconds=6};
   using(var f=new MainWindow(temp,titlebar,new[]{cache},root)){f.TestMode=true;f.Player.Muted();
    f.Shown+=async(s,e)=>{try{
     string watchTarget=null;var first=new TaskCompletionSource<bool>();f.ReceivedSnapshot+=snap=>{if(snap.Candidates.Any(x=>x.Path==old))first.TrySetResult(true);if(watchTarget!=null&&snap.Entries.Any(x=>x.SourcePath==watchTarget))watchArchived=true;};f.Engine.Start();
     Check(await Task.WhenAny(first.Task,Task.Delay(15000))==first.Task,"Nie otrzymano poczatkowego snapshotu.");
     f.TestSetWatchMode(ContinuousWatchMode.Watch);Check(f.WatchMenuChecked&&!f.WatchPlayMenuChecked&&f.WatchMode==ContinuousWatchMode.Watch,"Menu Watch nie jest wylaczne.");await Task.Delay(150);
     string watched=Path.Combine(cache,"f_1001");watchTarget=watched;File.Copy(fixture,watched);File.SetLastWriteTimeUtc(watched,DateTime.UtcNow);
     DateTime deadline=DateTime.UtcNow.AddSeconds(8);while((f.CurrentSignal==SignalState.Observing||!watchArchived)&&DateTime.UtcNow<deadline)await Task.Delay(100);
     watchSilent=!f.Player.Ready&&f.QueueCount==0;Check(watchSilent,"Watch uruchomil odtwarzanie albo dodal kolejke.");Check(watchArchived,"Watch nie przekazal streamu do archiwizacji.");
     await f.PlayHistory(manualEntry);Check(f.Player.Ready&&f.Player.Playing&&f.Player.File.Candidate==null,"Nie uruchomiono kontrolnego nagrania z historii.");
     f.TestSetWatchMode(ContinuousWatchMode.WatchAndPlay);Check(!f.WatchMenuChecked&&f.WatchPlayMenuChecked&&f.WatchMode==ContinuousWatchMode.WatchAndPlay,"Menu Watch and Play nie jest wylaczne.");await Task.Delay(150);
     string one=Path.Combine(cache,"f_1002");File.Copy(fixture,one);File.SetLastWriteTimeUtc(one,DateTime.UtcNow);
     deadline=DateTime.UtcNow.AddSeconds(8);while(f.QueueCount<1&&DateTime.UtcNow<deadline)await Task.Delay(100);
     historyProtected=f.QueueCount==1&&f.Player.Ready&&f.Player.Playing&&f.Player.File.Candidate==null;Check(historyProtected,"Nowy stream przerwal nagranie z historii.");
     await f.NextStream();deadline=DateTime.UtcNow.AddSeconds(12);while((!f.Player.Ready||f.Player.File==null||f.Player.File.Candidate==null||f.Player.File.Candidate.Path!=one)&&DateTime.UtcNow<deadline)await Task.Delay(100);
     Check(f.Player.Ready&&f.Player.Playing&&f.Player.File.Candidate.Path==one,"Pierwszy stream nie wystartowal po przycisku Nastepny.");
     string two=Path.Combine(cache,"f_1003");File.Copy(fixture,two);File.SetLastWriteTimeUtc(two,DateTime.UtcNow);
     deadline=DateTime.UtcNow.AddSeconds(8);while(f.QueueCount<1&&DateTime.UtcNow<deadline)await Task.Delay(100);
     queuedWithoutInterrupt=f.QueueCount==1&&f.Player.File.Candidate.Path==one&&f.Player.Playing;Check(queuedWithoutInterrupt,"Drugi stream przerwal pierwszy albo nie trafil do kolejki.");
     f.StopLocal();await Task.Delay(2000);stopHeld=!f.Player.Playing&&f.QueueCount==1&&(f.Player.File==null||f.Player.File.Candidate.Path==one);Check(stopHeld,"Stop lokalnie uruchomil kolejny stream.");
     await f.NextStream();deadline=DateTime.UtcNow.AddSeconds(12);while((!f.Player.Ready||f.Player.File==null||f.Player.File.Candidate==null||f.Player.File.Candidate.Path!=two)&&DateTime.UtcNow<deadline)await Task.Delay(100);
     nextWorked=f.Player.Playing&&f.Player.File.Candidate.Path==two;Check(nextWorked,"Nastepny strumien nie uruchomil pozycji z kolejki.");
     string three=Path.Combine(cache,"f_1004");File.Copy(fixture,three);File.SetLastWriteTimeUtc(three,DateTime.UtcNow);
     deadline=DateTime.UtcNow.AddSeconds(8);while(f.QueueCount<1&&DateTime.UtcNow<deadline)await Task.Delay(100);Check(f.QueueCount==1&&f.Player.File.Candidate.Path==two,"Trzeci stream nie czeka w kolejce.");
     File.SetLastWriteTimeUtc(two,DateTime.UtcNow.AddSeconds(-20));
     deadline=DateTime.UtcNow.AddSeconds(15);while((!f.Player.Ready||!f.Player.Playing||f.Player.File==null||f.Player.File.Candidate==null||f.Player.File.Candidate.Path!=three)&&DateTime.UtcNow<deadline)await Task.Delay(100);
     naturalAdvance=f.Player.Ready&&f.Player.Playing&&f.Player.File!=null&&f.Player.File.Candidate!=null&&f.Player.File.Candidate.Path==three;Check(naturalAdvance,"Naturalny koniec nie uruchomil kolejnego streamu. ready="+f.Player.Ready+", playing="+f.Player.Playing+", atEnd="+f.Player.AtEnd+", want="+f.Player.WantPlay+", live="+f.Player.Live+", queue="+f.QueueCount+", file="+(f.Player.File==null?"null":f.Player.File.Candidate==null?f.Player.File.Path:f.Player.File.Candidate.Path));
     await f.NextStream();Check(!f.Player.Ready&&f.QueueCount==0,"Pusty przycisk Nastepny nie zatrzymal odtwarzania.");
    }catch(Exception ex){failure=ex;}finally{f.Close();}};Application.Run(f);
   }
   try{if(failure!=null)throw failure;if(report!=null)Json.Save(report,new{ok=true,version="v0.12",watchSilent=watchSilent,watchArchived=watchArchived,historyNotInterrupted=historyProtected,exclusiveMenuChecks=true,queuedWithoutInterrupt=queuedWithoutInterrupt,stopHeldQueue=stopHeld,nextButtonWorked=nextWorked,naturalAdvance=naturalAdvance,emptyNextStops=true});}finally{DeleteTemp(temp);}
  }
 }
}

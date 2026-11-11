using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Intex {
 public sealed class Settings {
  public string theme="INTEX",minimizeTo="Taskbar",watchMode="Off";public int retentionDays=30;public bool titleBar,autoReadAloud;
  public Settings Clone(){return (Settings)MemberwiseClone();}
 }
 public sealed class Entry {
  public string Id,SourceKey,SourcePath,SourceName,Format,LastGrowthUtc,FirstSeenUtc,Mp3File="",SavedAtUtc="",LastAttemptUtc="",Error="";
  public long CopiedBytes,SourceBytes,ExportedBytes;public double Seconds;public bool Pinned,Partial=true,Unavailable;
  public List<string> PreviousMp3Files=new List<string>();
  public Entry Clone(){var e=(Entry)MemberwiseClone();e.PreviousMp3Files=new List<string>(PreviousMp3Files??new List<string>());return e;}
 }
 public static class Json {
  public static T Read<T>(string path){return new JavaScriptSerializer{MaxJsonLength=16000000}.Deserialize<T>(File.ReadAllText(path,Encoding.UTF8));}
  public static void Save(string path,object value){
   string temp=path+".tmp-"+Guid.NewGuid().ToString("N");
   try{File.WriteAllText(temp,new JavaScriptSerializer{MaxJsonLength=16000000}.Serialize(value),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
  }
 }
 public sealed class HistoryStore {
  public readonly string Root,History,Raw,Index;
  public readonly Dictionary<string,Entry> Entries=new Dictionary<string,Entry>();
  public Dictionary<string,long> Expired=new Dictionary<string,long>();
  public Settings Settings; Dictionary<string,object> extraSettings;
  public string Warning="";
  public HistoryStore(string root){Root=Path.GetFullPath(root);History=Path.Combine(Root,"history");Raw=Path.Combine(Root,"history-cache");Index=Path.Combine(Root,"history-index");Settings=LoadSettings(root);}
  public static Settings LoadSettings(string root){try{var s=Json.Read<Settings>(Path.Combine(root,"intex-settings.json"));if(s==null)throw new IOException();if(s.theme!="INTEX"&&s.theme!="Dark"&&s.theme!="Light")s.theme="INTEX";if(s.minimizeTo!="Tray")s.minimizeTo="Taskbar";if(s.watchMode!="Watch"&&s.watchMode!="WatchAndPlay")s.watchMode="Off";if(s.retentionDays<0||s.retentionDays>36500)s.retentionDays=30;return s;}catch{return new Settings();}}
  public void Initialize(){
   foreach(var d in new[]{History,Raw,Index}){Directory.CreateDirectory(d);if((File.GetAttributes(d)&FileAttributes.ReparsePoint)!=0)throw new IOException("Folder historii jest dowiazaniem: "+d);}
   string prefs=Path.Combine(Root,"intex-settings.json");if(File.Exists(prefs))try{extraSettings=Json.Read<Dictionary<string,object>>(prefs);}catch{extraSettings=new Dictionary<string,object>();}
   if(extraSettings==null)extraSettings=new Dictionary<string,object>();
   var expired=Owned(Index,"expired-keys.json");if(File.Exists(expired))Expired=Json.Read<Dictionary<string,long>>(expired)??new Dictionary<string,long>();
   foreach(var file in Directory.EnumerateFiles(Index,"*.json")){
    if(!Regex.IsMatch(Path.GetFileNameWithoutExtension(file),"^[0-9a-f]{32}$"))continue;
    try{var e=Json.Read<Entry>(file);if(e!=null&&e.Id==Path.GetFileNameWithoutExtension(file)&&Regex.IsMatch(e.Id,"^[0-9a-f]{32}$")){if(e.PreviousMp3Files==null)e.PreviousMp3Files=new List<string>();if(e.Format!="AAC"&&e.Format!="MP3")continue;Entries[e.Id]=e;}}
    catch(Exception ex){Warning="Pominieto uszkodzony indeks "+Path.GetFileName(file)+": "+ex.Message;}
   }
  }
  public static string Owned(string root,string leaf){
   if(String.IsNullOrWhiteSpace(leaf)||Path.GetFileName(leaf)!=leaf||leaf=="."||leaf==".."||leaf.IndexOf(':')>=0)throw new IOException("Nieprawidlowa nazwa pliku historii.");
   if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Folder jest dowiazaniem.");
   var p=Path.Combine(root,leaf);if(File.Exists(p)&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Plik jest dowiazaniem.");return p;
  }
  public static DateTime Date(string text){DateTime d;return DateTime.TryParse(text,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out d)?d.ToUniversalTime():DateTime.UtcNow;}
  public void Save(Entry e){if(!Regex.IsMatch(e.Id,"^[0-9a-f]{32}$"))throw new IOException("Nieprawidlowy identyfikator.");Json.Save(Owned(Index,e.Id+".json"),e);}
  public void SaveSettings(Settings s){Settings=s.Clone();extraSettings["theme"]=s.theme;extraSettings["minimizeTo"]=s.minimizeTo;extraSettings["retentionDays"]=s.retentionDays;extraSettings["titleBar"]=s.titleBar;extraSettings["autoReadAloud"]=s.autoReadAloud;extraSettings["watchMode"]=s.watchMode;Json.Save(Path.Combine(Root,"intex-settings.json"),extraSettings);}
  public Entry Capture(Candidate c,AudioReader reader,CancellationToken token){
   var info=reader.Inspect(c.Path);if(info.Invalid||info.Format==null||info.Seconds<2.4)return null;
   string key=AudioReader.Key(c.Path);long expired;if(Expired.TryGetValue(key,out expired)&&info.CompleteBytes<=expired)return null;
   var e=Entries.Values.FirstOrDefault(x=>x.SourceKey==key&&x.CopiedBytes<=info.CompleteBytes);
   if(e==null){string now=DateTime.UtcNow.ToString("o");e=new Entry{Id=Guid.NewGuid().ToString("N"),SourceKey=key,SourcePath=c.Path,SourceName=c.Name,Format=info.Format,LastGrowthUtc=now,FirstSeenUtc=now,Pinned=c.ManualArchive};Entries[e.Id]=e;}
   bool changed=false;var raw=Owned(Raw,e.Id+"."+e.Format.ToLowerInvariant());
   long actual=File.Exists(raw)?new FileInfo(raw).Length:0;
   // A crash can leave a partially appended raw file ahead of its persisted index.
   if(actual!=e.CopiedBytes){using(var f=new FileStream(raw,FileMode.OpenOrCreate,FileAccess.Write,FileShare.Read))f.SetLength(Math.Min(actual,e.CopiedBytes));if(actual<e.CopiedBytes){e.CopiedBytes=0;using(var f=new FileStream(raw,FileMode.OpenOrCreate,FileAccess.Write,FileShare.Read))f.SetLength(0);}}
   if(info.CompleteBytes>e.CopiedBytes){AudioReader.Copy(c.Path,raw,e.CopiedBytes,info.CompleteBytes,token);e.CopiedBytes=info.CompleteBytes;e.Seconds=info.Seconds;e.LastGrowthUtc=DateTime.UtcNow.ToString("o");e.Error="";changed=true;}
   bool partial=info.Bytes!=info.CompleteBytes||(DateTime.UtcNow-Date(e.LastGrowthUtc)).TotalSeconds<10;
   changed|=partial!=e.Partial||e.SourceBytes!=info.Bytes||e.Unavailable;e.Partial=partial;e.SourceBytes=info.Bytes;e.Unavailable=false;
   if(changed)Save(e);return e;
  }
  public void Pin(string id,bool pinned){Entry e;if(Entries.TryGetValue(id,out e)){e.Pinned=pinned;Save(e);}}
  public void Cleanup(string activeId,string exportingId){
   if(Settings.retentionDays==0)return;var cutoff=DateTime.UtcNow.AddDays(-Settings.retentionDays);
   foreach(var e in Entries.Values.ToArray()){
    if(e.Pinned||e.Id==activeId||e.Id==exportingId||Date(String.IsNullOrEmpty(e.SavedAtUtc)?e.FirstSeenUtc:e.SavedAtUtc)>=cutoff)continue;
    Expired[e.SourceKey]=e.CopiedBytes;Json.Save(Owned(Index,"expired-keys.json"),Expired);
    foreach(var leaf in new[]{e.Mp3File}.Concat(e.PreviousMp3Files))if(!String.IsNullOrEmpty(leaf))File.Delete(Owned(History,leaf));
    File.Delete(Owned(Raw,e.Id+"."+e.Format.ToLowerInvariant()));File.Delete(Owned(Index,e.Id+".json"));Entries.Remove(e.Id);
   }
  }
 }
 public sealed class Snapshot {public List<Candidate> Candidates;public List<Entry> Entries;public string Warning;public double ScanMs;public int PendingExports;}
 public sealed class ExportResult {public string Id,Input,Output;public long Bytes;public Task Task;}
 public sealed class Engine {
  public readonly Scanner Scanner;public readonly HistoryStore Store;readonly string toolsRoot;
  readonly ConcurrentQueue<Action> commands=new ConcurrentQueue<Action>();readonly CancellationTokenSource stop=new CancellationTokenSource();Task loop;
  ExportResult job;public volatile string ActiveHistoryId;public event Action<Snapshot> Updated;
  public Engine(string root,IEnumerable<string> cacheDirectories=null,string dependencyRoot=null){Store=new HistoryStore(root);Scanner=new Scanner(cacheDirectories);toolsRoot=dependencyRoot??root;}
  public void Command(Action a){commands.Enqueue(a);}
  public void Start(){loop=Task.Run((Func<Task>)Run);}
  public async Task Stop(){stop.Cancel();if(loop!=null)await loop;}
  async Task Run(){
   try{
    Store.Initialize();DateTime imported=DateTime.MinValue,cleanup=DateTime.MinValue;
    while(!stop.IsCancellationRequested){
     Action command;while(commands.TryDequeue(out command)){try{command();}catch(Exception ex){Store.Warning=ex.Message;}}
     var watch=System.Diagnostics.Stopwatch.StartNew();var candidates=Scanner.Scan();
     foreach(var c in candidates){try{Store.Capture(c,Scanner.Reader,stop.Token);}catch(OperationCanceledException){throw;}catch(Exception ex){Store.Warning=ex.Message;}}
     if((DateTime.UtcNow-imported).TotalSeconds>=60){Publish(candidates,watch.Elapsed.TotalMilliseconds);foreach(var c in Scanner.Legacy(Store.Root)){try{Store.Capture(c,Scanner.Reader,stop.Token);}catch(OperationCanceledException){throw;}catch(Exception ex){Store.Warning=ex.Message;}while(commands.TryDequeue(out command)){try{command();}catch(Exception ex){Store.Warning=ex.Message;}}}imported=DateTime.UtcNow;}
     FinishExport();if(job==null){var e=Store.Entries.Values.OrderBy(x=>x.FirstSeenUtc).FirstOrDefault(x=>x.CopiedBytes>x.ExportedBytes&&(String.IsNullOrEmpty(x.LastAttemptUtc)||(DateTime.UtcNow-HistoryStore.Date(x.LastAttemptUtc)).TotalSeconds>=60||(DateTime.UtcNow-HistoryStore.Date(x.LastGrowthUtc)).TotalSeconds>=10&&(DateTime.UtcNow-HistoryStore.Date(x.LastAttemptUtc)).TotalSeconds>=10));if(e!=null)try{StartExport(e);}catch(OperationCanceledException){throw;}catch(Exception ex){e.Error=ex.Message;e.LastAttemptUtc=DateTime.UtcNow.ToString("o");Store.Save(e);}}
     if((DateTime.UtcNow-cleanup).TotalSeconds>=60){try{Store.Cleanup(ActiveHistoryId,job==null?null:job.Id);}catch(Exception ex){Store.Warning="Porzadkowanie historii: "+ex.Message;}cleanup=DateTime.UtcNow;}
     watch.Stop();Publish(candidates,watch.Elapsed.TotalMilliseconds);
     await Task.Delay(1500,stop.Token);
    }
   }catch(OperationCanceledException){}catch(Exception ex){var update=Updated;if(update!=null)update(new Snapshot{Candidates=new List<Candidate>(),Entries=new List<Entry>(),Warning="Blad procesu tla: "+ex.Message});}
   // Zamknięcie zaraz po wyborze trybu nie może zgubić ostatniego zapisu ustawień.
   Action pending;while(commands.TryDequeue(out pending)){try{pending();}catch(Exception ex){Store.Warning=ex.Message;}}
   if(job!=null){try{await job.Task;}catch{}try{FinishExport();}catch{}if(job!=null)RemoveJobFiles(job);}
  }
  void Publish(List<Candidate> candidates,double ms){var update=Updated;if(update!=null)update(new Snapshot{Candidates=candidates,Entries=Store.Entries.Values.Select(x=>x.Clone()).OrderByDescending(x=>x.FirstSeenUtc).ToList(),Warning=Store.Warning,ScanMs=ms,PendingExports=Store.Entries.Values.Count(x=>x.CopiedBytes>x.ExportedBytes)});}
  void StartExport(Entry e){
   string token=Guid.NewGuid().ToString("N");var raw=HistoryStore.Owned(Store.Raw,e.Id+"."+e.Format.ToLowerInvariant());
   var j=new ExportResult{Id=e.Id,Bytes=e.CopiedBytes,Input=HistoryStore.Owned(Store.Raw,"job-"+token+"."+e.Format.ToLowerInvariant()),Output=HistoryStore.Owned(Store.History,"job-"+token+".tmp.mp3")};
   try{AudioReader.Copy(raw,j.Input,0,e.CopiedBytes,stop.Token);e.LastAttemptUtc=DateTime.UtcNow.ToString("o");Store.Save(e);j.Task=Converter.Convert(toolsRoot,j.Input,j.Output,e.Format,stop.Token);job=j;}catch{RemoveJobFiles(j);throw;}
  }
  void FinishExport(){
   if(job==null||!job.Task.IsCompleted)return;var j=job;var e=Store.Entries[j.Id];
   try{
    j.Task.GetAwaiter().GetResult();if(j.Bytes<e.ExportedBytes)throw new IOException("Nie zastapie nagrania krotsza kopia.");
    string name=Regex.Replace(e.SourceName??"audio","[^a-zA-Z0-9_-]","_");if(name.Length>48)name=name.Substring(0,48);
    string leaf="ReadAloud_"+name+"_"+e.Id.Substring(0,8)+"_"+DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff")+".mp3";
    File.Move(j.Output,HistoryStore.Owned(Store.History,leaf));if(!String.IsNullOrEmpty(e.Mp3File))e.PreviousMp3Files.Add(e.Mp3File);
    e.Mp3File=leaf;e.SavedAtUtc=DateTime.UtcNow.ToString("o");e.ExportedBytes=j.Bytes;e.Error="";Store.Save(e);
    if(!e.Pinned&&e.Id!=ActiveHistoryId){var remaining=new List<string>();foreach(var old in e.PreviousMp3Files)try{File.Delete(HistoryStore.Owned(Store.History,old));}catch(IOException){remaining.Add(old);}e.PreviousMp3Files=remaining;Store.Save(e);}
   }catch(OperationCanceledException){}catch(Exception ex){e.Error=ex.Message;Store.Save(e);}finally{RemoveJobFiles(j);job=null;}
  }
  static void RemoveJobFiles(ExportResult j){foreach(var p in new[]{j.Input,j.Output})try{File.Delete(p);}catch(IOException){}}
 }
}

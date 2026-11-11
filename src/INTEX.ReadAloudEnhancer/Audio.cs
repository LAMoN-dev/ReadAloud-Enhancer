using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Intex {
 public sealed class AudioInfo {
  public string Format; public long Bytes,CompleteBytes; public double Seconds; public int Frames; public bool Invalid;
  public AudioInfo Clone(){return (AudioInfo)MemberwiseClone();}
 }
 public sealed class AudioReader {
  sealed class Cache {public long Length,Ticks; public AudioInfo Info;}
  readonly Dictionary<string,Cache> cache=new Dictionary<string,Cache>(StringComparer.OrdinalIgnoreCase);
  public long ParsedBytes;
  public AudioInfo Inspect(string path){
   var file=new FileInfo(path); Cache old;
   lock(cache){if(cache.TryGetValue(path,out old)&&old.Length==file.Length&&old.Ticks==file.LastWriteTimeUtc.Ticks)return old.Info.Clone();}
   // Resume only complete, previously validated frames when the file grows.
   var seed=old!=null&&!old.Info.Invalid&&file.Length>old.Length?old.Info:null;
   AudioInfo info=Parse(path,seed);
   lock(cache){cache[path]=new Cache{Length=file.Length,Ticks=file.LastWriteTimeUtc.Ticks,Info=info};}
   return info.Clone();
  }
  public AudioInfo Parse(string path,AudioInfo seed=null){
   using(var raw=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
   using(var f=new BufferedStream(raw,262144)){
    var r=seed==null?new AudioInfo():seed.Clone();r.Bytes=raw.Length;r.Invalid=false;
    if(r.Bytes<7)return r;
    byte[] h=new byte[7];Read(f,h,7);bool mp3=h[0]==255&&h[1]==243&&h[2]==228;
    bool aac=h[0]==255&&(h[1]&246)==240;
    if(!mp3&&!aac)return new AudioInfo{Bytes=r.Bytes};
    r.Format=mp3?"MP3":"AAC";
    if(seed!=null&&seed.Format!=r.Format)r=new AudioInfo{Bytes=raw.Length,Format=r.Format};
    long p=r.CompleteBytes;f.Position=p;byte[] payload=new byte[8192];
    int[] rates={96000,88200,64000,48000,44100,32000,24000,22050,16000,12000,11025,8000,7350};
    while(p<r.Bytes){
     int header=mp3?4:7;if(p+header>r.Bytes)break;
     if(Read(f,h,header)!=header)break;int len;double seconds;
     if(mp3){if(h[0]!=255||h[1]!=243||h[2]!=228){r.Invalid=true;break;}len=480;seconds=.024;}
     else {if(h[0]!=255||(h[1]&246)!=240){r.Invalid=true;break;}
      int index=(h[2]>>2)&15;len=((h[3]&3)<<11)|(h[4]<<3)|(h[5]>>5);
      if(index>=rates.Length||len<((h[1]&1)==1?7:9)){r.Invalid=true;break;}
      seconds=1024.0*(1+(h[6]&3))/rates[index];}
     if(p+len>r.Bytes||Read(f,payload,len-header)!=len-header)break;p+=len;r.Frames++;r.Seconds+=seconds;
    }
    Interlocked.Add(ref ParsedBytes,p-r.CompleteBytes);r.CompleteBytes=p;return r;
   }
  }
  static int Read(Stream s,byte[] b,int n){int p=0,k;while(p<n&&(k=s.Read(b,p,n-p))>0)p+=k;return p;}
  public static bool Header(string path){using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
   byte[] h=new byte[4];if(f.Length<4800||Read(f,h,4)!=4)return false;
   return h[0]==255&&((h[1]==243&&h[2]==228)||(h[1]&246)==240);
  }}
  public static string Key(string path){using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var hash=SHA256.Create()){
   byte[] b=new byte[128];int n=Read(f,b,128);return path.ToLowerInvariant()+":"+BitConverter.ToString(hash.ComputeHash(b,0,n));
  }}
  public static void Copy(string source,string target,long start,long end,CancellationToken token){
   using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
   using(var output=new FileStream(target,FileMode.OpenOrCreate,FileAccess.Write,FileShare.Read)){
    if(output.Length!=start)throw new IOException("Rozmiar bufora nie zgadza sie z indeksem.");
    input.Position=start;output.Position=start;var b=new byte[262144];
    while(start<end){token.ThrowIfCancellationRequested();int n=input.Read(b,0,(int)Math.Min(b.Length,end-start));if(n==0)throw new IOException("Zrodlo zmienilo sie w trakcie kopiowania.");output.Write(b,0,n);start+=n;}
   }
  }
 }
 public sealed class Candidate {
  public string Path,Name,Format; public long Length,CompleteBytes,Ticks;public double Seconds;public DateTime ModifiedUtc;public bool ManualArchive;
 }
 public sealed class Scanner {
  public readonly AudioReader Reader=new AudioReader();
  readonly List<string> fixedDirectories;
  public Scanner(IEnumerable<string> cacheDirectories=null){if(cacheDirectories!=null)fixedDirectories=cacheDirectories.ToList();}
  List<string> directories=new List<string>();DateTime discovered=DateTime.MinValue;
  public static IEnumerable<string> CacheDirectories(){
   var roots=new List<string>{System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Codex","web"),System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"ChatGPT")};
   var packages=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Packages");
   if(Directory.Exists(packages))foreach(var p in Directory.EnumerateDirectories(packages,"OpenAI.*")){var name=System.IO.Path.GetFileName(p);if(name.StartsWith("OpenAI.ChatGPT")||name.StartsWith("OpenAI.Codex"))roots.Add(System.IO.Path.Combine(p,"LocalCache","Roaming"));}
   var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var root in roots)Walk(root,result);return result;
  }
  static void Walk(string root,HashSet<string> result){
   try{if(!Directory.Exists(root)||(File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)return;
    if(System.IO.Path.GetFileName(root)=="Cache_Data"){result.Add(root);return;}
    foreach(var d in Directory.EnumerateDirectories(root))Walk(d,result);
   }catch(IOException){}catch(UnauthorizedAccessException){}
  }
  public List<Candidate> Scan(){
   if(fixedDirectories!=null)directories=fixedDirectories;
   else if((DateTime.UtcNow-discovered).TotalSeconds>60){directories=CacheDirectories().ToList();discovered=DateTime.UtcNow;}
   var result=new List<Candidate>();foreach(var dir in directories){try{foreach(var p in Directory.EnumerateFiles(dir,"f_*")){try{if(!AudioReader.Header(p))continue;var c=FromFile(p);if(c!=null)result.Add(c);}catch(IOException){}catch(UnauthorizedAccessException){}}}catch(IOException){}catch(UnauthorizedAccessException){}}
   return result.OrderByDescending(c=>c.ModifiedUtc).ToList();
  }
  public Candidate FromFile(string p,bool manual=false){var info=Reader.Inspect(p);if(info.Format==null||info.Invalid||info.Seconds<2.4)return null;var f=new FileInfo(p);return new Candidate{Path=f.FullName,Name=f.Name,Format=info.Format,Length=info.Bytes,CompleteBytes=info.CompleteBytes,Seconds=info.Seconds,ModifiedUtc=f.LastWriteTimeUtc,Ticks=f.LastWriteTimeUtc.Ticks,ManualArchive=manual};}
  public IEnumerable<Candidate> Legacy(string root){
   var paths=new List<string>();foreach(var dir in new[]{System.IO.Path.Combine(root,"buffer"),System.IO.Path.Combine(root,"cache-recovery")})if(Directory.Exists(dir))paths.AddRange(Directory.EnumerateFiles(dir).Where(p=>!System.IO.Path.GetFileName(p).StartsWith("play-")&&(p.EndsWith(".mp3",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".aac",StringComparison.OrdinalIgnoreCase))));
   paths.AddRange(Directory.EnumerateFiles(root,"*.mp3"));foreach(var p in paths){Candidate c=null;try{c=FromFile(p,System.IO.Path.GetDirectoryName(p).Equals(root,StringComparison.OrdinalIgnoreCase));}catch(IOException){}if(c!=null)yield return c;}
  }
 }
 public static class Converter {
  public static async Task Convert(string root,string source,string target,string format,CancellationToken token){
   token.ThrowIfCancellationRequested();
   if(format=="MP3"){await Task.Run(()=>AudioReader.Copy(source,target,0,new FileInfo(source).Length,token),token);return;}
   var start=new ProcessStartInfo(System.IO.Path.Combine(root,"tools","ffmpeg.exe"),"-nostdin -hide_banner -loglevel error -xerror -y -i \""+source+"\" -map 0:a:0 -vn -c:a libmp3lame -ar 24000 -ac 1 -b:a 160k -write_xing 0 -id3v2_version 0 -f mp3 \""+target+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
   using(var p=new Process{StartInfo=start}){
    p.Start();var error=p.StandardError.ReadToEndAsync();
    using(token.Register(()=>{try{if(!p.HasExited)p.Kill();}catch(InvalidOperationException){}})){
     await Task.Run(()=>{if(!p.WaitForExit(300000)){p.Kill();throw new TimeoutException("Konwersja przekroczyla 5 minut.");}});
     string message=await error;token.ThrowIfCancellationRequested();if(p.ExitCode!=0)throw new IOException("Blad konwersji MP3: "+message);
    }
   }
   var reader=new AudioReader();var a=reader.Parse(source);var b=reader.Parse(target);
   if(b.Invalid||b.Format!="MP3"||b.CompleteBytes!=b.Bytes||Math.Abs(b.Seconds-a.Seconds)>.2)throw new IOException("Nieprawidlowy MP3 lub niezgodna dlugosc.");
  }
 }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace Intex {
 public sealed class PlaybackFile {
  public string Path;public bool Temporary;public Candidate Candidate;public long Bytes;public double Seconds;
 }
 public sealed class Player : IDisposable {
  readonly MediaPlayer media=new MediaPlayer();readonly string root,toolsRoot;
  readonly List<string> snapshots=new List<string>();CancellationTokenSource request=new CancellationTokenSource();TaskCompletionSource<bool> opened;
  public PlaybackFile File;public bool Playing,WantPlay,Ready,Live,AtEnd;public double Duration;public event Action Changed;
  public Player(string root,string dependencyRoot=null){this.root=root;toolsRoot=dependencyRoot??root;media.Volume=1;
   media.MediaOpened+=(s,e)=>{Ready=true;Duration=media.NaturalDuration.HasTimeSpan?media.NaturalDuration.TimeSpan.TotalSeconds:File.Seconds;if(opened!=null)opened.TrySetResult(true);Raise();};
   media.MediaFailed+=(s,e)=>{if(opened!=null)opened.TrySetException(e.ErrorException);Ready=false;Playing=WantPlay=AtEnd=false;Raise();};
   media.MediaEnded+=(s,e)=>{AtEnd=true;Playing=false;Raise();};
  }
  void Raise(){var changed=Changed;if(changed!=null)changed();}
  public double Position {get{return media.Position.TotalSeconds;}set{media.Position=TimeSpan.FromSeconds(Math.Max(0,Math.Min(Duration,value)));}}
  public void Toggle(){if(!Ready)return;if(Playing){media.Pause();Playing=WantPlay=false;}else{if(AtEnd||Position>=Duration-.1)Position=0;AtEnd=false;media.Play();Playing=WantPlay=true;}Raise();}
  public void Stop(){media.Stop();Playing=WantPlay=AtEnd=false;Raise();}
  public async Task<PlaybackFile> Prepare(Candidate candidate,bool waitStable,CancellationToken token){
   return await Task.Run(async ()=>{
    var reader=new AudioReader();var c=candidate;
    if(waitStable){long length=-1,ticks=-1;DateTime stable=DateTime.UtcNow;
     while(true){token.ThrowIfCancellationRequested();var f=new FileInfo(c.Path);if(f.Length!=length||f.LastWriteTimeUtc.Ticks!=ticks){length=f.Length;ticks=f.LastWriteTimeUtc.Ticks;stable=DateTime.UtcNow;}if((DateTime.UtcNow-stable).TotalSeconds>=3)break;await Task.Delay(250,token);}
    }
    var info=reader.Parse(c.Path);if(info.Invalid||info.Seconds<2.4)throw new IOException("Za malo poprawnego audio; potrzeba 2,4 sekundy.");
    string dir=System.IO.Path.Combine(root,"buffer");Directory.CreateDirectory(dir);string tokenName=Guid.NewGuid().ToString("N");
    string raw=HistoryStore.Owned(dir,"play-v08-"+tokenName+"."+info.Format.ToLowerInvariant());string output=HistoryStore.Owned(dir,"play-v08-"+tokenName+".mp3");
    bool success=false;
    try{AudioReader.Copy(c.Path,raw,0,info.CompleteBytes,token);
     if(info.Format=="AAC")await Converter.Convert(toolsRoot,raw,output,info.Format,token);else output=raw;
     token.ThrowIfCancellationRequested();success=true;return new PlaybackFile{Path=output,Temporary=true,Candidate=c,Bytes=info.CompleteBytes,Seconds=info.Seconds};
    }finally{if(raw!=output||!success)try{System.IO.File.Delete(raw);}catch(IOException){}if(!success&&raw!=output)try{System.IO.File.Delete(output);}catch(IOException){}}
   },token);
  }
  public async Task Open(PlaybackFile file,double position=0){
   media.Close();Ready=false;Playing=AtEnd=false;File=file;Duration=file.Seconds;if(file.Temporary)snapshots.Add(file.Path);
   opened=new TaskCompletionSource<bool>();var completion=opened;media.Open(new Uri(file.Path));
   var timeout=Task.Delay(10000);if(await Task.WhenAny(completion.Task,timeout)!=completion.Task)throw new IOException("Nie udalo sie otworzyc audio w 10 sekund.");
   await completion.Task;Position=position;media.Play();Playing=WantPlay=true;Raise();
   foreach(var old in snapshots.ToArray()){if(old==file.Path)continue;try{System.IO.File.Delete(old);snapshots.Remove(old);}catch(IOException){}}
  }
  public CancellationToken NewRequest(bool closePlayback=true){request.Cancel();request.Dispose();request=new CancellationTokenSource();if(opened!=null&&!opened.Task.IsCompleted)opened.TrySetCanceled();if(closePlayback){media.Close();Ready=false;Playing=WantPlay=AtEnd=false;Raise();}return request.Token;}
  public void CancelRequest(){request.Cancel();}
  public void Dispose(){request.Cancel();media.Close();request.Dispose();foreach(var p in snapshots)try{System.IO.File.Delete(p);}catch(IOException){}}
  public void Muted(){media.Volume=0;}
 }
}

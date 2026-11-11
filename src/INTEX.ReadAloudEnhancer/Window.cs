using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Intex {
 public enum ContinuousWatchMode { Off, Watch, WatchAndPlay }
 sealed class QueuedStream {public string Path;}
 public sealed class ThemeButton : Button {
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Enabled)return;
   var b=BackColor;var f=ForeColor;var muted=Color.FromArgb((f.R*65+b.R*35)/100,(f.G*65+b.G*35)/100,(f.B*65+b.B*35)/100);
   var r=Rectangle.Inflate(ClientRectangle,-3,-3);using(var brush=new SolidBrush(b))e.Graphics.FillRectangle(brush,r);
   TextRenderer.DrawText(e.Graphics,Text,Font,r,muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
  }
 }
 public sealed class MainWindow : Form {
  [DllImport("user32.dll")] static extern bool ReleaseCapture();
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
  public readonly string Root;public bool TitleBar {get;private set;}public readonly Engine Engine;public readonly Player Player;
  public Settings Preferences;public readonly ThemeListView Streams=new ThemeListView(),History=new ThemeListView();public readonly ThemeSeekBar Seek=new ThemeSeekBar();
  public readonly ThemeButton Back=new ThemeButton(),Pause=new ThemeButton(),Forward=new ThemeButton(),StopButton=new ThemeButton(),NextButton=new ThemeButton();
  public readonly Label FooterCenter=new Label(),FooterVersion=new Label();
  public readonly InstrumentLamp Indicator=new InstrumentLamp();
  readonly Label status=new Label(),buffer=new Label(),time=new Label(),detail=new Label(),autoStatus=new Label();
  readonly AutoReadAloud autoReadAloud=new AutoReadAloud();int bindEpoch;
  readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();readonly NotifyIcon tray=new NotifyIcon();
  TrayObservation trayObservation;
  ToolStripMenuItem watchItem,watchPlayItem;
  TableLayoutPanel header;Label displayFusionLabel;ThemeButton windowMinimize,windowMaximize,windowClose;
  RegisteredWaitHandle activationWait;EventWaitHandle activationAck;
  readonly CancellationTokenSource closing=new CancellationTokenSource();
  readonly HashSet<string> warned=new HashSet<string>();
  readonly HashSet<string> watchKnown=new HashSet<string>(StringComparer.OrdinalIgnoreCase),queuedPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  readonly Queue<QueuedStream> streamQueue=new Queue<QueuedStream>();
  Snapshot snapshot;bool dragging,refreshing,preparing,closingNow,trayTransition;DateTime? armed;FormWindowState restore=FormWindowState.Normal;
  string historyId,activeStreamPath,observedStreamPath;Color surface,accent;long selectionEpoch;bool queueHold,queueAdvancing;ContinuousWatchMode watchMode;DateTime watchStartedUtc;
  public bool TestMode;public event Action<Snapshot> ReceivedSnapshot;public event Action ActivatedBySecondInstance;
  public MainWindow(string root,bool titlebar,IEnumerable<string> cacheDirectories=null,string dependencyRoot=null){
   Root=root;Preferences=HistoryStore.LoadSettings(root);TitleBar=titlebar||Preferences.titleBar;Engine=new Engine(root,cacheDirectories,dependencyRoot);Player=new Player(root,dependencyRoot);
   Text="INTEX ReadAloud Enhancer ("+Version+")";Name="IntexEnhancer";AccessibleName="INTEX ReadAloud Enhancer";
   AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);var area=Screen.PrimaryScreen.WorkingArea;ClientSize=new Size(Math.Max(850,Math.Min(1000,area.Width-40)),Math.Max(620,Math.Min(760,area.Height-(titlebar?70:40))));MinimumSize=new Size(850,640);StartPosition=FormStartPosition.CenterScreen;try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{Icon=SystemIcons.Application;}
   BuildInterface();ApplyTitleBar(TitleBar,false);ApplyTheme(this,Preferences.theme);
   autoReadAloud.DiagnosticPath=Path.Combine(root,"auto-readaloud-diagnostic.log");
   SetWatchMode((ContinuousWatchMode)Enum.Parse(typeof(ContinuousWatchMode),Preferences.watchMode),false);
   autoReadAloud.Changed+=message=>{if(IsDisposed||!IsHandleCreated||closingNow)return;try{BeginInvoke((Action)(()=>autoStatus.Text=message));}catch(InvalidOperationException){}};
   Engine.Updated+=s=>{if(IsDisposed||!IsHandleCreated)return;try{BeginInvoke((Action)(()=>ApplySnapshot(s)));}catch(InvalidOperationException){}};
   Player.Changed+=RefreshPlayback;
   timer.Interval=200;timer.Tick+=(s,e)=>{RefreshPlayback();MaybeExtendLive();MaybeStartNext();};
   Shown+=(s,e)=>{tray.Visible=true;if(!TestMode)Engine.Start();timer.Start();};
   Resize+=(s,e)=>{if(WindowState==FormWindowState.Minimized&&Preferences.minimizeTo=="Tray"&&!trayTransition)MinimizeWindow();else if(WindowState!=FormWindowState.Minimized&&!trayTransition)restore=WindowState;FitFooter();};
   FormClosing+=async (s,e)=>{if(closingNow)return;e.Cancel=true;closingNow=true;autoReadAloud.Stop();closing.Cancel();Player.CancelRequest();timer.Stop();status.Text="Zamykanie — zachowuje bufor...";await Engine.Stop();Close();};
   FormClosed+=(s,e)=>{if(activationWait!=null)activationWait.Unregister(null);autoReadAloud.Dispose();timer.Dispose();tray.Visible=false;trayObservation.Dispose();tray.Dispose();Player.Dispose();closing.Dispose();};
  }
  public string Version {get{return "v0.12";}}
  public bool ObservationArmed {get{return armed.HasValue;}}
  public bool TrayIsRed {get{return trayObservation.State==SignalState.Observing;}}
  public bool TrayVisible {get{return tray.Visible;}}
  public SignalState CurrentSignal {get{return trayObservation.State;}}
  public ContinuousWatchMode WatchMode {get{return watchMode;}}
  public int QueueCount {get{return queuedPaths.Count;}}
  public bool WatchMenuChecked {get{return watchItem!=null&&watchItem.Checked;}}
  public bool WatchPlayMenuChecked {get{return watchPlayItem!=null&&watchPlayItem.Checked;}}
  public void ArmObservation(DateTime startedUtc){if(closingNow)return;armed=startedUtc;SetSignal(SignalState.Observing);status.Text="Obserwuję jeden nowy odczyt. Uruchom Read aloud w ChatGPT.";}
  void SetSignal(SignalState state){trayObservation.SetState(state);Indicator.State=state;}
  void ClearObservation(SignalState next){armed=null;SetSignal(next);}
  public void TestTrayClick(MouseButtons button,int clicks){trayObservation.HandleClick(button,clicks);}
  public void TestTrayDoubleClick(){trayObservation.HandleDoubleClick();}
  public void TestSetWatchMode(ContinuousWatchMode mode){SetWatchMode(mode);}
  public Icon TrayIconImage(){return (Icon)tray.Icon.Clone();}
  FlowLayoutPanel Flow(){return new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Margin=Padding.Empty,Padding=Padding.Empty};}
  ThemeButton Button(string text,Action action,int width=165){var b=new ThemeButton{Text=text,Width=width,Height=34,AutoEllipsis=true,Margin=new Padding(0,3,10,3),Name=text,AccessibleName=text};b.Click+=(s,e)=>action();return b;}
  Label Label(string text,int height){return new Label{Text=text,Dock=DockStyle.Fill,Height=height,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft};}
  void BuildInterface(){
   var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=12};
   Controls.Add(layout);foreach(var h in new[]{42,0,42,48,26,26,44,42,30,42,30,0})layout.RowStyles.Add(h==0?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.Absolute,h));
   layout.RowStyles[11]=new RowStyle(SizeType.Absolute,0);
   layout.RowStyles[2]=new RowStyle(SizeType.AutoSize);
   layout.RowStyles[8]=new RowStyle(SizeType.Absolute,52);
   header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=6,Margin=Padding.Empty};
   header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));foreach(var n in new[]{0,1,2})header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,44));
   var heading=Label("INTEX ReadAloud Enhancer",38);heading.Name="WindowHeading";heading.MouseDown+=(s,e)=>{if(!TitleBar&&e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);}};heading.DoubleClick+=(s,e)=>{if(!TitleBar)MaximizeWindow();};header.Controls.Add(heading,0,0);
   displayFusionLabel=Label("DF",38);header.Controls.Add(displayFusionLabel,1,0);var monitor=Button("Ekran >",MoveMonitor,95);header.Controls.Add(monitor,2,0);
   windowMinimize=Button("—",MinimizeWindow,40);windowMaximize=Button("□",MaximizeWindow,40);windowClose=Button("×",Close,40);header.Controls.Add(windowMinimize,3,0);header.Controls.Add(windowMaximize,4,0);header.Controls.Add(windowClose,5,0);
   foreach(Control control in header.Controls)if(header.GetColumn(control)>=3){control.Font=new Font("Segoe UI",10);control.Name=header.GetColumn(control)==3?"WindowMinimize":header.GetColumn(control)==4?"WindowMaximize":"WindowClose";control.AccessibleName=header.GetColumn(control)==3?"Minimalizuj":header.GetColumn(control)==4?"Maksymalizuj lub przywroc":"Zamknij";}
   layout.Controls.Add(header,0,0);
   var tabs=new ThemeTabs{Dock=DockStyle.Fill,Name="AudioTabs"};var streamsTab=new TabPage("Streamy");var historyTab=new TabPage("Historia");tabs.TabPages.Add(streamsTab);tabs.TabPages.Add(historyTab);layout.Controls.Add(tabs,0,1);
   ConfigureList(Streams,new[]{"Data pliku","Dostępne audio","Rozmiar MB","Plik cache"},new[]{175,130,110,380});ConfigureList(History,new[]{"Zachowaj","Data zapisu","Czas","Stan","Nagranie MP3"},new[]{90,175,90,240,460});
   Streams.Name="Streams";History.Name="History";History.CheckBoxes=true;streamsTab.Controls.Add(Streams);historyTab.Controls.Add(History);
   Streams.MouseClick+=async(s,e)=>{if(e.Button!=MouseButtons.Left)return;var row=Streams.GetItemAt(e.X,e.Y);if(row!=null)await PlayCandidate((Candidate)row.Tag,false,false);};
   Streams.KeyDown+=async(s,e)=>{if(e.KeyCode==Keys.Enter&&Streams.SelectedItems.Count==1){e.SuppressKeyPress=true;await PlayCandidate((Candidate)Streams.SelectedItems[0].Tag,false,false);}};
   History.MouseClick+=async(s,e)=>{if(e.Button!=MouseButtons.Left)return;var hit=History.HitTest(e.Location);if(hit.Item!=null&&(hit.Location&ListViewHitTestLocations.StateImage)==0)await PlayHistory((Entry)hit.Item.Tag);};
   History.KeyDown+=async(s,e)=>{if(e.KeyCode==Keys.Enter&&History.SelectedItems.Count==1){e.SuppressKeyPress=true;await PlayHistory((Entry)History.SelectedItems[0].Tag);}};
   History.ItemChecked+=(s,e)=>{if(refreshing||e.Item.Tag==null)return;var entry=(Entry)e.Item.Tag;bool pinned=e.Item.Checked;Engine.Command(()=>Engine.Store.Pin(entry.Id,pinned));};
   AddMenus();var action=Flow();action.Controls.Add(Button("Odśwież listę",()=>Engine.Command(()=>{}),135));action.Controls.Add(Button("Obserwuj nowy odczyt",()=>ArmObservation(DateTime.UtcNow),205));action.Controls.Add(Button("Buforuj; graj po 3 s",async()=>{var c=SelectedCandidate();if(c!=null)await PlayCandidate(c,true,false);},215));action.Controls.Add(Button("Live: dostępny fragment",async()=>{var c=SelectedCandidate();if(c!=null)await PlayCandidate(c,false,true);},240));layout.Controls.Add(action,0,2);
   status.Text="Czekam. Zatrzymaj natywny głos ChatGPT osobno, aby uniknąć dwóch głosów.";status.Dock=DockStyle.Fill;status.AutoEllipsis=true;
   var statusPanel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty};statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,205));Indicator.Dock=DockStyle.Fill;Indicator.Margin=new Padding(4,3,0,3);statusPanel.Controls.Add(status,0,0);statusPanel.Controls.Add(Indicator,1,0);layout.Controls.Add(statusPanel,0,3);
   buffer.Text="Bufor: brak";buffer.Dock=DockStyle.Fill;buffer.AutoEllipsis=true;layout.Controls.Add(buffer,0,4);time.Text="0:00 / 0:00";time.Dock=DockStyle.Fill;layout.Controls.Add(time,0,5);
   Seek.Name="Seek";Seek.AccessibleName="Pozycja odtwarzania";Seek.Dock=DockStyle.Fill;Seek.Height=42;Seek.Maximum=1;Seek.Enabled=false;Seek.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left)dragging=true;};Seek.MouseUp+=(s,e)=>{if(e.Button!=MouseButtons.Left)return;dragging=false;if(Player.Ready)Player.Position=Seek.Value;};Seek.KeyUp+=(s,e)=>{if(Player.Ready)Player.Position=Seek.Value;};Seek.MouseCaptureChanged+=(s,e)=>dragging=false;layout.Controls.Add(Seek,0,6);
   var controls=Flow();Back.Text="−10 sekund";Pause.Text="Pauza";Forward.Text="+10 sekund";StopButton.Text="Stop lokalnie";NextButton.Text="Następny strumień";
   int[] controlWidths={140,140,140,160,190};int controlIndex=0;foreach(var b in new[]{Back,Pause,Forward,StopButton,NextButton}){b.Width=controlWidths[controlIndex++];b.Height=34;b.Margin=new Padding(0,2,10,2);b.Enabled=false;b.AccessibleName=b.Text;controls.Controls.Add(b);}
   Back.Name="Back";Pause.Name="Pause";Forward.Name="Forward";StopButton.Name="Stop";NextButton.Name="NextStream";
   Back.Click+=(s,e)=>Player.Position-=10;Forward.Click+=(s,e)=>Player.Position+=10;Pause.Click+=(s,e)=>TogglePlayback();StopButton.Click+=(s,e)=>StopLocal();NextButton.Click+=async(s,e)=>await NextStream();layout.Controls.Add(controls,0,7);
   detail.Text="Kompletne ramki nie dowodzą pobrania całej wiadomości. Historia zachowuje dostępne audio.";detail.Dock=DockStyle.Fill;detail.AutoEllipsis=true;
   var diagnostics=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};diagnostics.RowStyles.Add(new RowStyle(SizeType.Percent,50));diagnostics.RowStyles.Add(new RowStyle(SizeType.Percent,50));diagnostics.Controls.Add(detail,0,0);
   autoStatus.Text=Preferences.autoReadAloud?"Auto Read aloud: wskaż okno ChatGPT w Opcjach.":"Auto Read aloud: wyłączone (Opcje).";autoStatus.Dock=DockStyle.Fill;autoStatus.AutoEllipsis=true;autoStatus.Name="AutoReadAloudStatus";diagnostics.Controls.Add(autoStatus,0,1);layout.Controls.Add(diagnostics,0,8);
   var bottom=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty};bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));
   var left=Flow();left.Controls.Add(Button("Wyczyść streamy z cache...",async()=>await ClearCache(false),245));left.Controls.Add(Button("Otwórz kopie odzyskiwania",()=>OpenFolder(Path.Combine(Root,"cache-recovery")),260));bottom.Controls.Add(left,0,0);bottom.Controls.Add(Button("Opcje",ShowOptions,100),1,0);layout.Controls.Add(bottom,0,9);
   var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,Margin=Padding.Empty};foreach(var width in new[]{33f,34f,33f})footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,width));
   FooterCenter.Text="LAMoN 2026 AS";FooterCenter.TextAlign=ContentAlignment.MiddleCenter;FooterCenter.Dock=DockStyle.Fill;FooterVersion.Text="INTEX ReadAloud Enhancer "+Version;FooterVersion.TextAlign=ContentAlignment.MiddleRight;FooterVersion.Dock=DockStyle.Fill;FooterCenter.UseMnemonic=FooterVersion.UseMnemonic=false;FooterVersion.AutoEllipsis=true;
   footer.Controls.Add(FooterCenter,1,0);footer.Controls.Add(FooterVersion,2,0);footer.SizeChanged+=(s,e)=>FitFooter();layout.Controls.Add(footer,0,10);
   tray.Icon=SystemIcons.Application;tray.Text="INTEX ReadAloud Enhancer";var menu=new ContextMenuStrip();
   watchItem=new ToolStripMenuItem("Watch");watchPlayItem=new ToolStripMenuItem("Watch and Play");watchItem.Click+=(s,e)=>ToggleWatchMode(ContinuousWatchMode.Watch);watchPlayItem.Click+=(s,e)=>ToggleWatchMode(ContinuousWatchMode.WatchAndPlay);
   menu.Items.Add(watchItem);menu.Items.Add(watchPlayItem);menu.Items.Add(new ToolStripSeparator());menu.Items.Add("Pokaż INTEX",null,(s,e)=>RestoreWindow());menu.Items.Add("Opcje",null,(s,e)=>{RestoreWindow();ShowOptions();});menu.Items.Add(new ToolStripSeparator());menu.Items.Add("Zakończ",null,(s,e)=>Close());tray.ContextMenuStrip=menu;
   trayObservation=new TrayObservation(tray,ArmObservation,RestoreWindow,()=>tray.Visible);Indicator.State=SignalState.Idle;
  }
  void ConfigureList(ListView list,string[] columns,int[] widths){list.Dock=DockStyle.Fill;list.View=View.Details;list.FullRowSelect=true;list.MultiSelect=false;list.HideSelection=false;list.OwnerDraw=true;
   for(int i=0;i<columns.Length;i++)list.Columns.Add(columns[i],widths[i]);
   list.DrawColumnHeader+=(s,e)=>{using(var b=new SolidBrush(surface))e.Graphics.FillRectangle(b,e.Bounds);TextRenderer.DrawText(e.Graphics,e.Header.Text,list.Font,e.Bounds,list.ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};
   list.DrawItem+=(s,e)=>{if(list.View!=View.Details)e.DrawDefault=true;};list.DrawSubItem+=(s,e)=>{bool selected=e.Item.Selected;Color bg=selected?accent:surface,fg=selected?BackColor:list.ForeColor;using(var b=new SolidBrush(bg))e.Graphics.FillRectangle(b,e.Bounds);if(list.CheckBoxes&&e.ColumnIndex==0){int size=Math.Min(14,e.Bounds.Height-4);ThemeDrawing.Check(e.Graphics,new Rectangle(e.Bounds.Left+3,e.Bounds.Top+(e.Bounds.Height-size)/2,size,size),fg,bg,e.Item.Checked);return;}TextRenderer.DrawText(e.Graphics,e.SubItem.Text,list.Font,e.Bounds,fg,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};
   list.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Right){var row=list.GetItemAt(e.X,e.Y);if(row!=null){list.SelectedItems.Clear();row.Selected=true;}}};
  }
  void AddMenus(){
   var streams=new ContextMenuStrip();streams.Items.Add("Zapisz stream jako MP3...",null,async(s,e)=>await SaveStream());streams.Items.Add("Usuń ten stream z cache...",null,async(s,e)=>await ClearCache(true));Streams.ContextMenuStrip=streams;
   var history=new ContextMenuStrip();history.Items.Add("Zachowaj na stałe / usuń ochronę",null,(s,e)=>{if(History.SelectedItems.Count==1)History.SelectedItems[0].Checked=!History.SelectedItems[0].Checked;});history.Items.Add("Otwórz folder historii",null,(s,e)=>OpenFolder(Path.Combine(Root,"history")));History.ContextMenuStrip=history;
  }
  Candidate SelectedCandidate(){return Streams.SelectedItems.Count==1?(Candidate)Streams.SelectedItems[0].Tag:null;}
  static string FormatTime(double seconds){var n=(int)Math.Max(0,seconds);return (n/60)+":"+(n%60).ToString("00");}
  void ToggleWatchMode(ContinuousWatchMode mode){SetWatchMode(watchMode==mode?ContinuousWatchMode.Off:mode);}
  void SetWatchMode(ContinuousWatchMode mode,bool save=true){
   watchMode=mode;watchStartedUtc=DateTime.UtcNow;watchKnown.Clear();ClearQueue();observedStreamPath=null;
   if(watchItem!=null)watchItem.Checked=mode==ContinuousWatchMode.Watch;if(watchPlayItem!=null)watchPlayItem.Checked=mode==ContinuousWatchMode.WatchAndPlay;
   if(mode==ContinuousWatchMode.Off){status.Text="Stała obserwacja wyłączona.";if(!armed.HasValue&&String.IsNullOrEmpty(activeStreamPath))SetSignal(SignalState.Idle);}
   else {queueHold=false;SetSignal(SignalState.Observing);status.Text=mode==ContinuousWatchMode.Watch?"Watch: stale obserwuję i archiwizuję nowe streamy bez odtwarzania.":"Watch and Play: stale obserwuję; nowe streamy odtworzę kolejno.";}
   Preferences.watchMode=mode.ToString();if(save){var prefs=Preferences.Clone();Engine.Command(()=>Engine.Store.SaveSettings(prefs));}
  }
  void ClearQueue(){streamQueue.Clear();queuedPaths.Clear();}
  void EnqueueStream(Candidate candidate){
   if(candidate==null||candidate.Path==activeStreamPath||!queuedPaths.Add(candidate.Path))return;
   streamQueue.Enqueue(new QueuedStream{Path=candidate.Path});observedStreamPath=candidate.Path;SetSignal(SignalState.Active);
   status.Text="Dodano do kolejki: "+candidate.Name+". Oczekuje: "+QueueCount+".";
  }
  void RemoveQueued(string path){if(!String.IsNullOrEmpty(path))queuedPaths.Remove(path);}
  Candidate TakeNext(){
   while(streamQueue.Count>0){var item=streamQueue.Dequeue();if(!queuedPaths.Remove(item.Path))continue;if(snapshot==null)continue;var candidate=snapshot.Candidates.FirstOrDefault(x=>x.Path.Equals(item.Path,StringComparison.OrdinalIgnoreCase));if(candidate!=null)return candidate;}
   return null;
  }
  bool CandidateStable(string path){if(String.IsNullOrEmpty(path)||snapshot==null)return true;var c=snapshot.Candidates.FirstOrDefault(x=>x.Path.Equals(path,StringComparison.OrdinalIgnoreCase));return c==null||c.Length==c.CompleteBytes&&(DateTime.UtcNow-c.ModifiedUtc).TotalSeconds>=10;}
  bool PlaybackBlocksQueue(){if(preparing||queueAdvancing||Player.Playing)return true;if(Player.Ready&&!Player.AtEnd)return true;if(Player.AtEnd&&Player.Live&&!CandidateStable(activeStreamPath))return true;return false;}
  void DetectContinuousStreams(Snapshot s){
   if(watchMode==ContinuousWatchMode.Off)return;
   foreach(var c in s.Candidates.Where(x=>x.ModifiedUtc>=watchStartedUtc).OrderBy(x=>x.ModifiedUtc))if(watchKnown.Add(c.Path)){
    observedStreamPath=c.Path;SetSignal(SignalState.Active);
    if(watchMode==ContinuousWatchMode.WatchAndPlay)EnqueueStream(c);else status.Text="Watch wykrył i archiwizuje: "+c.Name;
   }
  }
  void UpdateSignal(){
   if(armed.HasValue){SetSignal(SignalState.Observing);return;}
   string path=!String.IsNullOrEmpty(observedStreamPath)?observedStreamPath:activeStreamPath;
   if(!String.IsNullOrEmpty(path)){SetSignal(CandidateStable(path)?SignalState.Stable:SignalState.Active);return;}
   SetSignal(watchMode==ContinuousWatchMode.Off?SignalState.Idle:SignalState.Observing);
  }
  void ApplySnapshot(Snapshot s){
   snapshot=s;refreshing=true;try{
    Sync(Streams,s.Candidates.Select(c=>Tuple.Create(c.Path,(object)c,new[]{c.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),FormatTime(c.Seconds),(c.Length/1048576.0).ToString("F2"),c.Name+" ["+c.Format+"]"},false)).ToList());
    Sync(History,s.Entries.Select(e=>Tuple.Create(e.Id,(object)e,new[]{"",String.IsNullOrEmpty(e.SavedAtUtc)?"Oczekuje na MP3":HistoryStore.Date(e.SavedAtUtc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),FormatTime(e.Seconds),!String.IsNullOrEmpty(e.Error)?"Błąd; audio zachowane":String.IsNullOrEmpty(e.Mp3File)?"Kolejka MP3":e.Partial||e.ExportedBytes<e.CopiedBytes?"Fragment / rosnący":"Ramki kompletne; pełna treść nieznana",String.IsNullOrEmpty(e.Mp3File)?e.SourceName:e.Mp3File},e.Pinned)).ToList());
   }finally{refreshing=false;}
   detail.Text="Streamy: "+s.Candidates.Count+" | Historia: "+s.Entries.Count+" | Kolejka MP3: "+s.PendingExports+" | Oczekuje na odtworzenie: "+QueueCount+" | Skan: "+s.ScanMs.ToString("F0")+" ms.";
   if(!String.IsNullOrEmpty(s.Warning)&&warned.Add(s.Warning))status.Text=s.Warning;
   DetectContinuousStreams(s);
   if(armed.HasValue){var c=s.Candidates.FirstOrDefault(x=>x.ModifiedUtc>armed.Value);if(c!=null){armed=null;EnqueueStream(c);}}
   UpdateSignal();MaybeStartNext();
   var received=ReceivedSnapshot;if(received!=null)received(s);
  }
  static void Sync(ListView list,List<Tuple<string,object,string[],bool>> data){
   var existing=list.Items.Cast<ListViewItem>().ToDictionary(x=>x.Name);var keep=new HashSet<string>();list.BeginUpdate();try{
    int index=0;foreach(var d in data){ListViewItem row;if(!existing.TryGetValue(d.Item1,out row)){row=new ListViewItem(d.Item3){Name=d.Item1};list.Items.Insert(Math.Min(index,list.Items.Count),row);}else{for(int i=0;i<d.Item3.Length;i++)if(row.SubItems[i].Text!=d.Item3[i])row.SubItems[i].Text=d.Item3[i];}
     row.Tag=d.Item2;if(list.CheckBoxes&&row.Checked!=d.Item4)row.Checked=d.Item4;keep.Add(d.Item1);index++;
    }
    foreach(var row in list.Items.Cast<ListViewItem>().ToArray())if(!keep.Contains(row.Name))list.Items.Remove(row);
   }finally{list.EndUpdate();}
  }
  public async Task PlayCandidate(Candidate c,bool waitStable,bool live){
   if(closingNow)return;RemoveQueued(c.Path);queueHold=false;if(observedStreamPath==c.Path)observedStreamPath=null;long epoch=++selectionEpoch;preparing=true;autoReadAloud.Blocked=true;var token=Player.NewRequest();Player.Live=live;historyId=null;Engine.ActiveHistoryId=null;activeStreamPath=c.Path;if(!armed.HasValue)SetSignal(SignalState.Active);
   status.Text=waitStable?"Buforuję. Czekam na 3 sekundy bez przyrostu...":"Przygotowuję audio w tle — okno pozostaje dostępne.";
   PlaybackFile file=null;
   try{file=await Player.Prepare(c,waitStable,token);if(token.IsCancellationRequested||epoch!=selectionEpoch){if(file.Temporary)File.Delete(file.Path);return;}await Player.Open(file);status.Text="Odtwarzam: "+c.Name;buffer.Text="Bufor: "+FormatTime(file.Seconds)+" | "+Path.GetFileName(file.Path);}
   catch(OperationCanceledException){}catch(Exception ex){if(activeStreamPath==c.Path)activeStreamPath=null;status.Text="Błąd odtwarzania: "+ex.Message;}finally{if(epoch==selectionEpoch)preparing=false;}
  }
  public async Task PlayHistory(Entry e){
   if(String.IsNullOrEmpty(e.Mp3File)){status.Text="Audio zachowane. MP3 jest jeszcze przygotowywane.";return;}
   queueHold=false;long epoch=++selectionEpoch;preparing=true;autoReadAloud.Blocked=true;Player.NewRequest();Player.Live=false;activeStreamPath=null;armed=null;UpdateSignal();historyId=e.Id;Engine.ActiveHistoryId=e.Id;
   try{var path=HistoryStore.Owned(Path.Combine(Root,"history"),e.Mp3File);if(!File.Exists(path))throw new IOException("Brak pliku historii.");await Player.Open(new PlaybackFile{Path=path,Seconds=e.Seconds});status.Text="Historia: "+e.Mp3File;buffer.Text="Odtwarzam zachowane MP3.";}catch(Exception ex){status.Text=ex.Message;}finally{if(epoch==selectionEpoch){preparing=false;RefreshPlayback();}}
  }
  async void MaybeExtendLive(){
   if(preparing||!Player.Live||!Player.WantPlay||Player.File==null||Player.File.Candidate==null||snapshot==null||!Player.Ready||(!Player.AtEnd&&Player.Position<Player.Duration-.7))return;
   var c=snapshot.Candidates.FirstOrDefault(x=>x.Path==Player.File.Candidate.Path);if(c==null||c.CompleteBytes<=Player.File.Bytes||c.Seconds<=Player.Duration+.5)return;
   long epoch=selectionEpoch;preparing=true;double position=Player.AtEnd?Player.Duration:Player.Position;var token=Player.NewRequest(false);PlaybackFile file=null;
   try{file=await Player.Prepare(c,false,token);if(epoch!=selectionEpoch||token.IsCancellationRequested||!Player.WantPlay){if(file.Temporary)File.Delete(file.Path);return;}await Player.Open(file,position);}
   catch(OperationCanceledException){}catch(Exception ex){status.Text=ex.Message;}finally{if(epoch==selectionEpoch)preparing=false;}
  }
  async void MaybeStartNext(){
   if(closingNow||queueHold||queueAdvancing||QueueCount==0||PlaybackBlocksQueue())return;
   queueAdvancing=true;try{var next=TakeNext();if(next==null)return;await PlayCandidate(next,false,true);}finally{queueAdvancing=false;RefreshPlayback();}
  }
  void TogglePlayback(){if(!Player.Ready)return;if(!Player.Playing)queueHold=false;Player.Toggle();}
  public void StopLocal(){
   queueHold=true;if(preparing){++selectionEpoch;Player.NewRequest();preparing=false;}else Player.Stop();
   status.Text=QueueCount>0?"Odtwarzanie zatrzymane. W kolejce czeka: "+QueueCount+". Użyj „Następny strumień”.":"Odtwarzanie zatrzymane.";RefreshPlayback();
  }
  public async Task NextStream(){
   queueHold=false;var next=TakeNext();++selectionEpoch;preparing=true;autoReadAloud.Blocked=true;Player.NewRequest();preparing=false;activeStreamPath=null;historyId=null;Engine.ActiveHistoryId=null;
   if(next==null){queueHold=true;status.Text="Zatrzymano odtwarzanie. W kolejce nie ma następnego streamu.";UpdateSignal();RefreshPlayback();return;}
   await PlayCandidate(next,false,true);
  }
  public void RefreshPlayback(){if(IsDisposed)return;autoReadAloud.Blocked=closingNow||queueHold||PlaybackBlocksQueue()||QueueCount>0;bool ready=Player.Ready;Seek.Enabled=Back.Enabled=Pause.Enabled=Forward.Enabled=StopButton.Enabled=ready;NextButton.Enabled=ready||QueueCount>0;Pause.Text=Player.Playing?"Pauza":"Wznów";if(ready){Seek.Maximum=Math.Max(1,(int)Math.Ceiling(Player.Duration));if(!dragging)Seek.Value=Math.Max(0,Math.Min(Seek.Maximum,(int)Player.Position));time.Text=FormatTime(Player.Position)+" / "+FormatTime(Player.Duration);}}
  public void ApplyTheme(Control root,string name){
   Color bg,fg;if(name=="Light"){bg=ColorTranslator.FromHtml("#F4F4F4");surface=Color.White;fg=Color.FromArgb(32,32,32);accent=ColorTranslator.FromHtml("#1769AA");}else if(name=="Dark"){bg=ColorTranslator.FromHtml("#17191C");surface=ColorTranslator.FromHtml("#24282E");fg=ColorTranslator.FromHtml("#E9EDF1");accent=ColorTranslator.FromHtml("#65B7FF");}else{bg=ColorTranslator.FromHtml("#001A0E");surface=ColorTranslator.FromHtml("#0A2C12");fg=ColorTranslator.FromHtml("#78FF63");accent=ColorTranslator.FromHtml("#3FFF14");}
   string font="Segoe UI";if(name=="INTEX"){var names=FontFamily.Families.Select(x=>x.Name).ToList();font=new[]{"VCR OSD Mono","VCR A","OCR A Extended","Consolas"}.FirstOrDefault(names.Contains)??"Consolas";}
   root.Font=new Font(font,9);ThemeControls(root,bg,fg);foreach(var menu in new[]{Streams.ContextMenuStrip,History.ContextMenuStrip,tray.ContextMenuStrip})if(menu!=null){menu.BackColor=surface;menu.ForeColor=fg;foreach(ToolStripItem i in menu.Items){i.BackColor=surface;i.ForeColor=fg;}}if(root==this){Streams.Columns[0].Width=TextRenderer.MeasureText("2026-10-09 00:15:21",Streams.Font).Width+16;Streams.Columns[1].Width=TextRenderer.MeasureText("Dostępne audio",Streams.Font).Width+16;History.Columns[1].Width=Streams.Columns[0].Width;}FitFooter();root.Invalidate(true);
  }
  void ThemeControls(Control c,Color bg,Color fg){c.BackColor=(c is ListView||c is Button||c is ComboBox)?surface:bg;c.ForeColor=fg;var button=c as Button;if(button!=null){button.UseVisualStyleBackColor=false;button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=accent;}var check=c as CheckBox;if(check!=null){check.UseVisualStyleBackColor=false;check.FlatStyle=FlatStyle.Flat;check.FlatAppearance.BorderColor=accent;check.FlatAppearance.CheckedBackColor=surface;}var themed=c as IThemeAccent;if(themed!=null)themed.Accent=accent;foreach(Control child in c.Controls)ThemeControls(child,bg,fg);}
  void FitFooter(){if(FooterVersion==null||FooterVersion.Width<10)return;foreach(var label in new[]{FooterCenter,FooterVersion}){float size=Font.SizeInPoints;Font f=null;while(size>=6){f=new Font(Font.FontFamily,size);var measured=TextRenderer.MeasureText(label.Text,f);if(measured.Width<=label.Width-8&&measured.Height<=label.Height)break;f.Dispose();f=null;size-=.25f;}if(f==null)f=new Font(Font.FontFamily,6);if(label.Font.Equals(f)){f.Dispose();continue;}var old=label.Tag as Font;label.Font=f;label.Tag=f;if(old!=null)old.Dispose();}}
  public void ApplyTitleBar(bool system,bool save){
   if(WindowState==FormWindowState.Maximized)WindowState=FormWindowState.Normal;TitleBar=system;Preferences.titleBar=system;
   SuspendLayout();try{FormBorderStyle=system?FormBorderStyle.Sizable:FormBorderStyle.None;header.ColumnStyles[1].Width=system?0:110;foreach(var column in new[]{3,4,5})header.ColumnStyles[column].Width=system?0:44;displayFusionLabel.Visible=!system;windowMinimize.Visible=!system;windowMaximize.Visible=!system;windowClose.Visible=!system;Text="INTEX ReadAloud Enhancer ("+Version+")";}finally{ResumeLayout(true);}
   if(save){var prefs=Preferences.Clone();Engine.Command(()=>Engine.Store.SaveSettings(prefs));}FitFooter();
  }
  public void ListenForActivation(EventWaitHandle request,EventWaitHandle ack){
   activationAck=ack;activationWait=ThreadPool.RegisterWaitForSingleObject(request,(state,timedOut)=>{if(timedOut||IsDisposed)return;try{BeginInvoke((Action)(()=>{RestoreWindow();if(activationAck!=null)activationAck.Set();var activated=ActivatedBySecondInstance;if(activated!=null)activated();}));}catch(InvalidOperationException){}},null,Timeout.Infinite,false);
  }
  public void MinimizeWindow(){if(Preferences.minimizeTo=="Tray"){trayTransition=true;try{tray.Visible=true;Hide();ShowInTaskbar=false;}finally{trayTransition=false;}}else WindowState=FormWindowState.Minimized;}
  public void RestoreWindow(){trayTransition=true;try{tray.Visible=true;ShowInTaskbar=true;Show();WindowState=restore==FormWindowState.Minimized?FormWindowState.Normal:restore;Activate();SetForegroundWindow(Handle);}finally{trayTransition=false;}}
  void MaximizeWindow(){MaximizedBounds=Screen.FromHandle(Handle).WorkingArea;WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
  void MoveMonitor(){var screens=Screen.AllScreens;if(screens.Length<2){status.Text="Windows udostępnia jeden monitor.";return;}var current=Screen.FromHandle(Handle);int i=Array.FindIndex(screens,s=>s.DeviceName==current.DeviceName);var area=screens[(i+1)%screens.Length].WorkingArea;bool max=WindowState==FormWindowState.Maximized;WindowState=FormWindowState.Normal;Location=new Point(area.Left+20,area.Top+20);if(max){MaximizedBounds=area;WindowState=FormWindowState.Maximized;}}
  protected override void WndProc(ref Message m){base.WndProc(ref m);if(!TitleBar&&m.Msg==0x84&&WindowState==FormWindowState.Normal){long value=m.LParam.ToInt64();Point p=PointToClient(new Point((short)(value&65535),(short)((value>>16)&65535)));int b=7;bool l=p.X<b,r=p.X>=ClientSize.Width-b,t=p.Y<b,d=p.Y>=ClientSize.Height-b;int hit=t&&l?13:t&&r?14:d&&l?16:d&&r?17:l?10:r?11:t?12:d?15:0;if(hit!=0)m.Result=new IntPtr(hit);}}
  void OpenFolder(string path){try{Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo("explorer.exe","\""+path+"\""){UseShellExecute=true});}catch(Exception ex){status.Text=ex.Message;}}
  void ShowOptions(){ShowOptions(null);}
  public void TestOptionsPreview(string path){ShowOptions(path);}
  void ShowOptions(string preview){
   bool bindAfterSave=false;
   using(var f=new Form{Text="INTEX — Opcje",StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,ClientSize=new Size(580,430),MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false,AutoScaleMode=AutoScaleMode.Dpi}){
    var table=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(15),ColumnCount=2,RowCount=9};table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55));f.Controls.Add(table);
    var theme=new ThemeComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};theme.Items.AddRange(new object[]{"Dark","Light","INTEX"});theme.SelectedItem=Preferences.theme;
    var min=new ThemeComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};min.Items.AddRange(new object[]{"Pasek zadań","Tray"});min.SelectedIndex=Preferences.minimizeTo=="Tray"?1:0;
    var title=new ThemeComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};title.Items.AddRange(new object[]{"Belka INTEX","Systemowa belka tytułu"});title.SelectedIndex=TitleBar?1:0;
    var days=new ThemeNumber{Minimum=1,Maximum=36500,Value=Math.Max(1,Preferences.retentionDays),Dock=DockStyle.Fill};var never=new ThemeCheckBox{Text="Nigdy nie usuwaj automatycznie",Checked=Preferences.retentionDays==0,AutoSize=true};days.Enabled=!never.Checked;never.CheckedChanged+=(s,e)=>days.Enabled=!never.Checked;
    table.Controls.Add(Label("Motyw:",28),0,0);table.Controls.Add(theme,1,0);table.Controls.Add(Label("Minimalizuj do:",28),0,1);table.Controls.Add(min,1,1);table.Controls.Add(Label("Belka tytułu:",28),0,2);table.Controls.Add(title,1,2);table.Controls.Add(Label("Historia — liczba dni:",28),0,3);table.Controls.Add(days,1,3);table.Controls.Add(never,0,4);table.SetColumnSpan(never,2);
    var auto=new ThemeCheckBox{Text="Auto Read aloud — automatyczny odczyt (testowe)",Checked=Preferences.autoReadAloud,AutoSize=true,Name="AutoReadAloudEnabled"};table.Controls.Add(auto,0,5);table.SetColumnSpan(auto,2);
    var setup=Label("Po starcie wskaż okno ChatGPT (przycisk poniżej).\nOdczyt obejmie nowe odpowiedzi.",44);setup.MinimumSize=new Size(0,44);table.Controls.Add(setup,0,6);table.SetColumnSpan(setup,2);
    var bind=Button("Wskaż okno ChatGPT — przełącz się w ciągu 5 s",()=>{auto.Checked=true;bindAfterSave=true;f.DialogResult=DialogResult.OK;},535);bind.Name="BindChatGptWindow";table.Controls.Add(bind,0,7);table.SetColumnSpan(bind,2);
    var save=Button("Zapisz",()=>{},110);save.DialogResult=DialogResult.OK;table.Controls.Add(save,1,8);f.AcceptButton=save;ApplyTheme(f,Preferences.theme);
    if(preview!=null){f.Show();f.PerformLayout();using(var bitmap=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(preview);}f.Close();return;}
    if(f.ShowDialog(this)==DialogResult.OK){bool wasEnabled=Preferences.autoReadAloud;Preferences.theme=(string)theme.SelectedItem;Preferences.minimizeTo=min.SelectedIndex==1?"Tray":"Taskbar";Preferences.retentionDays=never.Checked?0:(int)days.Value;Preferences.titleBar=title.SelectedIndex==1;Preferences.autoReadAloud=auto.Checked;ApplyTitleBar(Preferences.titleBar,false);var prefs=Preferences.Clone();Engine.Command(()=>Engine.Store.SaveSettings(prefs));ApplyTheme(this,prefs.theme);if(!prefs.autoReadAloud){++bindEpoch;autoReadAloud.Stop();autoStatus.Text="Auto Read aloud: wyłączone.";}else if(!wasEnabled)autoStatus.Text="Auto Read aloud: wskaż okno ChatGPT w Opcjach.";}
   }
   if(bindAfterSave)BindAutoReadAloud();
  }
  async void BindAutoReadAloud(){
   int epoch=++bindEpoch;autoReadAloud.Stop();
   try{for(int left=5;left>0;left--){if(epoch!=bindEpoch)return;autoStatus.Text="Auto Read aloud: przełącz się do właściwego okna ChatGPT — "+left+" s.";await Task.Delay(1000,closing.Token);}if(epoch!=bindEpoch||!Preferences.autoReadAloud||closingNow)return;RefreshPlayback();autoReadAloud.BindForeground();}catch(OperationCanceledException){}
  }
  async Task SaveStream(){var c=SelectedCandidate();if(c==null)return;using(var dialog=new SaveFileDialog{Title="Zapisz stream jako MP3",Filter="MP3 audio (*.mp3)|*.mp3",DefaultExt="mp3",AddExtension=true,OverwritePrompt=true,FileName="ReadAloud_"+DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")+".mp3",InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)}){
   if(dialog.ShowDialog(this)!=DialogResult.OK)return;string target=Path.GetFullPath(dialog.FileName);if(target.Equals(c.Path,StringComparison.OrdinalIgnoreCase)||!target.EndsWith(".mp3",StringComparison.OrdinalIgnoreCase)){status.Text="Wybierz plik MP3 poza cache.";return;}
   status.Text="Zapisuję MP3 w tle...";PlaybackFile prepared=null;string temp=target+".part-"+Guid.NewGuid().ToString("N");
   try{prepared=await Player.Prepare(c,true,closing.Token);await Task.Run(()=>{AudioReader.Copy(prepared.Path,temp,0,new FileInfo(prepared.Path).Length,closing.Token);closing.Token.ThrowIfCancellationRequested();if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);},closing.Token);status.Text="Zapisano: "+target;}catch(OperationCanceledException){}catch(Exception ex){status.Text=ex.Message;}finally{foreach(var p in new[]{temp,prepared==null?null:prepared.Path})if(p!=null)try{File.Delete(p);}catch(IOException){}}
  }}
  async Task ClearCache(bool selected){
   if(snapshot==null)return;var candidates=selected?(SelectedCandidate()==null?new List<Candidate>():new List<Candidate>{SelectedCandidate()}):snapshot.Candidates.ToList();if(candidates.Count==0)return;
   if(MessageBox.Show(this,"Usunąć "+candidates.Count+" stream(y) z cache?\nZatrzymaj Read aloud w ChatGPT. Kopie pozostaną w cache-recovery.\nPonowne pobranie nie jest gwarantowane — ChatGPT może pamiętać audio w RAM.","INTEX — usuwanie cache",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
   status.Text="Weryfikuję i tworzę kopie odzyskiwania w tle...";
   try{int n=await Task.Run(()=>{int count=0;var known=Scanner.CacheDirectories().ToList();foreach(var c in candidates){closing.Token.ThrowIfCancellationRequested();string path=Path.GetFullPath(c.Path);if(!known.Contains(Path.GetDirectoryName(path),StringComparer.OrdinalIgnoreCase)||!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path),"^f_[0-9a-fA-F]+$"))throw new IOException("Nieprawidłowy plik cache.");var file=new FileInfo(path);if(file.Length!=c.Length||file.LastWriteTimeUtc.Ticks!=c.Ticks||(DateTime.UtcNow-file.LastWriteTimeUtc).TotalSeconds<3)throw new IOException("Stream zmienił się lub nadal jest pobierany.");
    var info=new AudioReader().Parse(path);if(info.Invalid||info.CompleteBytes!=info.Bytes)throw new IOException("Stream ma niepełną ramkę; nie usunę go.");string dir=Path.Combine(Root,"cache-recovery");Directory.CreateDirectory(dir);string basename=DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N");string backup=HistoryStore.Owned(dir,basename+"."+info.Format.ToLowerInvariant());
    using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)){
     if(input.Length!=c.Length)throw new IOException("Stream zmienił się.");using(var output=new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);
     var check=new AudioReader().Parse(backup);if(check.Invalid||check.CompleteBytes!=info.CompleteBytes||check.Bytes!=info.Bytes)throw new IOException("Błąd kopii odzyskiwania.");Json.Save(HistoryStore.Owned(dir,basename+".json"),new{originalPath=path,backupFile=Path.GetFileName(backup),format=info.Format,durationSeconds=info.Seconds,backupAtUtc=DateTime.UtcNow.ToString("o")});File.Delete(path);count++;
    }
   }return count;},closing.Token);status.Text="Usunięto "+n+" streamów. Kopie zachowano.";}catch(OperationCanceledException){}catch(Exception ex){status.Text="Nie wszystkie streamy usunięto: "+ex.Message;}
  }
 }
}

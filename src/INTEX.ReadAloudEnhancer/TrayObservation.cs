using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Intex {
 public enum SignalState { Idle, Observing, Active, Stable }

 // Jeden klik uzbraja obserwację; dwuklik przywraca okno bez dodatkowego uzbrojenia.
 public sealed class TrayObservation : IDisposable {
  [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
  public static readonly Color Green=ColorTranslator.FromHtml("#001A06");
  public static readonly Color Red=Color.FromArgb(235,48,48);
  public static readonly Color Yellow=Color.FromArgb(255,210,28);
  readonly NotifyIcon tray;readonly Timer singleClick=new Timer();readonly Icon original,green,red,yellow;
  readonly Action<DateTime> arm;readonly Action restore;readonly Func<bool> available;
  DateTime pendingAt,suppressUntil;bool pending,disposed;
  public SignalState State {get;private set;}
  public bool Armed {get{return State==SignalState.Observing;}}
  public TrayObservation(NotifyIcon tray,Action<DateTime> arm,Action restore,Func<bool> available){
   this.tray=tray;this.arm=arm;this.restore=restore;this.available=available;original=tray.Icon;
   green=CreateIcon(Green);red=CreateIcon(Red);yellow=CreateIcon(Yellow);
   singleClick.Interval=Math.Max(1,SystemInformation.DoubleClickTime);
   singleClick.Tick+=(s,e)=>{singleClick.Stop();if(!pending||disposed)return;pending=false;if(available())arm(pendingAt);};
   tray.MouseClick+=OnMouseClick;tray.DoubleClick+=OnDoubleClick;SetState(SignalState.Idle);
  }
  void OnMouseClick(object sender,MouseEventArgs e){HandleClick(e.Button,e.Clicks);}
  void OnDoubleClick(object sender,EventArgs e){HandleDoubleClick();}
  public void HandleClick(MouseButtons button,int clicks){
   if(disposed||button!=MouseButtons.Left||clicks>1||!available()||DateTime.UtcNow<suppressUntil)return;
   pendingAt=DateTime.UtcNow;pending=true;singleClick.Stop();singleClick.Start();
  }
  public void HandleDoubleClick(){
   if(disposed)return;pending=false;singleClick.Stop();suppressUntil=DateTime.UtcNow.AddMilliseconds(SystemInformation.DoubleClickTime);restore();
  }
  public void SetState(SignalState value){
   if(disposed)return;if(State!=value&&value!=SignalState.Observing){pending=false;singleClick.Stop();}State=value;
   tray.Icon=value==SignalState.Observing?red:value==SignalState.Active?yellow:green;
   tray.Text=value==SignalState.Observing?"INTEX — obserwuję nowy Read aloud":value==SignalState.Active?"INTEX — stream wykryty / aktywny":value==SignalState.Stable?"INTEX — stream stabilny":"INTEX ReadAloud Enhancer — gotowy";
  }
  static Icon CreateIcon(Color color){
   using(var bitmap=new Bitmap(32,32))using(var graphics=Graphics.FromImage(bitmap)){
    graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;graphics.Clear(Color.Transparent);
    using(var shadow=new SolidBrush(Color.FromArgb(110,0,0,0)))graphics.FillEllipse(shadow,2,3,29,29);
    using(var brush=new SolidBrush(color))graphics.FillEllipse(brush,1,1,29,29);
    using(var ring=new Pen(Color.FromArgb(210,205,205,195),2))graphics.DrawEllipse(ring,1,1,29,29);
    using(var brush=new SolidBrush(Color.White)){graphics.FillRectangle(brush,7,12,5,8);graphics.FillPolygon(brush,new[]{new Point(11,12),new Point(18,7),new Point(18,25),new Point(11,20)});}
    using(var pen=new Pen(Color.White,2))graphics.DrawArc(pen,16,9,10,14,-60,120);
    IntPtr handle=bitmap.GetHicon();try{using(var temporary=Icon.FromHandle(handle))return (Icon)temporary.Clone();}finally{DestroyIcon(handle);}
   }
  }
  public void Dispose(){if(disposed)return;disposed=true;singleClick.Stop();singleClick.Dispose();tray.MouseClick-=OnMouseClick;tray.DoubleClick-=OnDoubleClick;tray.Icon=original;green.Dispose();red.Dispose();yellow.Dispose();}
 }

 // Wskaźnik stylizowany na pojedynczą lampkę kontrolną aparatury pomiarowej z lat 60.
 public sealed class InstrumentLamp : Control {
  SignalState state;
  public SignalState State {get{return state;}set{if(state==value)return;state=value;Invalidate();}}
  public string StateText {get{return state==SignalState.Observing?"OBSERWACJA":state==SignalState.Active?"STRUMIEŃ":state==SignalState.Stable?"STABILNY":"GOTOWY";}}
  public Color StateColor {get{return state==SignalState.Observing?TrayObservation.Red:state==SignalState.Active?TrayObservation.Yellow:TrayObservation.Green;}}
  public InstrumentLamp(){DoubleBuffered=true;MinimumSize=new Size(185,38);AccessibleName="Stan Read aloud";}
  protected override void OnPaint(PaintEventArgs e){
   base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
   var panel=new Rectangle(1,2,Width-3,Height-5);
   using(var body=new System.Drawing.Drawing2D.LinearGradientBrush(panel,Color.FromArgb(104,101,87),Color.FromArgb(42,41,35),90))g.FillRectangle(body,panel);
   using(var edge=new Pen(Color.FromArgb(180,190,182,150)))g.DrawRectangle(edge,panel);
   var outer=new Rectangle(8,(Height-28)/2,28,28);using(var bezel=new SolidBrush(Color.FromArgb(185,178,155)))g.FillEllipse(bezel,outer);
   using(var dark=new Pen(Color.FromArgb(30,30,26),2))g.DrawEllipse(dark,outer);
   var lens=Rectangle.Inflate(outer,-5,-5);using(var glow=new SolidBrush(StateColor))g.FillEllipse(glow,lens);
   using(var glass=new SolidBrush(Color.FromArgb(75,255,255,255)))g.FillEllipse(glass,new Rectangle(lens.X+3,lens.Y+2,7,5));
   var textRect=new Rectangle(44,2,Width-50,Height-5);using(var shadow=new SolidBrush(Color.FromArgb(20,20,17)))g.FillRectangle(shadow,textRect);
   TextRenderer.DrawText(g,StateText,Font,textRect,Color.FromArgb(225,218,183),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
  }
 }
}

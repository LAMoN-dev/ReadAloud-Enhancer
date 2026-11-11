using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Intex {
 public interface IThemeAccent {Color Accent {get;set;}}
 public sealed class ThemeTabs : TabControl,IThemeAccent {
  Color background=Color.FromArgb(0,26,14);
  public override Color BackColor {get{return background;}set{background=value;Invalidate();}}
  public Color Accent {get;set;}
  public ThemeTabs(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Padding=new Point(16,5);}
  protected override void OnPaint(PaintEventArgs e){
   e.Graphics.Clear(BackColor);
   for(int i=0;i<TabCount;i++){var r=GetTabRect(i);bool selected=i==SelectedIndex;
    using(var b=new SolidBrush(BackColor))e.Graphics.FillRectangle(b,r);
    using(var p=new Pen(Accent))e.Graphics.DrawRectangle(p,r.X,r.Y,r.Width-1,r.Height-1);
    if(selected)using(var p=new Pen(Accent,3))e.Graphics.DrawLine(p,r.Left+2,r.Bottom-2,r.Right-2,r.Bottom-2);
    TextRenderer.DrawText(e.Graphics,TabPages[i].Text,Font,r,ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
    if(selected&&Focused)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(r,-4,-4),ForeColor,BackColor);
   }
  }
  protected override void OnSelectedIndexChanged(EventArgs e){base.OnSelectedIndexChanged(e);Invalidate();}
 }
 public sealed class ThemeComboBox : ComboBox {
  public ThemeComboBox(){FlatStyle=FlatStyle.Flat;DrawMode=DrawMode.OwnerDrawFixed;}
  protected override void OnDrawItem(DrawItemEventArgs e){
   bool selected=(e.State&DrawItemState.Selected)!=0;Color bg=selected?ForeColor:BackColor,fg=selected?BackColor:ForeColor;
   using(var b=new SolidBrush(bg))e.Graphics.FillRectangle(b,e.Bounds);
   string text=e.Index>=0?GetItemText(Items[e.Index]):Text;
   TextRenderer.DrawText(e.Graphics,text,Font,e.Bounds,fg,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
   if((e.State&DrawItemState.Focus)!=0)e.DrawFocusRectangle();
  }
  protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg!=0xF&&m.Msg!=0x318&&m.Msg!=0x317)return;using(var g=m.Msg==0x318||m.Msg==0x317?Graphics.FromHdc(m.WParam):Graphics.FromHwnd(Handle)){
   using(var b=new SolidBrush(BackColor))g.FillRectangle(b,ClientRectangle);
   using(var p=new Pen(ForeColor))g.DrawRectangle(p,0,0,Width-1,Height-1);
   int arrow=SystemInformation.VerticalScrollBarWidth+4;
   TextRenderer.DrawText(g,Text,Font,new Rectangle(4,1,Math.Max(1,Width-arrow-6),Height-2),ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
   ThemeDrawing.Arrow(g,new Rectangle(Width-arrow,2,arrow,Height-4),ForeColor,true,false);
   if(Focused&&!DroppedDown)ControlPaint.DrawFocusRectangle(g,new Rectangle(3,3,Math.Max(1,Width-arrow-4),Math.Max(1,Height-6)),ForeColor,BackColor);
  }}
 }
 public sealed class ThemeCheckBox : CheckBox {
  protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(BackColor);int size=Math.Max(13,Font.Height-2);var r=new Rectangle(1,(Height-size)/2,size,size);ThemeDrawing.Check(e.Graphics,r,ForeColor,BackColor,Checked);var text=new Rectangle(size+7,0,Math.Max(1,Width-size-7),Height);TextRenderer.DrawText(e.Graphics,Text,Font,text,ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,text,ForeColor,BackColor);}
 }
 // Zachowuje natywną obsługę klawiatury, walidacji i przytrzymania strzałek.
 public sealed class ThemeNumber : NumericUpDown {
  PaintHook arrows;
  public ThemeNumber(){BorderStyle=BorderStyle.FixedSingle;}
  protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);foreach(Control c in Controls)if(!(c is TextBox)){arrows=new PaintHook(c,PaintArrows);break;}}
  void PaintArrows(Graphics g,Rectangle r){using(var b=new SolidBrush(BackColor))g.FillRectangle(b,r);Color color=Enabled?ForeColor:ControlPaint.Dark(ForeColor);using(var p=new Pen(color)){g.DrawRectangle(p,0,0,r.Width-1,r.Height-1);g.DrawLine(p,0,r.Height/2,r.Width,r.Height/2);}ThemeDrawing.Arrow(g,new Rectangle(0,0,r.Width,r.Height/2),color,true,true);ThemeDrawing.Arrow(g,new Rectangle(0,r.Height/2,r.Width,r.Height-r.Height/2),color,true,false);}
  protected override void OnHandleDestroyed(EventArgs e){if(arrows!=null){arrows.Dispose();arrows=null;}base.OnHandleDestroyed(e);}
 }
 // Natywny TrackBar ignoruje część kolorów. Ten suwak ma tę samą skalę sekund,
 // kliknięcie/drag oraz klawisze strzałek, Home, End, PageUp i PageDown.
 public sealed class ThemeSeekBar : Control,IThemeAccent {
  int maximum=1,current;bool moving;
  public Color Accent {get;set;}
  public int Maximum {get{return maximum;}set{maximum=Math.Max(1,value);Value=current;Invalidate();}}
  public int Value {get{return current;}set{int next=Math.Max(0,Math.Min(maximum,value));if(next==current)return;current=next;Invalidate();AccessibilityNotifyClients(AccessibleEvents.ValueChange,-1);}}
  public ThemeSeekBar(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);TabStop=true;AccessibleRole=AccessibleRole.Slider;}
  int MarginX {get{return Math.Max(8,Height/5);}}
  void SetPosition(int x){Value=(int)Math.Round((double)(x-MarginX)*Maximum/Math.Max(1,Width-2*MarginX));}
  protected override void OnPaint(PaintEventArgs e){
   int margin=MarginX,y=Height/2,x=margin+(int)((double)Value/Maximum*Math.Max(1,Width-2*margin));
   Color track=ControlPaint.Dark(ForeColor),fill=Enabled?Accent:track;
   using(var p=new Pen(track,3))e.Graphics.DrawLine(p,margin,y,Width-margin,y);
   using(var p=new Pen(fill,3))e.Graphics.DrawLine(p,margin,y,x,y);
   using(var b=new SolidBrush(fill))e.Graphics.FillRectangle(b,x-5,y-9,10,18);
   if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-2,-2),ForeColor,BackColor);
  }
  protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left&&Enabled){Focus();moving=true;Capture=true;SetPosition(e.X);}base.OnMouseDown(e);}
  protected override void OnMouseMove(MouseEventArgs e){if(moving)SetPosition(e.X);base.OnMouseMove(e);}
  protected override void OnMouseUp(MouseEventArgs e){if(e.Button==MouseButtons.Left&&moving){SetPosition(e.X);moving=false;Capture=false;}base.OnMouseUp(e);}
  protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture)moving=false;base.OnMouseCaptureChanged(e);}
  protected override bool IsInputKey(Keys key){switch(key&Keys.KeyCode){case Keys.Left:case Keys.Right:case Keys.Up:case Keys.Down:case Keys.Home:case Keys.End:case Keys.PageUp:case Keys.PageDown:return true;}return base.IsInputKey(key);}
  protected override void OnKeyDown(KeyEventArgs e){switch(e.KeyCode){case Keys.Left:case Keys.Down:Value--;break;case Keys.Right:case Keys.Up:Value++;break;case Keys.PageDown:Value-=10;break;case Keys.PageUp:Value+=10;break;case Keys.Home:Value=0;break;case Keys.End:Value=Maximum;break;default:base.OnKeyDown(e);return;}e.Handled=true;base.OnKeyDown(e);}
  protected override AccessibleObject CreateAccessibilityInstance(){return new SeekAccessible(this);}
  sealed class SeekAccessible : ControlAccessibleObject {
   readonly ThemeSeekBar owner;public SeekAccessible(ThemeSeekBar c):base(c){owner=c;}
   public override string Value {get{return owner.Value.ToString(CultureInfo.InvariantCulture);}set{int n;if(Int32.TryParse(value,out n))owner.Value=n;}}
  }
 }
 static class ThemeDrawing {
  public static void Check(Graphics g,Rectangle r,Color fg,Color bg,bool check){using(var b=new SolidBrush(bg))g.FillRectangle(b,r);using(var p=new Pen(fg)){g.DrawRectangle(p,r);if(check){p.Width=2;g.DrawLines(p,new[]{new Point(r.Left+3,r.Top+r.Height/2),new Point(r.Left+r.Width/2,r.Bottom-3),new Point(r.Right-2,r.Top+3)});}}}
  public static void Arrow(Graphics g,Rectangle r,Color color,bool vertical,bool start){int x=r.Left+r.Width/2,y=r.Top+r.Height/2,d=Math.Max(2,Math.Min(r.Width,r.Height)/4);Point[] points=vertical?(start?new[]{new Point(x,y-d),new Point(x-d,y+d),new Point(x+d,y+d)}:new[]{new Point(x,y+d),new Point(x-d,y-d),new Point(x+d,y-d)}):(start?new[]{new Point(x-d,y),new Point(x+d,y-d),new Point(x+d,y+d)}:new[]{new Point(x+d,y),new Point(x-d,y-d),new Point(x-d,y+d)});using(var b=new SolidBrush(color))g.FillPolygon(b,points);}
 }
 // Rysowanie nakładane po natywnym WM_PAINT; obsługa wejścia pozostaje w Windows.
 sealed class PaintHook : NativeWindow,IDisposable {
  readonly Action<Graphics,Rectangle> paint;
  [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr h,out ThemeListView.Rect r);
  public PaintHook(Control c,Action<Graphics,Rectangle> p):this(c.Handle,p){}
  public PaintHook(IntPtr h,Action<Graphics,Rectangle> p){paint=p;AssignHandle(h);}
  protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg!=0xF&&m.Msg!=0x318)return;ThemeListView.Rect r;if(!GetClientRect(Handle,out r))return;using(var g=m.Msg==0x318?Graphics.FromHdc(m.WParam):Graphics.FromHwnd(Handle))paint(g,Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom));}
  public void Dispose(){ReleaseHandle();}
 }
 public sealed class ThemeListView : ListView,IThemeAccent {
  [StructLayout(LayoutKind.Sequential)]public struct Rect {public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)]struct ScrollInfo {public int Size;public Rect Rect;public int Button,ThumbTop,ThumbBottom,Reserved;[MarshalAs(UnmanagedType.ByValArray,SizeConst=6)]public int[] State;}
  [DllImport("user32.dll")]static extern bool GetScrollBarInfo(IntPtr h,int objectId,ref ScrollInfo info);
  [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out Rect r);
  [DllImport("user32.dll")]static extern IntPtr GetWindowDC(IntPtr h);
  [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr h,IntPtr dc);
  [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
  [DllImport("user32.dll",EntryPoint="SendMessageW")]static extern IntPtr GetItemRect(IntPtr h,int msg,IntPtr w,ref Rect r);
  PaintHook header;IntPtr headerHandle;bool painting;
  public Color Accent {get;set;}
  public ThemeListView(){DoubleBuffered=true;BorderStyle=BorderStyle.FixedSingle;}
  protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);AttachHeader();}
  void AttachHeader(){if(!IsHandleCreated)return;var h=SendMessage(Handle,0x101F,IntPtr.Zero,IntPtr.Zero);if(h==IntPtr.Zero||h==headerHandle)return;if(header!=null)header.Dispose();headerHandle=h;header=new PaintHook(h,PaintHeader);}
  void PaintHeader(Graphics g,Rectangle bounds){using(var b=new SolidBrush(BackColor))g.FillRectangle(b,bounds);for(int i=0;i<Columns.Count;i++){Rect native=new Rect();if(GetItemRect(headerHandle,0x1207,new IntPtr(i),ref native)==IntPtr.Zero)continue;var r=Rectangle.FromLTRB(native.Left,native.Top,native.Right,native.Bottom);TextRenderer.DrawText(g,Columns[i].Text,Font,Rectangle.Inflate(r,-4,0),ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);using(var p=new Pen(Accent))g.DrawLine(p,r.Right-1,r.Top+4,r.Right-1,r.Bottom-4);}}
  protected override void OnHandleDestroyed(EventArgs e){if(header!=null){header.Dispose();header=null;}headerHandle=IntPtr.Zero;base.OnHandleDestroyed(e);}
  protected override void WndProc(ref Message m){
   base.WndProc(ref m);
   if(painting||!IsHandleCreated)return;
   if(m.Msg==0xF||m.Msg==0x85||m.Msg==0x115||m.Msg==0x114||m.Msg==0x20A||m.Msg==0xA0||m.Msg==0xA2||m.Msg==0x5||m.Msg==0x317){
    painting=true;try{AttachHeader();IntPtr dc=m.Msg==0x317?m.WParam:GetWindowDC(Handle);if(dc!=IntPtr.Zero)try{using(var g=Graphics.FromHdc(dc))PaintScrollbars(g);}finally{if(m.Msg!=0x317)ReleaseDC(Handle,dc);}}finally{painting=false;}
   }
  }
  void PaintScrollbars(Graphics g){
   Rect window;if(!GetWindowRect(Handle,out window))return;
   Rectangle vertical=PaintScrollbar(g,-5,true,window),horizontal=PaintScrollbar(g,-6,false,window);
   if(!vertical.IsEmpty&&!horizontal.IsEmpty)using(var b=new SolidBrush(BackColor))g.FillRectangle(b,vertical.Left,horizontal.Top,vertical.Width,horizontal.Height);
   using(var p=new Pen(Accent))g.DrawRectangle(p,0,0,Math.Max(0,Width-1),Math.Max(0,Height-1));
  }
  Rectangle PaintScrollbar(Graphics g,int id,bool vertical,Rect window){
   var info=new ScrollInfo{Size=Marshal.SizeOf(typeof(ScrollInfo)),State=new int[6]};
   if(!GetScrollBarInfo(Handle,id,ref info)||(info.State[0]&0x18000)!=0)return Rectangle.Empty;
   var r=Rectangle.FromLTRB(info.Rect.Left-window.Left,info.Rect.Top-window.Top,info.Rect.Right-window.Left,info.Rect.Bottom-window.Top);if(r.Width<=0||r.Height<=0)return Rectangle.Empty;
   using(var b=new SolidBrush(BackColor))g.FillRectangle(b,r);
   var first=vertical?new Rectangle(r.X,r.Y,r.Width,info.Button):new Rectangle(r.X,r.Y,info.Button,r.Height);
   var last=vertical?new Rectangle(r.X,r.Bottom-info.Button,r.Width,info.Button):new Rectangle(r.Right-info.Button,r.Y,info.Button,r.Height);
   ThemeDrawing.Arrow(g,first,ForeColor,vertical,true);ThemeDrawing.Arrow(g,last,ForeColor,vertical,false);
   var thumb=vertical?new Rectangle(r.X+3,r.Y+info.ThumbTop,r.Width-6,info.ThumbBottom-info.ThumbTop):new Rectangle(r.X+info.ThumbTop,r.Y+3,info.ThumbBottom-info.ThumbTop,r.Height-6);
   Color thumbColor=Color.FromArgb((Accent.R+BackColor.R)/2,(Accent.G+BackColor.G)/2,(Accent.B+BackColor.B)/2);
   if(thumb.Width>0&&thumb.Height>0)using(var b=new SolidBrush(thumbColor))g.FillRectangle(b,thumb);
   return r;
  }
 }
}

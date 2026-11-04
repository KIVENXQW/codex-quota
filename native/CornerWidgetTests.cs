using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CodexQuota;

class CornerWidgetTests {
  [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;}
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out NativeRect rectangle);
  [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr window,int index);
  static int checks;
  static Type corner,tray;
  static readonly Color Rose=Color.FromArgb(218,145,170);
  static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
  static object Call(Type type,string method,params object[] args){return type.GetMethod(method,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}
  static void Mouse(Form form,string method,int x,int y){corner.GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{new MouseEventArgs(MouseButtons.Left,1,x,y,0)});}
  static UserPreferences Preferences(string style,int transparency,string language) {
    var p=new UserPreferences {Language=language};
    typeof(UserPreferences).GetField("CornerStyle").SetValue(p,style);
    typeof(UserPreferences).GetField("GlassTransparency").SetValue(p,transparency);return p;
  }
  static QuotaState State(double? remaining,bool stale){var state=new QuotaState {Available=true,Stale=stale};state.Windows.Add(new QuotaWindow {Remaining=remaining,ResetAt=1794403080});return state;}
  static Bitmap Render(string style,int transparency,float scale,double? remaining,bool stale,bool dark){return (Bitmap)Call(corner,"Render",Preferences(style,transparency,"zh"),State(remaining,stale),scale,dark);}
  static int OpaqueInk(Bitmap image) {
    int n=0;for(int y=10;y<43;y++)for(int x=14;x<171;x++){Color c=image.GetPixel(x,y);if(c.A>=254&&c.R<90&&c.G<90&&c.B<100)n++;}return n;
  }
  static int AccentPixels(Bitmap image) {
    int n=0;for(int y=49;y<56;y++)for(int x=14;x<170;x++){Color c=image.GetPixel(x,y);if(c.A>200&&c.R-c.G>35&&c.B-c.G>12)n++;}return n;
  }
  [STAThread] static int Main() {
    try {
      corner=typeof(CornerWidgetTests).Assembly.GetType("CodexQuota.CornerWidget");
      tray=typeof(CornerWidgetTests).Assembly.GetType("CodexQuota.TrayTextIcon");
      Check(corner!=null,"Per-pixel glass corner renderer is missing");
      Check(tray!=null,"Bare numeric tray renderer is missing");
      using(var form=(Form)Activator.CreateInstance(corner)) {
        Check(!form.Visible&&!form.IsHandleCreated,"Constructing the corner must not show or create a native window");
        corner.GetMethod("ApplyPreferences").Invoke(form,new object[]{Preferences("tray",45,"zh")});
        corner.GetMethod("PositionAt").Invoke(form,new object[]{new Rectangle(-1920,-1080,1920,1040),1.5f});
        Check(!form.Visible&&!form.IsHandleCreated,"Tray style must stay hidden without creating an HWND");
      }
      using(var hidden=(Form)Activator.CreateInstance(corner))using(var bitmap=Render("card",80,1,93,false,false)) {
        corner.GetMethod("SetLayeredBitmap",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hidden,new object[]{bitmap});
        NativeRect rectangle;Check(GetWindowRect(hidden.Handle,out rectangle)&&rectangle.Right-rectangle.Left==184&&rectangle.Bottom-rectangle.Top==88,"Real UpdateLayeredWindow must upload the exact bitmap dimensions");
        Check(!IsWindowVisible(hidden.Handle),"Uploading a native alpha surface must not expose the test window");
        int styles=GetWindowLong(hidden.Handle,-20);
        Check((styles&0x08080080)==0x08080080&&!hidden.ShowInTaskbar,"Native corner must be layered, nonactivating, and omitted from the taskbar");
      }
      using(var hidden=(Form)Activator.CreateInstance(corner)) {
        // Exercise actual handlers on a hidden HWND. Clamp at both outer edges,
        // so the pointer passes the drag threshold but the window cannot move.
        hidden.Bounds=new Rectangle(456,392,184,88);
        corner.GetField("workingArea",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(hidden,new Rectangle(0,0,640,480));
        int activations=0;EventHandler activated=delegate{activations++;};
        corner.GetEvent("ActivatedByUser").AddEventHandler(hidden,activated);
        Mouse(hidden,"OnMouseDown",30,20);
        int movedX=30+SystemInformation.DragSize.Width+8,movedY=20+SystemInformation.DragSize.Height+8;
        Mouse(hidden,"OnMouseMove",movedX,movedY);
        Mouse(hidden,"OnMouseUp",movedX,movedY);
        Check(hidden.Location==new Point(456,392),"Outward edge drag must remain inside the working area");
        Check(activations==0,"Outward drag past the threshold must not activate details when edge clamping prevents movement");
        Mouse(hidden,"OnMouseDown",30,20);Mouse(hidden,"OnMouseUp",30,20);
        Check(activations==1,"A simple click after an edge drag must still activate details exactly once");
        Check(!IsWindowVisible(hidden.Handle),"Mouse-handler regression checks must never show their window");
      }
      foreach(string style in new[]{"card","capsule"})foreach(float scale in new[]{1f,1.5f,2f})using(var bitmap=Render(style,45,scale,100,false,false)) {
        Check(bitmap.Width==(int)((style=="card"?184:132)*scale)&&bitmap.Height==(int)((style=="card"?88:44)*scale),"Widget geometry must scale with DPI");
        Check(bitmap.GetPixel(0,0).A==0&&bitmap.GetPixel(bitmap.Width-1,bitmap.Height-1).A==0,"Rounded window corners must remain transparent");
      }
      using(var standard=Render("card",45,1,93,false,false))using(var clear=Render("card",80,1,93,false,false)) {
        Check(standard.GetPixel(90,78).A>clear.GetPixel(90,78).A+65,"Glass transparency must change background alpha substantially");
        Check(clear.GetPixel(90,78).A<90&&standard.GetPixel(90,78).A<190,"Glass interior must never become an opaque panel");
        Check(OpaqueInk(standard)>150&&OpaqueInk(clear)>150,"Large and small text must retain opaque ink at high transparency");
        Check(Math.Abs(OpaqueInk(standard)-OpaqueInk(clear))<15,"Background transparency must not fade the text");
      }
      using(var low=Render("card",45,1,0,false,false))using(var full=Render("card",45,1,100,false,false))using(var unknown=Render("card",45,1,null,false,false))using(var stale=Render("card",45,1,93,true,false)) {
        Check(AccentPixels(low)==0&&AccentPixels(unknown)==0,"Zero and missing values must not invent quota fill");
        Check(AccentPixels(full)>350,"100 percent must fill the progress track");
        Check(AccentPixels(stale)==0,"Stale quota must be visually distinct from current quota");
      }
      Check((string)Call(corner,"ValueText",State(0,false))=="0"&&(string)Call(corner,"ValueText",State(100,false))=="100","Corner must distinguish 0 from 100");
      Check((string)Call(corner,"ValueText",State(null,false))=="—","Corner must keep unknown values missing");
      var unavailable=State(93,false);unavailable.Available=false;
      Check((string)Call(corner,"ValueText",unavailable)=="—","Unavailable live state must not expose cached quota as current");
      Check(((string)Call(corner,"ResetText",State(93,true),false)).Contains("旧数据"),"Stale data must carry a visible label");
      Check((string)Call(corner,"ResetText",State(93,false),false)=="11/11 21:18 重置","Reset timestamp must retain the app's UTC+8 convention");
      var noReset=State(93,false);noReset.Windows[0].ResetAt=null;
      Check((string)Call(corner,"ResetText",noReset,false)=="暂无重置时间","Missing reset must remain missing");
      Rectangle area=new Rectangle(-1920,-1080,1920,1040);
      Rectangle bounds=(Rectangle)Call(corner,"CalculateBounds",area,1.5f,"card",null);
      Check(bounds==new Rectangle(-300,-196,276,132),"Default placement must honor negative monitor coordinates and scaled margin");
      bounds=(Rectangle)Call(corner,"CalculateBounds",area,2f,"capsule",(Point?)new Point(400,-2000));
      Check(bounds==new Rectangle(-264,-1080,264,88),"Dragged location must clamp to the monitor working area");
      Check((string)Call(tray,"CenterText",(double?)0,false)=="0"&&(string)Call(tray,"CenterText",(double?)100,false)=="100","Tray must retain numeric boundaries");
      Check((string)Call(tray,"CenterText",null,false)=="—"&&(string)Call(tray,"CenterText",(double?)Double.NaN,false)=="—","Tray unknown must differ from zero");
      Check((string)Call(tray,"CenterText",(double?)93,true)=="!"&&(string)Call(tray,"CenterText",null,true)=="—","Tray stale and missing semantics must match existing tray");
      using(var missing=(Bitmap)Call(tray,"Render",new Size(16,16),null,false,Rose,false,false)) {
        int first=16,last=-1;for(int y=0;y<16;y++)for(int x=0;x<16;x++)if(missing.GetPixel(x,y).A>128){first=Math.Min(first,y);last=Math.Max(last,y);}
        Check(last>=first&&last-first<5,"Missing tray quota must render as a thin dash, not a stretched solid block");
      }
      foreach(bool dark in new[]{false,true})foreach(int size in new[]{16,20,24,32})using(var bitmap=(Bitmap)Call(tray,"Render",new Size(size,size),(double?)100,false,Rose,dark,false)) {
        int ink=0;for(int y=0;y<size;y++)for(int x=0;x<size;x++){Color c=bitmap.GetPixel(x,y);if(c.A>220&&(dark?c.R>220:c.R<60))ink++;}
        Check(ink>size,"Numeric tray must have substantial high-contrast opaque strokes (size="+size+", dark="+dark+", opaque="+ink+")");
        Check(bitmap.GetPixel(0,0).A==0&&bitmap.GetPixel(size-1,size-1).A==0,"Bare number must not acquire an enclosing background");
      }
      using(var plain=(Bitmap)Call(tray,"Render",new Size(32,32),(double?)93,false,Rose,false,false))using(var updated=(Bitmap)Call(tray,"Render",new Size(32,32),(double?)93,false,Rose,false,true)) {
        int changed=0;bool numberPreserved=true;
        for(int y=0;y<32;y++)for(int x=0;x<32;x++){bool same=plain.GetPixel(x,y)==updated.GetPixel(x,y);if(!same)changed++;if(x<24&&y>7)numberPreserved&=same;}
        Check(changed>3&&numberPreserved,"Update indicator must be visible without overwriting digits");
      }
      using(var icon=(Icon)Call(tray,"Create",(double?)100,false,Rose,true,true,new Size(24,20)))Check(icon.Width==24&&icon.Height==20,"Native tray HICON must preserve rectangular system dimensions");
      Console.WriteLine("Corner widget and numeric tray: "+checks+" checks PASS");return 0;
    }catch(Exception error){if(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;Console.Error.WriteLine("FAIL: "+error.Message);return 1;}
  }
}

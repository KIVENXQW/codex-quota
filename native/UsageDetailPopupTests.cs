using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using CodexQuota;

class UsageDetailPopupTests {
  static int checks;
  static Type popupType;
  static void Check(bool result,string message){if(!result)throw new Exception(message);checks++;}
  static void Set(Form popup,ChartBucket bucket,Color accent,bool english){popupType.GetMethod("SetData",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(popup,new object[]{bucket,accent,english});}
  static Rectangle Place(Rectangle anchor,Size popup,Rectangle work){return (Rectangle)popupType.GetMethod("CalculatePlacement",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{anchor,popup,work});}
  [STAThread] static int Main(){try{Run();Console.WriteLine("Usage detail popup: "+checks+" checks PASS");return 0;}catch(Exception error){if(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;Console.Error.WriteLine("FAIL: "+error.Message);return 1;}}
  static void Run(){
    popupType=typeof(UsageDetailPopupTests).Assembly.GetType("CodexQuota.UsageDetailPopup");
    Check(popupType!=null,"Glass usage detail popup is missing");
    Color rose=Color.FromArgb(218,145,170);
    using(var popup=(Form)Activator.CreateInstance(popupType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{1f},null)) {
      var bucket=new ChartBucket {Start=new DateTime(2026,11,2),End=new DateTime(2026,11,2),Tokens=414417045,WorkSeconds=26532,Tasks=54,WorkPartial=true};
      Set(popup,bucket,rose,false);
      string text=popup.AccessibleDescription;
      Check(text.Contains("414,417,045")&&text.Contains("7时22分12秒")&&text.Contains("54"),"The rendered content must retain full Tokens, precise duration and task count");
      Check(text.Contains("星期一")&&text.Contains("2026 年 11 月 2 日"),"The card must include the complete date and weekday");
      Check(text.Contains("本机工作记录：部分覆盖")&&!text.Contains("Token 记录：部分覆盖"),"Local partial coverage must not be attributed to Tokens");
      int singleHeight=popup.Height;
      bucket.TokenPartial=true;Set(popup,bucket,rose,false);
      Check(popup.AccessibleDescription.Contains("Token 记录：部分覆盖")&&popup.Height>singleHeight,"Two independently partial sources need two readable notes");
      Set(popup,bucket,rose,true);
      Check(popup.AccessibleDescription.Contains("Monday")&&popup.AccessibleDescription.Contains("7h 22m 12s")&&popup.AccessibleDescription.Contains("Local work records: partial")&&popup.AccessibleDescription.Contains("Token records: partial"),"English must translate both values and independent coverage notes");
      bucket.End=bucket.Start.AddDays(6);Set(popup,bucket,rose,false);
      Check(popup.AccessibleDescription.Contains("2026/11/02 — 2026/11/08"),"A chart period must expose its full date range");
      Set(popup,new ChartBucket {Start=bucket.Start,End=bucket.Start,WorkSeconds=Double.NaN},rose,false);
      Check(popup.AccessibleDescription.Contains("Token 消耗：—")&&popup.AccessibleDescription.Contains("AI 工作时长：—")&&popup.AccessibleDescription.Contains("完成任务：—"),"Missing or nonfinite metrics must remain unavailable");
      Set(popup,new ChartBucket {Start=bucket.Start,End=bucket.Start,Tokens=0,WorkSeconds=0,Tasks=0},rose,false);
      Check(popup.AccessibleDescription.Contains("Token 消耗：0")&&popup.AccessibleDescription.Contains("AI 工作时长：0时0分0秒")&&popup.AccessibleDescription.Contains("完成任务：0"),"Known zero must differ from unknown");
      var parameters=(CreateParams)popupType.GetProperty("CreateParams",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(popup,null);
      Check((parameters.ExStyle&0x08000080)==0x08000080&&(bool)popupType.GetProperty("ShowWithoutActivation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(popup,null),"Hover cards must use a nonactivating tool window");
      var hitArgs=new object[]{Message.Create(IntPtr.Zero,0x0084,IntPtr.Zero,IntPtr.Zero)};
      popupType.GetMethod("WndProc",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(popup,hitArgs);
      Check(((Message)hitArgs[0]).Result==new IntPtr(-1),"The hover card must pass pointer hit testing through to the chart");
      using(var image=new Bitmap(popup.Width,popup.Height))popup.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
      Check(!popup.Visible,"Rendering verification must not show the window");
    }
    var work=new Rectangle(0,0,1920,1080);var size=new Size(312,289);
    var right=Place(new Rectangle(100,200,24,24),size,work);Check(right.Left==134&&work.Contains(right),"Prefer the anchor's right side when it fits");
    var left=Place(new Rectangle(1800,900,24,24),size,work);Check(left.Right==1790&&left.Bottom==work.Bottom&&work.Contains(left),"Right and bottom edges must flip left and clamp vertically");
    var negativeWork=new Rectangle(-1920,-200,1920,1080);var negative=Place(new Rectangle(-1900,-190,24,24),size,negativeWork);Check(negativeWork.Contains(negative),"Negative-origin monitors must remain in their own working area");
    var smallWork=new Rectangle(-100,30,180,150);Check(smallWork.Contains(Place(new Rectangle(20,40,24,24),size,smallWork)),"Small working areas must not overflow");
  }
}

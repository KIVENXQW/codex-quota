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
    popupType=typeof(ChartBucket).Assembly.GetType("CodexQuota.UsageDetailPopup");
    Check(popupType!=null,"Usage detail popup is missing");
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
      int completeWidth=popup.Width, completeHeight=popup.Height;
      Check(completeWidth>=240&&completeWidth<=270&&completeHeight>=115&&completeHeight<=145,
        "A complete daily card should fit beside the chart in about 250 by 130 logical pixels");
      Check(completeHeight<singleHeight,"Only unavailable or partially covered sources should consume note rows");
      Check(popup.AccessibleDescription.Contains("账户")&&popup.AccessibleDescription.Contains("本机"),"Readers should be able to identify each metric's source with complete coverage");
      Set(popup,new ChartBucket {Start=new DateTime(2026,9,30),End=new DateTime(2026,9,30),Tokens=Int64.MaxValue,WorkSeconds=1e16,Tasks=Int32.MaxValue},rose,true);
      Check(popup.AccessibleDescription.Contains("9,223,372,036,854,775,807")&&popup.AccessibleDescription.Contains("2,147,483,647")&&popup.AccessibleDescription.Contains("September 30, 2026")&&popup.AccessibleDescription.Contains("Wednesday"),"Long English dates and exact counts must survive formatting");
      Check(popup.Width>completeWidth&&popup.Height==completeHeight,"Wide values must expand their columns instead of truncating or adding empty vertical space");
      Set(popup,new ChartBucket {Start=bucket.Start,End=bucket.Start,Tokens=-1,WorkSeconds=0,Tasks=null},rose,false);
      Check(popup.AccessibleDescription.Contains("Token 记录：暂无记录")&&popup.AccessibleDescription.Contains("本机工作记录：部分覆盖")&&popup.AccessibleDescription.Contains("AI 工作时长：0时0分0秒")&&popup.AccessibleDescription.Contains("完成任务：—"),"A known local duration and missing task count should show partial local coverage independently of account coverage");
      var setHint=popupType.GetMethod("SetHint",BindingFlags.NonPublic|BindingFlags.Instance);
      Check(setHint!=null,"The passive popup must also support a plain text hint without chart rows");
      const string swatch="玫瑰粉 · #DA91AA";
      setHint.Invoke(popup,new object[]{swatch});
      int oneLineHeight=popup.Height;
      Check(popup.AccessibleName==swatch&&popup.AccessibleDescription==swatch&&oneLineHeight<50&&popup.Width<completeWidth,"A one-line swatch must only expose its text and use a smaller card");
      const string quota="Weekly  Remaining 75%\nUsed 25%\nReset 2026-11-09 15:30 UTC+8";
      setHint.Invoke(popup,new object[]{quota});
      Check(popup.Height>oneLineHeight+20&&popup.AccessibleDescription==quota,"Explicit line breaks in the three-line English quota summary must be preserved");
      string longHint=String.Concat(System.Linq.Enumerable.Repeat("本机工作记录和账户用量按北京时间更新。",8))+"END";
      setHint.Invoke(popup,new object[]{longHint});
      Check(popup.Width<=340&&popup.Height>oneLineHeight*2&&popup.AccessibleDescription==longHint,"Long localized hints must wrap into additional lines and retain their complete accessible text");
      using(var hintImage=new Bitmap(popup.Width,popup.Height)) {
        popup.DrawToBitmap(hintImage,new Rectangle(Point.Empty,hintImage.Size));
        bool lastLine=false;
        for(int y=hintImage.Height-25;y<hintImage.Height-5;y++)
          for(int x=8;x<hintImage.Width-8;x++)if(hintImage.GetPixel(x,y).R<180)lastLine=true;
        Check(lastLine,"The final line of the wrapped hint must actually paint near the bottom");
      }
      Set(popup,bucket,rose,true);
      Check(popup.AccessibleDescription.Contains("7h 22m 12s")&&!popup.AccessibleDescription.Contains("END")&&popup.Height>completeHeight,"Returning to a chart bucket must restore metrics and source-specific coverage");
      var parameters=(CreateParams)popupType.GetProperty("CreateParams",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(popup,null);
      Check((parameters.ExStyle&0x08000080)==0x08000080&&(bool)popupType.GetProperty("ShowWithoutActivation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(popup,null),"Hover cards must use a nonactivating tool window");
      var hitArgs=new object[]{Message.Create(IntPtr.Zero,0x0084,IntPtr.Zero,IntPtr.Zero)};
      popupType.GetMethod("WndProc",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(popup,hitArgs);
      Check(((Message)hitArgs[0]).Result==new IntPtr(-1),"The hover card must pass pointer hit testing through to the chart");
      using(var image=new Bitmap(popup.Width,popup.Height))popup.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
      Check(!popup.Visible,"Rendering verification must not show the window");
    }
    var work=new Rectangle(0,0,1920,1080);var size=new Size(252,126);
    var right=Place(new Rectangle(100,200,24,24),size,work);Check(right.Left==134&&work.Contains(right),"Prefer the anchor's right side when it fits");
    var left=Place(new Rectangle(1800,1020,24,24),size,work);Check(left.Right==1790&&left.Bottom==work.Bottom&&work.Contains(left),"Right and bottom edges must flip left and clamp vertically");
    var negativeWork=new Rectangle(-1920,-200,1920,1080);var negative=Place(new Rectangle(-1900,-190,24,24),size,negativeWork);Check(negativeWork.Contains(negative),"Negative-origin monitors must remain in their own working area");
    var narrowWork=new Rectangle(0,0,400,350);
    var centerAnchor=new Rectangle(190,35,18,18);
    var below=Place(centerAnchor,size,narrowWork);
    Check(narrowWork.Contains(below)&&below.Top>centerAnchor.Bottom,"When neither side fits, keep the hovered cell visible by placing the card below");
    centerAnchor.Y=295;
    var above=Place(centerAnchor,size,narrowWork);
    Check(narrowWork.Contains(above)&&above.Bottom<centerAnchor.Top,"Near the bottom on a narrow monitor, keep the hovered cell visible above it");
    var smallWork=new Rectangle(-100,30,180,150);Check(smallWork.Contains(Place(new Rectangle(20,40,24,24),size,smallWork)),"Small working areas must not overflow");
  }
}

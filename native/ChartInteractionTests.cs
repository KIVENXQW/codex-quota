using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using CodexQuota;
class ChartInteractionTests {
  static void Paint(Control control) {using(var bitmap=new Bitmap(control.Width,control.Height))control.DrawToBitmap(bitmap,new Rectangle(Point.Empty,control.Size));}
  static void Move(Control control,int x,int y) {typeof(Control).GetMethod("OnMouseMove",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(control,new object[]{new MouseEventArgs(MouseButtons.None,0,x,y,0)});}
  static List<ChartBucket> Buckets(int count) {var result=new List<ChartBucket>();for(int i=0;i<count;i++)result.Add(new ChartBucket {Start=new DateTime(2026,10,1).AddDays(i),End=new DateTime(2026,10,1).AddDays(i),Label="10/"+(i+1),Tokens=1000+i,WorkSeconds=3600,Tasks=1});return result;}
  static string ReadHint(Control view,string hitTest,string describe,Point point) {
    const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
    int index=(int)view.GetType().GetMethod(hitTest,flags).Invoke(view,new object[]{point});
    return index<0?"":(string)view.GetType().GetMethod(describe,flags).Invoke(view,new object[]{index});
  }
  static void HomeDetails() {
    var state=new QuotaState {Available=true,Credits="5"};
    state.Windows.Add(new QuotaWindow {Label="Weekly",Remaining=63});
    state.Windows.Add(new QuotaWindow {Label="Five hours",Remaining=90});
    using(var view=new MinimalOverview(1)) {
      view.Size=new Size(370,526);
      view.SetData(state,2699408899L,7514,17,"Account records","Local records","Today",false,new UserPreferences{Language="en"});
      if(!ReadHint(view,"HintAt","DescribeHint",new Point(10,24)).Contains("63%") ||
         !ReadHint(view,"HintAt","DescribeHint",new Point(10,149)).Contains("90%"))
        throw new Exception("A quota-row hint must use only the matching quota window");
      string tokens=ReadHint(view,"HintAt","DescribeHint",new Point(10,252));
      string work=ReadHint(view,"HintAt","DescribeHint",new Point(10,345));
      if(!tokens.Contains("2,699,408,899") || tokens.Contains("Local records") ||
         !work.Contains("2h 5m 14s") || work.Contains("Account records"))
        throw new Exception("Hovering a metric must show its own precise value and coverage");
      if(ReadHint(view,"HintAt","DescribeHint",new Point(10,182))!="")
        throw new Exception("The credits separator must not show an unrelated metric");
      view.SetData(new QuotaState(),null,null,null,"","","",true,new UserPreferences());
      if(!ReadHint(view,"HintAt","DescribeHint",new Point(10,147)).Contains("—"))
        throw new Exception("Missing daily Tokens must not become a zero in the hint");
    }
    using(var font=new Font("Microsoft YaHei UI",12))
    using(var view=new QuotaSummaryView(1,font,font,font)) {
      view.Size=new Size(360,144);
      state.Windows.Insert(1,null);
      // QuotaSummaryView lays out two columns. Use the second row after a missing entry.
      view.Height=238;
      view.SetData(state,Color.RosyBrown,true);Paint(view);
      if(ReadHint(view,"WindowAt","DescribeWindow",new Point(220,46))!="" ||
         !ReadHint(view,"WindowAt","DescribeWindow",new Point(40,146)).Contains("90%"))
        throw new Exception("Glass quota hint cells must retain their indexes when a window is missing");
    }
  }
  [STAThread] static void Main() {
    HomeDetails();
    using(var chart=new UsageBarChart(1)){chart.Size=new Size(380,150);chart.SetData(Buckets(7),false,Color.RosyBrown,false);Paint(chart);Move(chart,350,70);chart.SetData(Buckets(4),false,Color.RosyBrown,false);Move(chart,350,70);}
    using(var chart=new TokenHeatmap(1)){chart.Size=new Size(380,120);chart.SetData(Buckets(365),Color.RosyBrown,false);Paint(chart);chart.SetData(new List<ChartBucket>(),Color.RosyBrown,false);Move(chart,350,60);}
    using(var font=new Font("Microsoft YaHei UI",12))using(var quota=new QuotaSummaryView(1,font,font,font)){quota.Size=new Size(380,140);var state=new QuotaState();state.Windows.Add(new QuotaWindow {Label="A",Remaining=50});state.Windows.Add(new QuotaWindow {Label="B",Remaining=60});quota.SetData(state,Color.RosyBrown,false);Paint(quota);Move(quota,300,60);quota.SetData(new QuotaState(),Color.RosyBrown,false);Move(quota,300,60);}
    using (var detail = new UsageDetailPopup(1)) {
      detail.SetData(new ChartBucket { Start=new DateTime(2026,11,1), End=new DateTime(2026,11,1), Tokens=1234567, WorkSeconds=3720, Tasks=7, TokenPartial=true },Color.RosyBrown,true);
      string text=detail.AccessibleDescription;
      if(!text.Contains("1,234,567") || !text.Contains("1h 2m 0s") || !text.Contains("Completed tasks: 7") || !text.Contains("partial"))
        throw new Exception("Missing exact values or coverage in the detail");
    }
    Console.WriteLine("Native chart interactions: 9 checks PASS");
  }
}

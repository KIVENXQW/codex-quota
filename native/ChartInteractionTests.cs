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
  [STAThread] static void Main() {
    using(var chart=new UsageBarChart(1)){chart.Size=new Size(380,150);chart.SetData(Buckets(7),false,Color.RosyBrown,false);Paint(chart);Move(chart,350,70);chart.SetData(Buckets(4),false,Color.RosyBrown,false);Move(chart,350,70);}
    using(var chart=new TokenHeatmap(1)){chart.Size=new Size(380,120);chart.SetData(Buckets(365),Color.RosyBrown,false);Paint(chart);chart.SetData(new List<ChartBucket>(),Color.RosyBrown,false);Move(chart,350,60);}
    using(var font=new Font("Microsoft YaHei UI",12))using(var quota=new QuotaSummaryView(1,font,font,font)){quota.Size=new Size(380,140);var state=new QuotaState();state.Windows.Add(new QuotaWindow {Label="A",Remaining=50});state.Windows.Add(new QuotaWindow {Label="B",Remaining=60});quota.SetData(state,Color.RosyBrown,false);Paint(quota);Move(quota,300,60);quota.SetData(new QuotaState(),Color.RosyBrown,false);Move(quota,300,60);}
    string tooltip=UsageText.BucketTooltip(new ChartBucket {Start=new DateTime(2026,11,1),End=new DateTime(2026,11,1),Tokens=1234567,WorkSeconds=3720,Tasks=7,TokenPartial=true},true);
    if(!tooltip.Contains("1,234,567") || !tooltip.Contains("1h 2m") || !tooltip.Contains("Completed tasks  7") || !tooltip.Contains("partial"))throw new Exception("Missing exact tooltip detail");
    Console.WriteLine("Native chart interactions: 4 checks PASS");
  }
}

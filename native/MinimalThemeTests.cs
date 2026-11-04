using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CodexQuota;

class MinimalThemeTests {
  static int checks;
  static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
  static object Field(object target,string name){return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(target);}
  static Control Named(Control parent,string name){var controls=parent.Controls.Find(name,true);Check(controls.Length==1,"Required theme control: "+name);return controls[0];}
  static void Click(Control target){typeof(Control).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,new object[]{EventArgs.Empty});}
  static void Page(QuotaPopup popup,string page){typeof(QuotaPopup).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(popup,new object[]{page});}
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
  static Bitmap Paint(Control target){var result=new Bitmap(target.Width,target.Height);using(var g=Graphics.FromImage(result)){g.Clear(Color.Magenta);IntPtr dc=g.GetHdc();try{SendMessage(target.Handle,15,dc,IntPtr.Zero);}finally{g.ReleaseHdc(dc);}}return result;}
  static int Ink(Bitmap image,Rectangle area){int ink=0;area.Intersect(new Rectangle(Point.Empty,image.Size));for(int y=area.Top;y<area.Bottom;y++)for(int x=area.Left;x<area.Right;x++){Color c=image.GetPixel(x,y);if(c.R<180&&c.G<180&&c.B<180)ink++;}return ink;}
  static QuotaState Fixture(){var state=new QuotaState{Available=true,UpdatedAt=DateTime.UtcNow.ToString("o"),Activity=new WorkActivity{Status="ready",TotalWorkSeconds=3720,CompletedTasks=14}};state.Windows.Add(new QuotaWindow{Label="本周",Remaining=85});state.Activity.DailyBuckets.Add(new WorkDay{Date=DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-dd"),WorkSeconds=3720,CompletedTasks=14});return state;}
  [STAThread] static int Main(){try{Application.EnableVisualStyles();HistoryTotals();Run();Console.WriteLine("Minimal theme: "+checks+" checks PASS");return 0;}catch(Exception e){Console.Error.WriteLine("FAIL: "+(e.InnerException??e).Message);return 1;}}
  static void HistoryTotals(){
    using(var popup=new QuotaPopup()){
      DateTime today=DateTime.UtcNow.AddHours(8).Date;var state=new QuotaState{Available=true,Activity=new WorkActivity{Status="ready"}};
      int[] offsets={0,15,70},counts={1,2,4};
      for(int i=0;i<offsets.Length;i++){string date=today.AddDays(-offsets[i]).ToString("yyyy-MM-dd");state.DailyTokens.Add(new TokenDay{Date=date,Tokens=counts[i]*100});state.Activity.DailyBuckets.Add(new WorkDay{Date=date,WorkSeconds=counts[i]*600,CompletedTasks=counts[i]});}
      popup.SetState(state);Page(popup,"history");Label totals=(Label)Named(popup,"historyTotals");
      string text=totals.Text.Replace(" ","").Replace("*","");Check(text.Contains("100Tokens")&&text.Contains("10分钟")&&text.Contains("1回合"),"Minimal history exposes the selected seven-day totals");
      Click((Control)Field(popup,"week"));text=totals.Text.Replace(" ","").Replace("*","");Check(text.Contains("300Tokens")&&text.Contains("30分钟")&&text.Contains("3回合"),"Changing to four weeks recomputes the same selected-range totals");
      Click((Control)Field(popup,"month"));text=totals.Text.Replace(" ","").Replace("*","");Check(text.Contains("700Tokens")&&text.Contains("1时10分")&&text.Contains("7回合"),"Changing to six months retains all selected-range metrics");
      Check(totals.Text.Contains("*")&&totals.AccessibleDescription.Contains("部分"),"Partial coverage remains explicit in the compact totals");
      Check(totals.Bottom<=((Control)Field(popup,"chartTitle")).Top&&totals.Right<=((Control)Field(popup,"mainPage")).ClientSize.Width,"Compact totals occupy the existing gap without overlapping the chart or widening the page");
      popup.ApplyPreferences(new UserPreferences{PanelStyle="minimal",Language="en"});Check(totals.Text.Contains("7 turns")&&totals.AccessibleName.Contains("700")&&totals.AccessibleDescription.Contains("Partial"),"English totals include all values and their coverage");
      popup.SetState(new QuotaState());int dashes=0;foreach(char c in totals.Text)if(c=='—')dashes++;Check(dashes==3&&!totals.Text.Contains("700"),"Missing history replaces every old total with an unavailable marker");
      using(var bitmap=Paint(totals))Check(Ink(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height))>15,"Compact summary produces visible text through real WM_PAINT");
      state.DailyTokens.Clear();state.DailyTokens.Add(new TokenDay{Date=today.ToString("yyyy-MM-dd"),Tokens=Int64.MaxValue});popup.SetState(state);
      Check(totals.AutoEllipsis&&totals.AccessibleName.Contains("9,223,372,036,854,775,807"),"Long totals stay bounded while their full exact value remains accessible");
    }
  }
  static void Run(){
    using(var popup=new QuotaPopup()){
      float scale=(float)Field(popup,"scale");
      Check(popup.Width==(int)Math.Round(410*scale),"Default minimal panel must fit the approved 410 logical pixel width");
      Check(!((Control)Field(popup,"refresh")).Bounds.IntersectsWith(((Control)Field(popup,"appearance")).Bounds),"Header refresh and appearance actions must be separate before first Show");
      var fixture=Fixture();popup.SetState(fixture);
      Control overview=Named(popup,"minimalOverview");
      Check(overview.AccessibleName.Contains("85%")&&overview.AccessibleName.Contains("14"),"Minimal home exposes actual quota and completed turns");
      Check(overview.AccessibleName.Contains("—")&&!overview.AccessibleName.Contains("每小时"),"Missing account usage stays missing and an unsupported rate is not presented as a metric");
      int initialHeight=overview.Height;
      fixture.Windows.Add(new QuotaWindow{Label="5 小时",Remaining=100});fixture.Credits="12.5";popup.SetState(fixture);
      Check(overview.AccessibleName.Contains("100%")&&overview.AccessibleName.Contains("5 小时")&&overview.AccessibleName.Contains("12.5"),"Secondary quota windows and credits must not disappear in minimal mode");
      Check(overview.Height>initialHeight,"Secondary quota details receive their own visible layout space");
      int changes=0;popup.PreferencesChanged+=delegate{changes++;};Page(popup,"settings");
      Click(Named(popup,"themeGlass"));
      Check(popup.Preferences.PanelStyle=="glass"&&popup.Width==(int)Math.Round(448*scale)&&changes==1,"Selecting glass applies immediately and publishes one save event");
      Click(Named(popup,"themeMinimal"));
      Check(popup.Preferences.PanelStyle=="minimal"&&popup.Width==(int)Math.Round(410*scale)&&changes==2,"Selecting minimal restores its layout and publishes one save event");
      popup.ApplyPreferences(new UserPreferences{PanelStyle="minimal",Language="en",AccentHtml="#477ABB",CornerStyle="capsule",GlassTransparency=63});
      Check(popup.Preferences.GlassTransparency==63&&popup.Preferences.CornerStyle=="capsule","Theme changes retain corner settings");
      Check(Named(popup,"themeMinimal").Text=="Minimal","Theme controls translate with the rest of the appearance page");
      Page(popup,"home");fixture.Windows.RemoveAt(1);fixture.Credits=null;popup.SetState(fixture);
      Check(overview.AccessibleName.Contains("85%")&&overview.AccessibleName.Contains("1h 2m"),"Language changes preserve and translate exact visible usage values");
      var old=(string[])Field(Field(popup,"overview"),"values");Check(old.Length==4&&old[3]=="—","Glass overview data contract is preserved even when its unsupported rate is hidden in minimal mode");
      foreach(string style in new[]{"minimal","glass","minimal"}){
        var p=popup.Preferences;p.PanelStyle=style;popup.ApplyPreferences(p);
        foreach(string name in new[]{"homeTab","historyTab"}){Control tab=Named(popup,name);using(var image=Paint(tab)){Check(Ink(image,new Rectangle(3,3,image.Width-6,image.Height-6))>12,"Native painted tab label stays visible after theme switch: "+style+"/"+name);}}
      }
      popup.SetState(new QuotaState());Check(overview.AccessibleName.Contains("—")&&!overview.AccessibleName.Contains("85%"),"Missing state clears the previous quota instead of retaining a stale current value");
      for(int i=0;i<90;i++)fixture.DailyTokens.Add(new TokenDay{Date=DateTime.UtcNow.AddHours(8).Date.AddDays(-i).ToString("yyyy-MM-dd"),Tokens=i%8==0?0:1000*(i%5+1)});
      popup.SetState(fixture);Page(popup,"history");int height=popup.Height;Check(((Control)Field(popup,"bars")).Width>0&&((Control)Field(popup,"heatmap")).Width>0,"Minimal history keeps both real interactive charts");
      Control heatmap=(Control)Field(popup,"heatmap");using(var bitmap=Paint(heatmap)){
        var cells=(List<RectangleF>)Field(heatmap,"cells");var buckets=(List<ChartBucket>)Field(heatmap,"buckets");
        Check(cells.Count==buckets.Count&&cells.Count>=90,"Compact heatmap still paints every date in the selected period");
        foreach(var cell in cells)Check(cell.Left>=0&&cell.Right<=heatmap.Width&&cell.Bottom<=heatmap.Height-43*scale,"Compact heatmap keeps seven rows and room for both legend lines inside its bounds");
        var hit=heatmap.GetType().GetMethod("BucketAt",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        Check(hit!=null,"Compact heatmap pointer lookup uses its rendered cell geometry");
        foreach(int index in new[]{0,cells.Count/2,cells.Count-1}){
          RectangleF cell=cells[index];int actual=(int)hit.Invoke(heatmap,new object[]{new Point((int)(cell.Left+cell.Width/2),(int)(cell.Top+cell.Height/2))});
          Check(actual==index&&buckets[actual].Start==buckets[0].Start.AddDays(index),"Compact pointer hit resolves the intended date: "+index);
        }
        Check(Ink(bitmap,new Rectangle(0,heatmap.Height-(int)(42*scale),heatmap.Width,(int)(42*scale)))>20,"Compact heatmap keeps its actual legend text visible");
      }
      Page(popup,"home");Check(popup.Height==height,"Home and History keep a stable panel height");
      Control home=(Control)Field(popup,"homePage");Check(overview.Right<=home.ClientSize.Width,"Minimal content does not overflow its horizontal viewport");
    }
  }
}

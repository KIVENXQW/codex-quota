using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using CodexQuota;
class GlassPopupTests {
  static int checks;
  static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
  static Control Named(Control parent,string name){var found=parent.Controls.Find(name,true);Check(found.Length==1,"Missing or duplicated control: "+name);return found[0];}
  static void Click(Control target){typeof(Control).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,new object[]{EventArgs.Empty});}
  static string Page(QuotaPopup popup){return (string)typeof(QuotaPopup).GetField("page",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(popup);}
  [STAThread] static int Main(){try{Run();return 0;}catch(Exception error){Console.Error.WriteLine("FAIL: "+error.Message);return 1;}}
  static void Run(){
    using(var popup=new QuotaPopup()) {
      var history=Named(popup,"historyTab");var home=Named(popup,"homeTab");
      Check(Page(popup)=="home","Homepage is the first page");
      Click(history);Check(Page(popup)=="history","History tab navigates to history");
      Click(home);Check(Page(popup)=="home","Home tab navigates back");
      var state=new QuotaState {Available=true,LifetimeTokens=1234567,Activity=new WorkActivity {Status="ready",TotalWorkSeconds=3600,CompletedTasks=3}};
      state.Windows.Add(new QuotaWindow {Label="weekly",Remaining=72});
      string today=DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-dd");
      state.DailyTokens.Add(new TokenDay {Date=today,Tokens=12345});
      state.Activity.DailyBuckets.Add(new WorkDay {Date=today,WorkSeconds=3600,CompletedTasks=3});
      popup.SetState(state);
      var overview=Named(popup,"overview");
      var values=(string[])overview.GetType().GetField("values",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(overview);
      Check(values[0]=="12.3K"&&values[1]=="72%","Homepage uses actual selected-range usage and quota");
      Check(values[3]=="—","Unmatched account Tokens/local hours must not fabricate speed");
      popup.ApplyPreferences(new UserPreferences {Language="en",AccentHtml="#477ABB"});
      Check(home.Text=="Home"&&history.Text=="History","Tabs translate");
      Check(popup.Preferences.AccentHtml=="#477ABB","Accent preference is retained");
      using(var bmp=new Bitmap(popup.Width,popup.Height))popup.DrawToBitmap(bmp,new Rectangle(Point.Empty,popup.Size));
      popup.SetState(new QuotaState());
      using(var bmp=new Bitmap(popup.Width,popup.Height))popup.DrawToBitmap(bmp,new Rectangle(Point.Empty,popup.Size));
      Check(overview.AccessibleName.Contains("—"),"Unavailable data renders as unavailable");
    }
    Console.WriteLine("Glass popup: "+checks+" checks PASS");
  }
}

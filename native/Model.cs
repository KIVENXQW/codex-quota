using System;
using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.Web.Script.Serialization;
namespace CodexQuota {
  public class QuotaWindow { public string Label; public double? Remaining, Used; public long? ResetAt; }
  public class TokenDay { public string Date; public long Tokens; }
  public class WorkDay { public string Date; public double WorkSeconds; public int CompletedTasks; }
  public class WorkActivity {
    public string Status, Scope, CoveredFrom, CoveredTo, UpdatedAt;
    public int ThreadCount, CompletedTasks;
    public double TotalWorkSeconds;
    public List<WorkDay> DailyBuckets=new List<WorkDay>();
    public List<string> Warnings=new List<string>();
  }
  public class QuotaState {
    public bool Available, Stale;
    public string Error, UpdatedAt, Credits, LatestDate;
    public int? ResetCredits;
    public long? LifetimeTokens, LatestTokens;
    public long? PeakDailyTokens;
    public double? LongestRunningTurnSec;
    public int? CurrentStreakDays,LongestStreakDays;
    public List<TokenDay> DailyTokens=new List<TokenDay>();
    public WorkActivity Activity=new WorkActivity {Status="unavailable",Scope="local-codex"};
    public List<QuotaWindow> Windows = new List<QuotaWindow>();
    public QuotaWindow Primary { get {
      QuotaWindow result=null;
      foreach(var window in Windows) if(result==null || (window.Remaining.HasValue && (!result.Remaining.HasValue || window.Remaining < result.Remaining))) result=window;
      return result;
    } }
    static object Get(object source,string key) { var dict=source as Dictionary<string,object>; object value; return dict!=null && dict.TryGetValue(key,out value)?value:null; }
    static string Text(object value) { return value==null?null:Convert.ToString(value,CultureInfo.InvariantCulture); }
    static double? Number(object value) { double n; return value!=null && double.TryParse(Text(value),NumberStyles.Float,CultureInfo.InvariantCulture,out n) && !double.IsNaN(n) && !double.IsInfinity(n)?(double?)n:null; }
    static IEnumerable Items(object value) { return value as IEnumerable ?? new object[0]; }
    public static QuotaState Parse(string json) {
      var root=new JavaScriptSerializer().DeserializeObject(json);
      var s=new QuotaState {Available=object.Equals(Get(root,"ok"),true),Stale=object.Equals(Get(root,"stale"),true),Error=Text(Get(root,"error")),UpdatedAt=Text(Get(root,"updatedAt"))};
      foreach(var bucket in Items(Get(root,"limits"))) {
        foreach(var window in Items(Get(bucket,"windows"))) {
          double? remaining=Number(Get(window,"remainingPercent"));
          s.Windows.Add(new QuotaWindow {Label=Text(Get(window,"label"))??"使用额度",Remaining=remaining.HasValue?(double?)Math.Max(0,Math.Min(100,remaining.Value)):null,Used=Number(Get(window,"usedPercent")),ResetAt=(long?)Number(Get(window,"resetsAt"))});
        }
        var credit=Get(bucket,"credits");
        if(credit!=null && s.Credits==null)s.Credits=object.Equals(Get(credit,"unlimited"),true)?"不限额":Text(Get(credit,"balance"));
      }
      s.ResetCredits=(int?)Number(Get(root,"resetCreditsCount"));
      var usage=Get(root,"usage");
      var summary=Get(usage,"summary");
      s.LifetimeTokens=(long?)Number(Get(summary,"lifetimeTokens"));
      s.PeakDailyTokens=(long?)Number(Get(summary,"peakDailyTokens"));
      s.LongestRunningTurnSec=Number(Get(summary,"longestRunningTurnSec"));
      s.CurrentStreakDays=(int?)Number(Get(summary,"currentStreakDays"));
      s.LongestStreakDays=(int?)Number(Get(summary,"longestStreakDays"));
      foreach(var day in Items(Get(usage,"dailyUsageBuckets"))) {
        string date=Text(Get(day,"startDate"));
        long? tokens=(long?)Number(Get(day,"tokens"));
        if(date!=null && tokens.HasValue)s.DailyTokens.Add(new TokenDay {Date=date,Tokens=Math.Max(0,tokens.Value)});
        if(date!=null && (s.LatestDate==null || string.CompareOrdinal(date,s.LatestDate)>0)) {s.LatestDate=date;s.LatestTokens=(long?)Number(Get(day,"tokens"));}
      }
      var activity=Get(root,"activity");
      if(activity!=null) {
        s.Activity=new WorkActivity {Status=Text(Get(activity,"status"))??"unavailable",Scope=Text(Get(activity,"scope"))??"local-codex",CoveredFrom=Text(Get(activity,"coveredFrom")),CoveredTo=Text(Get(activity,"coveredTo")),UpdatedAt=Text(Get(activity,"updatedAt")),ThreadCount=(int?)Number(Get(activity,"threadCount"))??0,CompletedTasks=(int?)Number(Get(activity,"completedTasks"))??0,TotalWorkSeconds=Number(Get(activity,"totalWorkSeconds"))??0};
        foreach(var day in Items(Get(activity,"dailyBuckets"))) {
          string date=Text(Get(day,"date"));
          if(date!=null)s.Activity.DailyBuckets.Add(new WorkDay {Date=date,WorkSeconds=Number(Get(day,"workSeconds"))??0,CompletedTasks=(int?)Number(Get(day,"completedTasks"))??0});
        }
        foreach(var warning in Items(Get(activity,"warnings")))if(warning is string)s.Activity.Warnings.Add((string)warning);
      }
      return s;
    }
  }
}

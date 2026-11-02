using System;
using System.Collections.Generic;
using System.Globalization;
namespace CodexQuota {
  public class ChartBucket {
    public DateTime Start,End;
    public string Label;
    public long? Tokens;
    public double? WorkSeconds;
    public int? Tasks;
    public bool TokenPartial,WorkPartial;
  }
  public class UsageSummary {
    public long? Tokens;
    public double? WorkSeconds;
    public int? Tasks;
    public bool TokenPartial,WorkPartial;
  }
  public static class UsageAnalytics {
    public static List<ChartBucket> BuildBuckets(QuotaState state,string period,DateTime today) {
      today=today.Date;
      if(period!="week" && period!="month")return BuildDaily(state,today.AddDays(-6),today);
      DateTime current=period=="month"?new DateTime(today.Year,today.Month,1):today.AddDays(-(((int)today.DayOfWeek+6)%7));
      int count=period=="month"?6:4;
      var result=new List<ChartBucket>();
      for(int index=count-1;index>=0;index--) {
        DateTime start=period=="month"?current.AddMonths(-index):current.AddDays(-7*index);
        DateTime end=period=="month"?start.AddMonths(1).AddDays(-1):start.AddDays(6);
        if(end>today)end=today;
        var total=Summarize(BuildDaily(state,start,end));
        result.Add(new ChartBucket {Start=start,End=end,Label=start.ToString(period=="month"?"yyyy/MM":"MM/dd"),Tokens=total.Tokens,WorkSeconds=total.WorkSeconds,Tasks=total.Tasks,TokenPartial=total.TokenPartial,WorkPartial=total.WorkPartial});
      }
      return result;
    }
    public static List<ChartBucket> BuildDaily(QuotaState state,DateTime start,DateTime end) {
      var tokens=new Dictionary<string,long>();var work=new Dictionary<string,WorkDay>();
      if(state!=null)foreach(var day in state.DailyTokens)tokens[day.Date]=day.Tokens;
      var activity=state==null?null:state.Activity;
      if(activity!=null)foreach(var day in activity.DailyBuckets)work[day.Date]=day;
      DateTime from,to;
      bool full=activity!=null && activity.Status=="ready" && TryDate(activity.CoveredFrom,out from) && TryDate(activity.CoveredTo,out to);
      if(!TryDate(activity==null?null:activity.CoveredFrom,out from))from=DateTime.MaxValue;
      if(!TryDate(activity==null?null:activity.CoveredTo,out to))to=DateTime.MinValue;
      var result=new List<ChartBucket>();
      for(DateTime date=start.Date;date<=end.Date;date=date.AddDays(1)) {
        string key=date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);long token;WorkDay day;
        bool knownToken=tokens.TryGetValue(key,out token),knownWork=work.TryGetValue(key,out day),covered=full && date>=from && date<=to;
        result.Add(new ChartBucket {Start=date,End=date,Label=date.ToString("MM/dd"),Tokens=knownToken?(long?)token:null,WorkSeconds=knownWork?(double?)day.WorkSeconds:covered?0:(double?)null,Tasks=knownWork?(int?)day.CompletedTasks:covered?0:(int?)null,TokenPartial=!knownToken,WorkPartial=!covered});
      }
      return result;
    }
    static bool TryDate(string text,out DateTime date) {return DateTime.TryParseExact(text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date);}
    public static UsageSummary Summarize(List<ChartBucket> buckets) {
      var result=new UsageSummary();
      foreach(var bucket in buckets) {
        if(bucket.Tokens.HasValue)result.Tokens=(result.Tokens??0)+bucket.Tokens.Value;
        if(bucket.WorkSeconds.HasValue)result.WorkSeconds=(result.WorkSeconds??0)+bucket.WorkSeconds.Value;
        if(bucket.Tasks.HasValue)result.Tasks=(result.Tasks??0)+bucket.Tasks.Value;
        result.TokenPartial|=bucket.TokenPartial || !bucket.Tokens.HasValue;
        result.WorkPartial|=bucket.WorkPartial || !bucket.WorkSeconds.HasValue;
      }
      return result;
    }
  }
}

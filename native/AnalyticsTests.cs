using System;
using System.Collections.Generic;
using CodexQuota;
class AnalyticsTests {
  static void Check(bool ok,string message) {if(!ok){Console.Error.WriteLine("FAIL: "+message);Environment.Exit(1);}}
  static void Main() {
    var state=QuotaState.Parse("{\"ok\":true,\"usage\":{\"summary\":{\"lifetimeTokens\":3000,\"peakDailyTokens\":2000,\"longestRunningTurnSec\":130,\"currentStreakDays\":2,\"longestStreakDays\":4},\"dailyUsageBuckets\":[{\"startDate\":\"2026-10-30\",\"tokens\":1000},{\"startDate\":\"2026-10-31\",\"tokens\":2000}]},\"activity\":{\"status\":\"ready\",\"scope\":\"local-codex\",\"coveredFrom\":\"2026-10-26\",\"coveredTo\":\"2026-11-01\",\"totalWorkSeconds\":90,\"completedTasks\":2,\"threadCount\":1,\"dailyBuckets\":[{\"date\":\"2026-10-31\",\"workSeconds\":90,\"completedTasks\":2}]}}");
    Check(state.DailyTokens.Count==2 && state.PeakDailyTokens==2000,"parse real daily token data");
    Check(state.Activity.DailyBuckets.Count==1 && state.LongestRunningTurnSec==130,"parse work buckets and profile summary");
    var daily=UsageAnalytics.BuildBuckets(state,"day",new DateTime(2026,11,1));
    Check(daily.Count==7 && daily[0].Start==new DateTime(2026,10,26),"seven days ending today");
    Check(daily[6].Tokens==null && daily[6].TokenPartial,"missing token today is unknown");
    Check(daily[6].WorkSeconds==0 && !daily[6].WorkPartial,"no work on fully covered day is true zero");
    var weekly=UsageAnalytics.BuildBuckets(state,"week",new DateTime(2026,11,1));
    Check(weekly.Count==4 && weekly[3].Start==new DateTime(2026,10,26),"weeks begin Monday including Sunday boundary");
    Check(weekly[3].Tokens==3000 && weekly[3].TokenPartial && weekly[3].Tasks==2,"partial period sums only actual records");
    var monthly=UsageAnalytics.BuildBuckets(state,"month",new DateTime(2026,11,1));
    Check(monthly.Count==6 && monthly[0].Start==new DateTime(2026,6,1) && monthly[5].End==new DateTime(2026,11,1),"six calendar months clipped at today");
    Check(monthly[0].Tokens==null && monthly[0].WorkSeconds==null,"uncollected months stay unknown");
    var total=UsageAnalytics.Summarize(daily);
    Check(total.Tokens==3000 && total.WorkSeconds==90 && total.Tasks==2,"summary adds records with separate coverage");
    state.Activity.Status="partial";
    var incomplete=UsageAnalytics.BuildBuckets(state,"day",new DateTime(2026,11,1));
    Check(incomplete[6].WorkSeconds==null && incomplete[5].WorkSeconds==90 && incomplete[5].WorkPartial,"partial scan never invents zero days");
    Check(UsageAnalytics.BuildDaily(state,new DateTime(2026,11,1),new DateTime(2026,10,1)).Count==0,"reversed date range empty");
    Console.WriteLine("Native analytics: 12 checks PASS");
  }
}

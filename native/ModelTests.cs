using System;
using CodexQuota;
class ModelTests {
  static void Check(bool ok, string name) { if(!ok) throw new Exception(name); }
  static void Main() {
    var s=QuotaState.Parse("{\"ok\":true,\"updatedAt\":\"2026-11-01T14:40:00Z\",\"limits\":[{\"windows\":[{\"label\":\"本周额度\",\"usedPercent\":28,\"remainingPercent\":72,\"resetsAt\":1735689600},{\"label\":\"5 小时额度\",\"remainingPercent\":10,\"usedPercent\":90,\"resetsAt\":1735603200}],\"credits\":{\"balance\":\"0\"}}],\"resetCreditsCount\":0,\"usage\":{\"summary\":{\"lifetimeTokens\":1500000000},\"dailyUsageBuckets\":[{\"startDate\":\"2026-10-31\",\"tokens\":600000000}]}}");
    Check(s.Available && s.Windows.Count==2,"all windows are exposed");
    Check(s.Primary.Remaining==10,"badge uses lowest remaining available quota");
    Check(s.Windows[0].ResetAt==1735689600L,"reset keeps seconds precision");
    Check(s.Credits=="0" && s.ResetCredits==0,"zero credits is not missing");
    Check(s.LatestTokens==600000000L && s.LifetimeTokens==1500000000L,"large token counts are preserved");
    var n=QuotaState.Parse("{\"ok\":true,\"limits\":[{\"windows\":[{\"label\":\"额度\",\"remainingPercent\":null,\"usedPercent\":null}]}]}");
    Check(n.Windows[0].Remaining==null && n.ResetCredits==null,"unknown is not zero");
    var e=QuotaState.Parse("{\"ok\":false,\"error\":\"账户未登录\"}");
    Check(!e.Available && e.Error=="账户未登录","server error is displayed");
    var old=QuotaState.Parse("{\"ok\":true,\"stale\":true,\"error\":\"刷新失败\",\"limits\":[]}");
    Check(old.Stale && old.Error=="刷新失败","stale state is not marked live");
    Console.WriteLine("Native data: 8 checks PASS");
  }
}

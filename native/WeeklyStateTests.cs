using System;

namespace Kiven.WeeklyQuota
{
    internal static class WeeklyStateTests
    {
        private static int failures;
        private static int passed;
        private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-11T10:00:00Z");

        public static int Main()
        {
            Run("valid weekly percentage and Beijing reset", delegate {
                WeeklyState s = Parse("68", "1794623400");
                Equal(true, s.Available); Equal("68%", s.NumberText);
                Equal("11月14日 10:30 重置", s.DetailText);
            });
            Run("zero means exhausted, not unavailable", delegate {
                WeeklyState s = Parse("0", "1794623400");
                Equal(true, s.Available); Equal("0%", s.NumberText);
            });
            Run("100 remains a real percentage", delegate { Equal("100%", Parse("100", "1794623400").NumberText); });
            Run("fractional percentage keeps its precise value for drawing", delegate {
                WeeklyState s = Parse("67.6", "1794623400");
                Equal("68%", s.NumberText); Equal<double?>(67.6, s.RemainingPercent);
            });
            Run("positive fraction cannot look exhausted", delegate { Equal("<1%", Parse("0.3", "1794623400").NumberText); });
            Run("invalid or nonfinite percentages never show a number", delegate {
                foreach (string value in new string[] { "-1", "101", "null", "\"68\"", "true", "1e999", "NaN", "Infinity" })
                {
                    WeeklyState s = Parse(value, "1794623400");
                    Equal(false, s.Available); Equal("—", s.NumberText);
                }
            });
            Run("missing required fields never imply zero", delegate {
                foreach (string json in new string[] { "{}", "{\"available\":true}", "{\"available\":true,\"remainingPercent\":68}", "{\"available\":true,\"resetsAt\":1794623400}", "null", "[]", "broken" })
                { Equal("—", WeeklyState.FromJson(json, Now).NumberText); }
            });
            Run("expired and exact reset discard old percentage", delegate {
                Equal(false, Parse("68", "1794391199").Available);
                Equal(false, Parse("68", "1794391200").Available);
            });
            Run("reset must be a finite whole future Unix second", delegate {
                foreach (string value in new string[] { "null", "\"1794623400\"", "1794623400.5", "1e999", "999999999999999999999" })
                { Equal("—", Parse("68", value).NumberText); }
            });
            Run("Beijing reset crosses the UTC year boundary", delegate {
                Equal("1月1日 02:30 重置", Parse("68", "1798741800").DetailText);
            });
            Run("unavailable error cannot leak raw server diagnostics", delegate {
                WeeklyState s = WeeklyState.FromJson("{\"available\":false,\"remainingPercent\":0,\"resetsAt\":1794623400,\"error\":\"SECRET_PATH arbitrary diagnostics\"}", Now);
                Equal(false, s.Available); Equal("—", s.NumberText);
                Equal<double?>(null, s.RemainingPercent);
                Equal(false, s.DetailText.Contains("SECRET"));
            });
            Run("loading and readable errors stay distinct", delegate {
                Equal("正在读取…", WeeklyState.Loading.DetailText);
                Equal("读取超时，稍后重试", WeeklyState.Unavailable("timeout").DetailText);
            });
            Console.WriteLine("WeeklyStateTests: " + passed + " passed, " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        private static WeeklyState Parse(string percent, string reset)
        {
            return WeeklyState.FromJson("{\"available\":true,\"remainingPercent\":" + percent + ",\"resetsAt\":" + reset + ",\"error\":null,\"updatedAt\":\"2026-11-11T10:00:00.000Z\"}", Now);
        }
        private static void Equal<T>(T expected, T actual)
        {
            if (!object.Equals(expected, actual)) throw new Exception("expected <" + expected + ">; got <" + actual + ">");
        }
        private static void Run(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace Kiven.WeeklyQuota
{
    internal sealed class WeeklyState
    {
        public bool Available { get; private set; }
        public double? RemainingPercent { get; private set; }
        public string NumberText { get; private set; }
        public string DetailText { get; private set; }
        public DateTimeOffset? ResetsAt { get; private set; }
        public static WeeklyState Loading { get { return new WeeklyState { NumberText = "—", DetailText = "正在读取…" }; } }
        public static WeeklyState Unavailable(string error)
        {
            string detail = "暂时无法读取，稍后重试";
            string code = (error ?? "").ToLowerInvariant();
            if (code.Contains("timeout") || code.Contains("超时")) detail = "读取超时，稍后重试";
            else if (code.Contains("auth") || code.Contains("login") || code.Contains("登录")) detail = "请先登录 Codex";
            else if (code.Contains("reset_expired") || code.Contains("重置")) detail = "额度重置中，稍后重试";
            else if (code.Contains("no_weekly") || code.Contains("weekly_unavailable") || code.Contains("weekly_missing") || code.Contains("周额度")) detail = "暂未提供周额度";
            else if (code.Contains("node_missing")) detail = "未找到 Node 运行环境";
            else if (code.Contains("reader_missing")) detail = "读取文件缺失";
            return new WeeklyState { Available = false, NumberText = "—", DetailText = detail };
        }

        public static WeeklyState FromJson(string json, DateTimeOffset now)
        {
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 16 };
                Dictionary<string, object> data = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (data == null) return Unavailable("invalid_data");
                object available;
                if (!data.TryGetValue("available", out available) || !(available is bool) || !(bool)available)
                {
                    object error;
                    return Unavailable(data.TryGetValue("error", out error) ? error as string : null);
                }
                double percentage, seconds;
                object rawPercent, rawReset;
                if (!data.TryGetValue("remainingPercent", out rawPercent) || !TryNumber(rawPercent, out percentage) || percentage < 0 || percentage > 100 ||
                    !data.TryGetValue("resetsAt", out rawReset) || !TryNumber(rawReset, out seconds) || seconds != Math.Truncate(seconds) || seconds < 0 || seconds > 253402300799D)
                    return Unavailable("invalid_data");
                DateTimeOffset reset = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds);
                if (reset <= now) return Unavailable("reset_expired");
                DateTimeOffset beijing = reset.ToOffset(TimeSpan.FromHours(8));
                string percentText = percentage > 0 && percentage < 0.5 ? "<1" : Math.Round(percentage, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
                return new WeeklyState {
                    Available = true,
                    RemainingPercent = percentage,
                    ResetsAt = reset,
                    NumberText = percentText + "%",
                    DetailText = beijing.ToString("M月d日 HH:mm", CultureInfo.InvariantCulture) + " 重置"
                };
            }
            catch (Exception error)
            {
                if (error is OutOfMemoryException || error is StackOverflowException) throw;
                return Unavailable("invalid_data");
            }
        }

        private static bool TryNumber(object value, out double number)
        {
            number = 0;
            if (!(value is int || value is long || value is double || value is decimal || value is float)) return false;
            number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return !Double.IsNaN(number) && !Double.IsInfinity(number);
        }
    }
}

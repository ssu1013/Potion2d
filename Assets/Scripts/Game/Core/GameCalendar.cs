namespace PotionFrontier.Game
{
    /// <summary>
    /// ゲームの中の暦（日付のアイキャッチに出す）。1日目が GameBalance の「はじまりの日」で、
    /// 調合机に向かうたび（はじめから・つづきから）に1日進む。1年は 365 日で、ひと回りすると同じ月日に戻る。
    /// </summary>
    public static class GameCalendar
    {
        private static readonly int[] DaysInMonth = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

        /// <summary>day 日目（1から）の月と日</summary>
        public static void MonthDay(int day, int startMonth, int startDay, out int month, out int dayOfMonth)
        {
            int m = System.Math.Min(12, System.Math.Max(1, startMonth)) - 1;
            int d = System.Math.Min(DaysInMonth[m], System.Math.Max(1, startDay)) - 1;
            int offset = d;
            for (int i = 0; i < m; i++) offset += DaysInMonth[i];
            int n = (offset + System.Math.Max(1, day) - 1) % 365;
            month = 0;
            while (n >= DaysInMonth[month])
            {
                n -= DaysInMonth[month];
                month++;
            }
            month += 1;
            dayOfMonth = n + 1;
        }

        /// <summary>「4月12日」</summary>
        public static string Format(int day, GameBalance balance)
        {
            int startMonth = balance != null ? balance.startMonth : 4;
            int startDay = balance != null ? balance.startDay : 1;
            MonthDay(day, startMonth, startDay, out int month, out int dayOfMonth);
            return $"{month}月{dayOfMonth}日";
        }
    }
}

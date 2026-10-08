namespace TradingAlgo.Services;

public static class BarAggregator
{
    public static TimeZoneInfo GetEt()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
    }

    // 1 dakikalık mumlardan, sadece normal seansa (ET 09:30-16:00) ait,
    // tamamlanmış 5 dakikalık mumlar üretir.
    public static List<Bar> RegularSession5Min(IEnumerable<Bar> oneMin, DateTimeOffset now)
    {
        var et = GetEt();
        long step = TimeSpan.FromMinutes(5).Ticks;
        var open = new TimeSpan(9, 30, 0);
        var close = new TimeSpan(16, 0, 0);

        var groups = oneMin
            .Select(b => new { Bar = b, Local = TimeZoneInfo.ConvertTime(b.Time, et) })
            .Where(x => x.Local.TimeOfDay >= open && x.Local.TimeOfDay < close)
            .GroupBy(x => x.Bar.Time.UtcTicks / step)
            .OrderBy(g => g.Key);

        var result = new List<Bar>();
        foreach (var g in groups)
        {
            var list = g.OrderBy(x => x.Bar.Time).ToList();
            var start = new DateTimeOffset(g.Key * step, TimeSpan.Zero);

            if (start.AddMinutes(5) > now) continue; // henüz tamamlanmadı

            result.Add(new Bar(
                start,
                list.First().Bar.Open,
                list.Max(x => x.Bar.High),
                list.Min(x => x.Bar.Low),
                list.Last().Bar.Close,
                list.Sum(x => x.Bar.Volume)));
        }
        return result;
    }
}
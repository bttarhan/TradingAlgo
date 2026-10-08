namespace TradingAlgo.Services;

public static class Indicators
{
    public static decimal?[] Ema(IReadOnlyList<decimal> v, int period)
    {
        var r = new decimal?[v.Count];
        if (v.Count < period) return r;

        decimal k = 2m / (period + 1);
        decimal sum = 0;
        for (int i = 0; i < period; i++) sum += v[i];

        decimal ema = sum / period;
        r[period - 1] = ema;

        for (int i = period; i < v.Count; i++)
        {
            ema = v[i] * k + ema * (1 - k);
            r[i] = ema;
        }
        return r;
    }

    public static decimal?[] Rsi(IReadOnlyList<decimal> c, int period)
    {
        var r = new decimal?[c.Count];
        if (c.Count <= period) return r;

        decimal gain = 0, loss = 0;
        for (int i = 1; i <= period; i++)
        {
            var d = c[i] - c[i - 1];
            if (d > 0) gain += d; else loss -= d;
        }
        gain /= period;
        loss /= period;
        r[period] = RsiValue(gain, loss);

        for (int i = period + 1; i < c.Count; i++)
        {
            var d = c[i] - c[i - 1];
            var g = d > 0 ? d : 0;
            var l = d < 0 ? -d : 0;
            gain = (gain * (period - 1) + g) / period;
            loss = (loss * (period - 1) + l) / period;
            r[i] = RsiValue(gain, loss);
        }
        return r;
    }

    private static decimal RsiValue(decimal gain, decimal loss)
        => loss == 0 ? 100m : 100m - 100m / (1 + gain / loss);

    public static decimal?[] Atr(IReadOnlyList<Bar> b, int period)
    {
        var r = new decimal?[b.Count];
        if (b.Count <= period) return r;

        var tr = new decimal[b.Count];
        for (int i = 1; i < b.Count; i++)
        {
            var prevClose = b[i - 1].Close;
            tr[i] = Math.Max(b[i].High - b[i].Low,
                    Math.Max(Math.Abs(b[i].High - prevClose),
                             Math.Abs(b[i].Low - prevClose)));
        }

        decimal atr = 0;
        for (int i = 1; i <= period; i++) atr += tr[i];
        atr /= period;
        r[period] = atr;

        for (int i = period + 1; i < b.Count; i++)
        {
            atr = (atr * (period - 1) + tr[i]) / period;
            r[i] = atr;
        }
        return r;
    }
}
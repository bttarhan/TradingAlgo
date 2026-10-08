using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using TradingAlgo.Models;
using TradingAlgo.Services;

namespace TradingAlgo.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BacktestController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<BacktestController> _logger;

    public BacktestController(IConfiguration config, ILogger<BacktestController> logger)
    {
        _config = config;
        _logger = logger;
    }

    private class TradeRec
    {
        public DateTimeOffset EntryTime { get; set; }
        public DateTimeOffset ExitTime { get; set; }
        public decimal Entry { get; set; }
        public decimal Stop { get; set; }
        public int Shares { get; set; }
        public decimal Pnl { get; set; }
        public decimal RMultiple { get; set; }
        public string Reason { get; set; } = "";
    }

    private static string Tr(DateTimeOffset t) =>
        t.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd HH:mm");

    private static bool TryDate(string? s, out DateTime? d)
    {
        d = null;
        if (string.IsNullOrWhiteSpace(s)) return true;
        if (DateTime.TryParseExact(s.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var x))
        {
            d = x.Date;
            return true;
        }
        return false;
    }

    [HttpGet("{symbol}")]
    public async Task<IActionResult> Get(string symbol,
        [FromQuery] int days = 180,
        [FromQuery] decimal slippage = 0.02m,
        [FromQuery] decimal atrMult = 1.5m,
        [FromQuery] string? start = null,
        [FromQuery] string? end = null,
        [FromQuery] bool regime = false,
        [FromQuery] int regimeDays = 20,
        [FromQuery] int skipFirstMin = 0)
    {
        var s = symbol.Trim().ToUpper();
        var allowed = _config.GetSection("Webhook:AllowedSymbols").Get<string[]>() ?? Array.Empty<string>();
        if (!allowed.Contains(s))
            return BadRequest(new { error = "Sembol izinli değil" });

        if (!TryDate(start, out var startDate) || !TryDate(end, out var endDate))
            return BadRequest(new { error = "Tarih formatı yyyy-MM-dd olmalı (örnek: 2026-08-01)" });

        days = Math.Clamp(days, 20, 365);
        slippage = Math.Clamp(slippage, 0m, 1m);
        atrMult = Math.Clamp(atrMult, 0.5m, 5m);
        regimeDays = Math.Clamp(regimeDays, 5, 60);
        skipFirstMin = Math.Clamp(skipFirstMin, 0, 120);

        try
        {
            var client = new AlpacaClient(_config);
            var oneMin = await client.GetBarsRangeAsync(s, DateTime.UtcNow.AddDays(-days), "iex");
            var bars = BarAggregator.RegularSession5Min(oneMin, DateTimeOffset.UtcNow);

            if (bars.Count < 200)
                return BadRequest(new { error = "Yeterli veri yok", fiveMinBars = bars.Count });

            var et = BarAggregator.GetEt();

            var inRange = bars.Where(b =>
            {
                var d = TimeZoneInfo.ConvertTime(b.Time, et).Date;
                return (!startDate.HasValue || d >= startDate.Value) &&
                       (!endDate.HasValue || d <= endDate.Value);
            }).ToList();

            if (inRange.Count == 0)
                return BadRequest(new { error = "Seçilen tarih aralığında veri yok" });

            var tradingDays = inRange
                .Select(b => TimeZoneInfo.ConvertTime(b.Time, et).Date)
                .Distinct().Count();

            var trades = Simulate(bars, slippage, atrMult, startDate, endDate, regime, regimeDays, skipFirstMin);

            var n = trades.Count;
            var total = trades.Sum(t => t.Pnl);
            var wins = trades.Where(t => t.Pnl > 0).ToList();
            var losses = trades.Where(t => t.Pnl <= 0).ToList();
            var grossProfit = wins.Sum(t => t.Pnl);
            var grossLoss = -losses.Sum(t => t.Pnl);
            decimal? pf = grossLoss > 0 ? Math.Round(grossProfit / grossLoss, 2) : null;

            decimal cum = 0, peak = 0, maxDd = 0;
            foreach (var t in trades)
            {
                cum += t.Pnl;
                if (cum > peak) peak = cum;
                if (peak - cum > maxDd) maxDd = peak - cum;
            }

            var capital = _config.GetValue<decimal>("Risk:DailyCapital");
            var first = inRange.First().Close;
            var last = inRange.Last().Close;

            var perDay = trades
                .GroupBy(t => Tr(t.EntryTime).Substring(0, 10))
                .Select(g => g.Count())
                .ToList();

            return Ok(new
            {
                symbol = s,
                period = new
                {
                    fromTr = Tr(inRange.First().Time),
                    toTr = Tr(inRange.Last().Time),
                    tradingDays,
                    fiveMinBars = inRange.Count
                },
                settings = new
                {
                    days,
                    slippagePerShare = slippage,
                    atrStopMultiplier = atrMult,
                    start,
                    end,
                    regime,
                    regimeDays,
                    skipFirstMin
                },
                summary = new
                {
                    trades = n,
                    winRatePercent = n > 0 ? Math.Round(100m * wins.Count / n, 1) : 0,
                    totalPnl = Math.Round(total, 2),
                    profitFactor = pf,
                    avgWin = wins.Count > 0 ? Math.Round(wins.Average(t => t.Pnl), 2) : 0,
                    avgLoss = losses.Count > 0 ? Math.Round(losses.Average(t => t.Pnl), 2) : 0,
                    expectancyPerTrade = n > 0 ? Math.Round(total / n, 2) : 0,
                    avgRMultiple = n > 0 ? Math.Round(trades.Average(t => t.RMultiple), 2) : 0,
                    maxDrawdown = Math.Round(maxDd, 2),
                    maxDrawdownPercentOfCapital = capital > 0 ? Math.Round(100m * maxDd / capital, 1) : 0
                },
                tradeFrequency = new
                {
                    avgTradesPerTradingDay = Math.Round((decimal)n / tradingDays, 2),
                    daysWithTrades = perDay.Count,
                    maxTradesInOneDay = perDay.Count > 0 ? perDay.Max() : 0
                },
                buyAndHoldPercent = Math.Round((last / first - 1m) * 100m, 1),
                byEntryHourTr = trades
                    .GroupBy(t => Tr(t.EntryTime).Substring(11, 2))
                    .Select(g => new
                    {
                        hourTr = g.Key + ":00",
                        trades = g.Count(),
                        pnl = Math.Round(g.Sum(t => t.Pnl), 2)
                    })
                    .OrderBy(x => x.hourTr),
                exitReasons = trades.GroupBy(t => t.Reason)
                    .Select(g => new { reason = g.Key, count = g.Count(), pnl = Math.Round(g.Sum(t => t.Pnl), 2) })
                    .OrderByDescending(x => x.count),
                monthly = trades.GroupBy(t => Tr(t.EntryTime).Substring(0, 7))
                    .Select(g => new { month = g.Key, trades = g.Count(), pnl = Math.Round(g.Sum(t => t.Pnl), 2) })
                    .OrderBy(x => x.month)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("Backtest başarısız: {Message}", ex.Message);
            return StatusCode(502, new { error = "Backtest çalışmadı", detail = ex.Message });
        }
    }

    private List<TradeRec> Simulate(List<Bar> bars, decimal slip, decimal atrMult,
        DateTime? startDate, DateTime? endDate, bool regime, int regimeDays, int skipFirstMin)
    {
        var capital = _config.GetValue<decimal>("Risk:DailyCapital");
        var fixedRisk = _config.GetValue<decimal>("Risk:RiskAmount");
        var riskPercent = _config.GetValue<decimal>("Risk:RiskPercent");
        var maxPos = _config.GetValue<decimal>("Risk:MaxPositionPercent");

        var closes = bars.Select(b => b.Close).ToList();
        var ema20 = Indicators.Ema(closes, 20);
        var ema50 = Indicators.Ema(closes, 50);
        var rsi = Indicators.Rsi(closes, 14);
        var atr = Indicators.Atr(bars, 14);

        var et = BarAggregator.GetEt();
        var day = new DateTime[bars.Count];
        var tod = new TimeSpan[bars.Count];
        for (int k = 0; k < bars.Count; k++)
        {
            var l = TimeZoneInfo.ConvertTime(bars[k].Time, et);
            day[k] = l.Date;
            tod[k] = l.TimeOfDay;
        }

        // Günlük rejim: önceki günün kapanışı, önceki günün günlük EMA'sının üstünde mi?
        var regimeOk = new bool[bars.Count];
        if (regime)
        {
            var dayList = new List<DateTime>();
            var dayClose = new List<decimal>();
            for (int k = 0; k < bars.Count; k++)
            {
                if (dayList.Count == 0 || dayList[dayList.Count - 1] != day[k])
                {
                    dayList.Add(day[k]);
                    dayClose.Add(bars[k].Close);
                }
                else
                {
                    dayClose[dayClose.Count - 1] = bars[k].Close;
                }
            }

            var dEma = Indicators.Ema(dayClose, regimeDays);
            var okByDay = new Dictionary<DateTime, bool>();
            for (int d = 1; d < dayList.Count; d++)
                okByDay[dayList[d]] = dEma[d - 1] != null && dayClose[d - 1] > dEma[d - 1]!.Value;

            for (int k = 0; k < bars.Count; k++)
                regimeOk[k] = okByDay.TryGetValue(day[k], out var v) && v;
        }

        var lastEntry = new TimeSpan(15, 30, 0);
        var openSkip = new TimeSpan(9, 30, 0).Add(TimeSpan.FromMinutes(skipFirstMin));
        var result = new List<TradeRec>();
        int i = 50;

        while (i < bars.Count - 1)
        {
            if (startDate.HasValue && day[i] < startDate.Value) { i++; continue; }
            if (endDate.HasValue && day[i] > endDate.Value) break;

            if (ema20[i] == null || ema50[i] == null || rsi[i] == null || atr[i] == null)
            {
                i++;
                continue;
            }

            if (regime && !regimeOk[i]) { i++; continue; }

            var b = bars[i];
            bool trendUp = b.Close > ema20[i]!.Value && ema20[i]!.Value > ema50[i]!.Value;
            bool rsiOk = rsi[i]!.Value >= 50 && rsi[i]!.Value <= 70;
            if (!(trendUp && rsiOk)) { i++; continue; }

            int e = i + 1;
            if (day[e] != day[i] || tod[e] >= lastEntry || tod[e] < openSkip ||
                (bars[e].Time - bars[i].Time) > TimeSpan.FromMinutes(10))
            {
                i++;
                continue;
            }

            var entry = Math.Round(bars[e].Open + slip, 3);
            var stop = Math.Round(entry - atrMult * atr[i]!.Value, 2);
            var risk = entry - stop;
            if (risk <= 0) { i++; continue; }

            var shares = RiskCalculator.CalculateShares(capital, riskPercent, fixedRisk, maxPos, entry, stop);
            if (shares <= 0) { i++; continue; }

            var tp1 = Math.Round(entry + risk, 2);
            var tp2 = Math.Round(entry + 2 * risk, 2);
            int tp1Qty = shares >= 2 ? shares / 2 : shares;

            decimal pnl = 0;
            int rem = shares;
            bool tp1Done = false;
            decimal curStop = stop;
            string finalReason = "";
            int j = e;

            for (; j < bars.Count; j++)
            {
                var bj = bars[j];
                bool lastOfDay = j == bars.Count - 1 || day[j + 1] != day[j];

                if (bj.Open <= curStop)
                {
                    pnl += rem * ((bj.Open - slip) - entry);
                    finalReason = tp1Done ? "Başabaş stop (gap)" : "Stop (gap)";
                    rem = 0;
                    break;
                }
                if (bj.Low <= curStop)
                {
                    pnl += rem * ((curStop - slip) - entry);
                    finalReason = tp1Done ? "Başabaş stop" : "Stop";
                    rem = 0;
                    break;
                }

                if (!tp1Done && bj.High >= tp1)
                {
                    pnl += tp1Qty * (tp1 - entry);
                    rem -= tp1Qty;
                    tp1Done = true;
                    curStop = entry;
                    if (rem == 0) { finalReason = "TP1"; break; }
                }
                else if (tp1Done && bj.High >= tp2)
                {
                    pnl += rem * (tp2 - entry);
                    rem = 0;
                    finalReason = "TP2";
                    break;
                }

                if (lastOfDay)
                {
                    pnl += rem * ((bj.Close - slip) - entry);
                    rem = 0;
                    finalReason = "Gün sonu";
                    break;
                }
            }

            int exitIdx = Math.Min(j, bars.Count - 1);
            var reason = (tp1Done && finalReason != "TP1" ? "TP1 + " : "") + finalReason;

            result.Add(new TradeRec
            {
                EntryTime = bars[e].Time,
                ExitTime = bars[exitIdx].Time,
                Entry = entry,
                Stop = stop,
                Shares = shares,
                Pnl = pnl,
                RMultiple = pnl / (shares * risk),
                Reason = reason
            });

            i = exitIdx;
        }

        return result;
    }
}
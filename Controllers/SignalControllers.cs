using Microsoft.AspNetCore.Mvc;
using TradingAlgo.Models;
using TradingAlgo.Services;

namespace TradingAlgo.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SignalController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<SignalController> _logger;

    public SignalController(IConfiguration config, ILogger<SignalController> logger)
    {
        _config = config;
        _logger = logger;
    }

    public class Eval
    {
        public string Symbol { get; set; } = "";
        public string Status { get; set; } = "";
        public List<string> Reasons { get; set; } = new();
        public string? BarStartTr { get; set; }
        public decimal? Close { get; set; }
        public decimal? Ema20 { get; set; }
        public decimal? Ema50 { get; set; }
        public decimal? Rsi { get; set; }
        public decimal? Atr { get; set; }
        public decimal? Entry { get; set; }
        public decimal? Stop { get; set; }
        public decimal? Tp1 { get; set; }
        public decimal? Tp2 { get; set; }
        public int? Shares { get; set; }
        public decimal? RiskAmount { get; set; }
        public decimal? PositionValue { get; set; }
    }

    private static bool IsRegularOpen(DateTimeOffset now)
    {
        var et = TimeZoneInfo.ConvertTime(now, BarAggregator.GetEt());
        if (et.DayOfWeek == DayOfWeek.Saturday || et.DayOfWeek == DayOfWeek.Sunday) return false;
        var t = et.TimeOfDay;
        return t >= new TimeSpan(9, 30, 0) && t < new TimeSpan(16, 0, 0);
    }

        private async Task<Eval> EvaluateAsync(string s, DateTimeOffset now, bool open, decimal capital, decimal fixedRisk)
    {
        var ev = new Eval { Symbol = s };

        var oneMin = await new AlpacaClient(_config).GetBarsAsync(s, 3000, "iex");
        var bars = BarAggregator.RegularSession5Min(oneMin, now);

        if (bars.Count < 60)
        {
            ev.Status = "VERİ YETERSİZ";
            ev.Reasons.Add($"Yeterli 5 dakikalık mum yok ({bars.Count})");
            return ev;
        }

        var closes = bars.Select(b => b.Close).ToList();
        var last = bars.Last();
        var ema20 = Indicators.Ema(closes, 20).Last()!.Value;
        var ema50 = Indicators.Ema(closes, 50).Last()!.Value;
        var rsi = Indicators.Rsi(closes, 14).Last()!.Value;
        var atr = Indicators.Atr(bars, 14).Last()!.Value;

        ev.BarStartTr = last.Time.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd HH:mm");
        ev.Close = last.Close;
        ev.Ema20 = Math.Round(ema20, 3);
        ev.Ema50 = Math.Round(ema50, 3);
        ev.Rsi = Math.Round(rsi, 2);
        ev.Atr = Math.Round(atr, 3);

        var barEnd = last.Time.AddMinutes(5);
        if (open && now - barEnd > TimeSpan.FromMinutes(10))
        {
            ev.Status = "VERİ ESKİ";
            ev.Reasons.Add("Son tamamlanmış mum 10 dakikadan eski");
            return ev;
        }

        bool trendUp = last.Close > ema20 && ema20 > ema50;

        if (!trendUp)
        {
            ev.Status = "BEKLE";
            ev.Reasons.Add("Trend yukarı değil (kapanış > EMA20 > EMA50 sağlanmıyor)");
            return ev;
        }

        if (rsi < 50 || rsi > 70)
        {
            ev.Status = "İZLE";
            ev.Reasons.Add(rsi > 70
                ? "Trend yukarı ama RSI 70 üstünde, kovalamıyoruz"
                : "Trend yukarı ama RSI 50 altında, momentum zayıf");
            return ev;
        }

        var entry = Math.Round(last.Close, 2);
        var stop = Math.Round(entry - 1.5m * atr, 2);
        var risk = entry - stop;


        var riskPercent = _config.GetValue<decimal>("Risk:RiskPercent");
        var maxPos = _config.GetValue<decimal>("Risk:MaxPositionPercent");

        var shares = RiskCalculator.CalculateShares(capital, riskPercent, fixedRisk, maxPos, entry, stop);

        if (risk <= 0 || shares <= 0)
        {
            ev.Status = "ATLA";
            ev.Reasons.Add("Pozisyon büyüklüğü hesaplanamadı");
            return ev;
        }

        ev.Status = "LONG";
        ev.Reasons.Add("Kapanış > EMA20 > EMA50 ve RSI 50-70 arasında");
        ev.Entry = entry;
        ev.Stop = stop;
        ev.Tp1 = Math.Round(entry + risk, 2);
        ev.Tp2 = Math.Round(entry + 2 * risk, 2);
        ev.Shares = shares;
        ev.RiskAmount = Math.Round(shares * risk, 2);
        ev.PositionValue = Math.Round(shares * entry, 2);
        return ev;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] decimal? capital, [FromQuery] decimal? risk)
    {
        var allowed = _config.GetSection("Webhook:AllowedSymbols").Get<string[]>() ?? Array.Empty<string>();
        if (!allowed.Contains("SOXL") || !allowed.Contains("SOXS"))
            return BadRequest(new { error = "SOXL ve SOXS izinli semboller listesinde olmalı" });

        var cap = capital ?? _config.GetValue<decimal>("Risk:DailyCapital");
        var fixedRisk = risk ?? _config.GetValue<decimal>("Risk:RiskAmount");

        if (cap < 100 || cap > 10_000_000)
            return BadRequest(new { error = "capital 100 ile 10.000.000 arasında olmalı" });
        if (fixedRisk < 0 || fixedRisk > cap * 0.05m)
            return BadRequest(new { error = "risk, günlük tutarın %5'ini geçemez" });

        try
        {
            var now = DateTimeOffset.UtcNow;
            var open = IsRegularOpen(now);

            var soxl = await EvaluateAsync("SOXL", now, open, cap, fixedRisk);
            var soxs = await EvaluateAsync("SOXS", now, open, cap, fixedRisk);

            var longs = new[] { soxl, soxs }.Where(e => e.Status == "LONG").ToList();

            string advice;
            if (!open) advice = "SEANS KAPALI (değerlendirme bilgi amaçlı)";
            else if (longs.Count == 2) advice = "BEKLE: SOXL ve SOXS aynı anda AL veriyor, çelişki";
            else if (longs.Count == 1) advice = $"AL ADAYI: {longs[0].Symbol}";
            else advice = "BEKLE";

            return Ok(new
            {
                timeTr = now.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd HH:mm:ss"),
                marketOpen = open,
                capitalUsed = cap,
                fixedRiskUsed = fixedRisk,
                advice,
                soxl,
                soxs
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("Sinyal hesabı başarısız: {Message}", ex.Message);
            return StatusCode(502, new { error = "Hesaplanamadı", detail = ex.Message });
        }
    }
}
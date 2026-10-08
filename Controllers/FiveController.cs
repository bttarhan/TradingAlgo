using Microsoft.AspNetCore.Mvc;
using TradingAlgo.Services;

namespace TradingAlgo.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FiveController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<FiveController> _logger;

    public FiveController(IConfiguration config, ILogger<FiveController> logger)
    {
        _config = config;
        _logger = logger;
    }

    private static decimal? R(decimal? x) => x is null ? null : Math.Round(x.Value, 3);

    [HttpGet("{symbol}")]
    public async Task<IActionResult> Get(string symbol)
    {
        var s = symbol.Trim().ToUpper();
        var allowed = _config.GetSection("Webhook:AllowedSymbols").Get<string[]>() ?? Array.Empty<string>();
        if (!allowed.Contains(s))
            return BadRequest(new { error = "Sembol izinli değil" });

        try
        {
            var oneMin = await new AlpacaClient(_config).GetBarsAsync(s, 3000, "iex");
            var bars = BarAggregator.RegularSession5Min(oneMin, DateTimeOffset.UtcNow);

            if (bars.Count < 60)
                return BadRequest(new { error = "Yeterli 5 dakikalık mum yok", barsUsed = bars.Count });

            var closes = bars.Select(b => b.Close).ToList();
            var last = bars.Last();

            // VWAP: sadece son işlem gününün normal seansı
            var et = BarAggregator.GetEt();
            var lastDay = TimeZoneInfo.ConvertTime(last.Time, et).Date;
            var day = bars.Where(b => TimeZoneInfo.ConvertTime(b.Time, et).Date == lastDay).ToList();
            var volSum = day.Sum(b => b.Volume);
            decimal? vwap = volSum > 0
                ? day.Sum(b => (b.High + b.Low + b.Close) / 3m * b.Volume) / volSum
                : null;

            return Ok(new
            {
                symbol = s,
                barsUsed = bars.Count,
                lastBarStartUtc = last.Time,
                lastBarStartTr = last.Time.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd HH:mm"),
                close = last.Close,
                ema20 = R(Indicators.Ema(closes, 20).Last()),
                ema50 = R(Indicators.Ema(closes, 50).Last()),
                rsi14 = R(Indicators.Rsi(closes, 14).Last()),
                atr14 = R(Indicators.Atr(bars, 14).Last()),
                vwap = R(vwap)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("5 dakikalık hesap başarısız: {Message}", ex.Message);
            return StatusCode(502, new { error = "Hesaplanamadı", detail = ex.Message });
        }
    }
}
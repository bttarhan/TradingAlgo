using Microsoft.AspNetCore.Mvc;
using TradingAlgo.Services;

namespace TradingAlgo.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IndicatorsController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<IndicatorsController> _logger;

    public IndicatorsController(IConfiguration config, ILogger<IndicatorsController> logger)
    {
        _config = config;
        _logger = logger;
    }

    private static decimal? R(decimal? x) => x is null ? null : Math.Round(x.Value, 3);

    [HttpGet("{symbol}")]
    public async Task<IActionResult> Get(string symbol, [FromQuery] string feed = "iex", [FromQuery] int count = 200)
    {
        var feeds = new[] { "iex", "delayed_sip", "overnight", "boats" };
        feed = feed.Trim().ToLower();
        if (!feeds.Contains(feed))
            return BadRequest(new { error = "Geçersiz feed" });

        var s = symbol.Trim().ToUpper();
        var allowed = _config.GetSection("Webhook:AllowedSymbols").Get<string[]>() ?? Array.Empty<string>();
        if (!allowed.Contains(s))
            return BadRequest(new { error = "Sembol izinli değil" });

        try
        {
            var bars = await new AlpacaClient(_config).GetBarsAsync(s, Math.Clamp(count, 60, 1000), feed);
            if (bars.Count < 60)
                return BadRequest(new { error = "Yeterli mum yok", barsUsed = bars.Count });

            var closes = bars.Select(b => b.Close).ToList();

            return Ok(new
            {
                symbol = s,
                feed,
                barsUsed = bars.Count,
                lastBarTime = bars.Last().Time,
                close = bars.Last().Close,
                ema20 = R(Indicators.Ema(closes, 20).Last()),
                ema50 = R(Indicators.Ema(closes, 50).Last()),
                rsi14 = R(Indicators.Rsi(closes, 14).Last()),
                atr14 = R(Indicators.Atr(bars, 14).Last())
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("Gösterge hesabı başarısız ({Feed}): {Message}", feed, ex.Message);
            return StatusCode(502, new { feed, error = "Hesaplanamadı", detail = ex.Message });
        }
    }
}

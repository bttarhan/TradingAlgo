using Microsoft.AspNetCore.Mvc;
using TradingAlgo.Services;

namespace TradingAlgo.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SessionsController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(IConfiguration config, ILogger<SessionsController> logger)
    {
        _config = config;
        _logger = logger;
    }

    private static TimeZoneInfo GetEt()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
    }

    private static string SessionOf(TimeSpan t)
    {
        if (t >= new TimeSpan(4, 0, 0) && t < new TimeSpan(9, 30, 0)) return "pre-market";
        if (t >= new TimeSpan(9, 30, 0) && t < new TimeSpan(16, 0, 0)) return "regular";
        if (t >= new TimeSpan(16, 0, 0) && t < new TimeSpan(20, 0, 0)) return "after-hours";
        return "overnight";
    }

    [HttpGet("{symbol}")]
    public async Task<IActionResult> Get(string symbol, [FromQuery] string feed = "iex")
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
            var bars = await new AlpacaClient(_config).GetBarsAsync(s, 5000, feed);
            var et = GetEt();

            var groups = bars
                .Select(b =>
                {
                    var local = TimeZoneInfo.ConvertTime(b.Time, et);
                    var t = local.TimeOfDay;
                    var sess = SessionOf(t);
                    var d = local.Date;
                    if (sess == "overnight" && t < new TimeSpan(4, 0, 0)) d = d.AddDays(-1);
                    return new { Bar = b, Sess = sess, Day = d };
                })
                .GroupBy(x => new { x.Day, x.Sess })
                .Select(g => new
                {
                    etDate = g.Key.Day.ToString("yyyy-MM-dd"),
                    session = g.Key.Sess,
                    bars = g.Count(),
                    firstUtc = g.Min(x => x.Bar.Time),
                    lastUtc = g.Max(x => x.Bar.Time),
                    volume = g.Sum(x => x.Bar.Volume)
                })
                .OrderBy(x => x.firstUtc)
                .ToList();

            return Ok(new { feed, symbol = s, totalBars = bars.Count, sessions = groups });
        }
        catch (Exception ex)
        {
            _logger.LogError("Seans raporu başarısız ({Feed}): {Message}", feed, ex.Message);
            return StatusCode(502, new { feed, error = "Alınamadı", detail = ex.Message });
        }
    }
}
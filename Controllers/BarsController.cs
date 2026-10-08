using Microsoft.AspNetCore.Mvc;
using TradingAlgo.Services;

namespace TradingAlgo.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BarsController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<BarsController> _logger;

    public BarsController(IConfiguration config, ILogger<BarsController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet("{symbol}")]
    public async Task<IActionResult> Get(string symbol, [FromQuery] string feed = "iex", [FromQuery] int count = 100)
    {
        var feeds = new[] { "iex", "delayed_sip", "overnight", "boats" };
        feed = feed.Trim().ToLower();
        if (!feeds.Contains(feed))
            return BadRequest(new { error = "Geçersiz feed" });

        count = Math.Clamp(count, 1, 1000);

        var s = symbol.Trim().ToUpper();
        var allowed = _config.GetSection("Webhook:AllowedSymbols").Get<string[]>() ?? Array.Empty<string>();
        if (!allowed.Contains(s))
            return BadRequest(new { error = "Sembol izinli değil" });

        try
        {
            var bars = await new AlpacaClient(_config).GetBarsAsync(s, count, feed);
            return Ok(new
            {
                feed,
                symbol = s,
                count = bars.Count,
                first = bars.FirstOrDefault()?.Time,
                last = bars.LastOrDefault()?.Time,
                bars
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("Mumlar alınamadı ({Feed}): {Message}", feed, ex.Message);
            return StatusCode(502, new { feed, error = "Mumlar alınamadı", detail = ex.Message });
        }
    }
}

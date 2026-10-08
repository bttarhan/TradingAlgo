using Microsoft.AspNetCore.Mvc;
using TradingAlgo.Services;

namespace TradingAlgo.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PriceController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<PriceController> _logger;

    public PriceController(IConfiguration config, ILogger<PriceController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet("{symbol}")]
    public async Task<IActionResult> Get(string symbol)
    {
        var s = symbol.Trim().ToUpper();
        var allowed = _config.GetSection("Webhook:AllowedSymbols").Get<string[]>() ?? Array.Empty<string>();

        if (!allowed.Contains(s))
            return BadRequest(new { error = "Sembol izinli değil" });

        try
        {
            var trade = await new AlpacaClient(_config).GetLastTradeAsync(s);
            return Ok(trade);
        }
        catch (Exception ex)
        {
            _logger.LogError("Fiyat alınamadı: {Message}", ex.Message);
            return StatusCode(502, new { error = "Fiyat alınamadı", detail = ex.Message });
        }
    }
}

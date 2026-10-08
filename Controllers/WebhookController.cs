using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using TradingAlgo.Models;

namespace TradingAlgo.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WebhookController : ControllerBase
{
    private readonly ILogger<WebhookController> _logger;
    private readonly IConfiguration _config;

    public WebhookController(ILogger<WebhookController> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    [HttpPost]
    public IActionResult Receive([FromBody] TradingSignal signal)
    {
        var expectedSecret = _config["Webhook:Secret"] ?? "";

        if (string.IsNullOrEmpty(expectedSecret) || !SecretMatches(signal.Secret, expectedSecret))
        {
            _logger.LogWarning("Yetkisiz webhook isteği reddedildi.");
            return Unauthorized(new { error = "Yetkisiz" });
        }

        var action = signal.Action.Trim().ToLower();

        if (string.IsNullOrWhiteSpace(signal.Symbol) ||
            (action != "buy" && action != "sell") ||
            signal.Price <= 0)
        {
            _logger.LogWarning("Geçersiz sinyal: {Symbol} {Action} {Price}",
                signal.Symbol, signal.Action, signal.Price);
            return BadRequest(new { error = "Geçersiz sinyal" });
        }

        var allowedSymbols = _config.GetSection("Webhook:AllowedSymbols").Get<string[]>() ?? Array.Empty<string>();
        var symbol = signal.Symbol.Trim().ToUpper();

        if (!allowedSymbols.Contains(symbol))
        {
            _logger.LogWarning("İzin verilmeyen sembol reddedildi: {Symbol}", symbol);
            return BadRequest(new { error = "Sembol izinli değil" });
        }

        if (action == "sell")
        {
            _logger.LogInformation("SİNYAL: SELL {Symbol} @ {Price}", symbol, signal.Price);
            return Ok(new { received = true, symbol, action, price = signal.Price });
        }

        // BUY: stop zorunlu ve giriş fiyatının altında olmalı
        if (signal.Stop <= 0 || signal.Stop >= signal.Price)
        {
            _logger.LogWarning("BUY sinyalinde geçersiz stop: {Stop}", signal.Stop);
            return BadRequest(new { error = "Stop, giriş fiyatının altında ve sıfırdan büyük olmalı" });
        }

               var capital = _config.GetValue<decimal>("Risk:DailyCapital");
        var fixedRisk = _config.GetValue<decimal>("Risk:RiskAmount");
        var riskPercent = _config.GetValue<decimal>("Risk:RiskPercent");
        var maxPositionPercent = _config.GetValue<decimal>("Risk:MaxPositionPercent");

        var shares = RiskCalculator.CalculateShares(
            capital, riskPercent, fixedRisk, maxPositionPercent, signal.Price, signal.Stop);

        if (shares <= 0)
        {
            _logger.LogWarning("Pozisyon büyüklüğü 0 çıktı: {Symbol}", symbol);
            return BadRequest(new { error = "Pozisyon büyüklüğü hesaplanamadı" });
        }

        var riskAmount = shares * (signal.Price - signal.Stop);
        var positionValue = shares * signal.Price;

        _logger.LogInformation(
            "SİNYAL: BUY {Symbol} @ {Price} | Stop {Stop} | Adet {Shares} | Risk ${Risk} | Pozisyon ${Value}",
            symbol, signal.Price, signal.Stop, shares, riskAmount, positionValue);

        return Ok(new
        {
            received = true,
            symbol,
            action,
            price = signal.Price,
            stop = signal.Stop,
            shares,
            riskAmount,
            positionValue
        });
    }

    private static bool SecretMatches(string provided, string expected)
    {
        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
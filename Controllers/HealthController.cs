using Microsoft.AspNetCore.Mvc;

namespace TradingAlgo.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "ok",
            app = "TradingAlgo",
            time = DateTime.UtcNow
        });
    }
}
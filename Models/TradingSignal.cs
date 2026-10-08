namespace TradingAlgo.Models;

public class TradingSignal
{
    public string Symbol { get; set; } = "";
    public string Action { get; set; } = "";
    public decimal Price { get; set; }
    public decimal Stop { get; set; }
    public string Secret { get; set; } = "";
}
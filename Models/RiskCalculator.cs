namespace TradingAlgo.Models;

public static class RiskCalculator
{
    public static int CalculateShares(
        decimal capital, decimal riskPercent, decimal fixedRiskAmount,
        decimal maxPositionPercent, decimal entry, decimal stop)
    {
        var riskPerShare = entry - stop;
        if (riskPerShare <= 0) return 0;

        var maxRisk = fixedRiskAmount > 0
            ? fixedRiskAmount
            : capital * riskPercent / 100m;

        var sharesByRisk = (int)Math.Floor(maxRisk / riskPerShare);

        var maxPositionValue = capital * maxPositionPercent / 100m;
        var sharesByCap = (int)Math.Floor(maxPositionValue / entry);

        return Math.Max(0, Math.Min(sharesByRisk, sharesByCap));
    }
}
using System.Text.Json;

namespace TradingAlgo.Services;

public record LastTrade(string Symbol, decimal Price, DateTimeOffset Time);
public record Bar(DateTimeOffset Time, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

public class AlpacaClient
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://data.alpaca.markets/"),
        Timeout = TimeSpan.FromSeconds(60)
    };

    private readonly string _keyId;
    private readonly string _secret;

    public AlpacaClient(IConfiguration config)
    {
        _keyId = config["Alpaca:KeyId"] ?? "";
        _secret = config["Alpaca:SecretKey"] ?? "";
    }

    private async Task<string> GetAsync(string url)
    {
        if (string.IsNullOrEmpty(_keyId) || string.IsNullOrEmpty(_secret))
            throw new InvalidOperationException("Alpaca anahtarları bulunamadı (user-secrets).");

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("APCA-API-KEY-ID", _keyId);
        req.Headers.Add("APCA-API-SECRET-KEY", _secret);

        using var res = await Http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Alpaca hata kodu: {(int)res.StatusCode} - {body}");

        return body;
    }

    private static void ParseBars(JsonElement root, List<Bar> into)
    {
        if (root.TryGetProperty("bars", out var bars) && bars.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in bars.EnumerateArray())
            {
                into.Add(new Bar(
                    b.GetProperty("t").GetDateTimeOffset(),
                    b.GetProperty("o").GetDecimal(),
                    b.GetProperty("h").GetDecimal(),
                    b.GetProperty("l").GetDecimal(),
                    b.GetProperty("c").GetDecimal(),
                    b.GetProperty("v").GetInt64()));
            }
        }
    }

    public async Task<LastTrade> GetLastTradeAsync(string symbol, string feed = "iex")
    {
        var body = await GetAsync($"v2/stocks/{symbol}/trades/latest?feed={feed}");
        using var doc = JsonDocument.Parse(body);
        var trade = doc.RootElement.GetProperty("trade");

        return new LastTrade(
            symbol,
            trade.GetProperty("p").GetDecimal(),
            trade.GetProperty("t").GetDateTimeOffset());
    }

    public async Task<List<Bar>> GetBarsAsync(string symbol, int count = 100, string feed = "iex")
    {
        var start = DateTime.UtcNow.AddDays(-4).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var url = $"v2/stocks/{symbol}/bars?timeframe=1Min&limit={count}&sort=desc&feed={feed}" +
                  $"&start={Uri.EscapeDataString(start)}";

        var body = await GetAsync(url);
        using var doc = JsonDocument.Parse(body);

        var list = new List<Bar>();
        ParseBars(doc.RootElement, list);
        list.Reverse(); // eskiden yeniye sırala
        return list;
    }

    // Geçmiş veri: başlangıçtan bugüne, sayfa sayfa (eskiden yeniye)
    public async Task<List<Bar>> GetBarsRangeAsync(string symbol, DateTime startUtc, string feed = "iex")
    {
        var all = new List<Bar>();
        var start = Uri.EscapeDataString(startUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        string? token = null;
        int pages = 0;

        do
        {
            var url = $"v2/stocks/{symbol}/bars?timeframe=1Min&limit=10000&sort=asc" +
                      $"&adjustment=split&feed={feed}&start={start}";
            if (token != null) url += $"&page_token={Uri.EscapeDataString(token)}";

            var body = await GetAsync(url);
            using var doc = JsonDocument.Parse(body);
            ParseBars(doc.RootElement, all);

            token = null;
            if (doc.RootElement.TryGetProperty("next_page_token", out var t) &&
                t.ValueKind == JsonValueKind.String)
                token = t.GetString();

            pages++;
        } while (token != null && pages < 40);

        return all;
    }
}
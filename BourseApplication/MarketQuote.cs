namespace BourseApplication;

public sealed class MarketQuote
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "CAD";
    public DateTimeOffset TimestampUtc { get; set; }
    public string Source { get; set; } = "Market";
    public bool IsDelayed { get; set; }
    public bool IsRealTime { get; set; }
}

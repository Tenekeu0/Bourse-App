namespace BourseApplication;

public class Portfolio
{
    public long Id { get; init; }
    public string Currency { get; init; } = string.Empty;
    public decimal InitialCapital { get; init; }
    public decimal MaxRiskPerPosition { get; init; }
    public decimal MaxExposurePerAsset { get; init; }
    public decimal DailyLossLimit { get; init; }
    public int MaxOpenPositions { get; init; }

    public List<PortfolioPosition> Positions { get; set; } = new();
}

public class PortfolioPosition
{
    public string Symbol { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal AveragePrice { get; set; }
}

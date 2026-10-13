namespace BourseApplication;

public sealed class DashboardAction
{
    public string Symbol { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
    public decimal Notional { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public bool IsAvailable { get; init; }
    public bool IsDelayed { get; init; }
    public bool IsRealTime { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; }
}

public sealed class PortfolioDashboard
{
    public string Currency { get; init; } = string.Empty;
    public decimal InitialCapital { get; init; }
    public decimal AvailableCash { get; init; }
    public int PositionCount { get; init; }
    public int MaxPositions { get; init; }
    public decimal RiskPerPosition { get; init; }
    public decimal MaxExposurePerAsset { get; init; }
    public decimal DailyLossLimit { get; init; }
    public List<DashboardAction> Buyable { get; init; } = new();
    public List<DashboardAction> Sellable { get; init; } = new();
    public List<UnavailableDashboardQuote> UnavailableQuotes { get; } = new();
}

public sealed class UnavailableDashboardQuote
{
    public string Symbol { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public static class PortfolioDashboardService
{
    public static PortfolioDashboard Build(Portfolio? portfolio, IEnumerable<MarketQuote?> quotes)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        ArgumentNullException.ThrowIfNull(quotes);

        var quotesBySymbol = quotes
            .Where(quote => quote is not null)
            .Select(quote => quote!)
            .GroupBy(quote => quote.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var availableCash = portfolio.InitialCapital - portfolio.Positions.Sum(position => position.AveragePrice * position.Quantity);
        var buyable = quotesBySymbol.Values
            .Select(quote => BuildBuyAction(portfolio, quote, availableCash))
            .Where(item => item.IsAvailable)
            .OrderBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sellable = portfolio.Positions
            .Where(position => position.Quantity > 0)
            .Where(position => quotesBySymbol.ContainsKey(position.Symbol))
            .Select(position => BuildSellAction(position, quotesBySymbol[position.Symbol]))
            .Where(item => item.IsAvailable)
            .OrderBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PortfolioDashboard
        {
            Currency = portfolio.Currency,
            InitialCapital = portfolio.InitialCapital,
            AvailableCash = availableCash,
            PositionCount = portfolio.Positions.Count(position => position.Quantity > 0),
            MaxPositions = portfolio.MaxOpenPositions,
            RiskPerPosition = portfolio.MaxRiskPerPosition,
            MaxExposurePerAsset = portfolio.MaxExposurePerAsset,
            DailyLossLimit = portfolio.DailyLossLimit,
            Buyable = buyable,
            Sellable = sellable
        };
    }

    private static DashboardAction BuildBuyAction(Portfolio portfolio, MarketQuote quote, decimal availableCash)
    {
        var matchingPosition = portfolio.Positions
            .FirstOrDefault(position => string.Equals(position.Symbol, quote.Symbol, StringComparison.OrdinalIgnoreCase));

        var existingExposure = quote.Price * (matchingPosition?.Quantity ?? 0m);
        var maxQtyByRisk = quote.Price > 0m ? decimal.Floor(portfolio.MaxRiskPerPosition / quote.Price) : 0m;
        var maxQtyByExposure = quote.Price > 0m
            ? decimal.Floor(Math.Max(0m, portfolio.MaxExposurePerAsset - existingExposure) / quote.Price)
            : 0m;
        var maxQtyByCash = quote.Price > 0m ? decimal.Floor(Math.Max(0m, availableCash) / quote.Price) : 0m;
        var maxQty = Math.Min(maxQtyByRisk, Math.Min(maxQtyByExposure, maxQtyByCash));

        var openPositionCount = portfolio.Positions.Count(position => position.Quantity > 0);
        var canBuy = maxQty > 0m && (matchingPosition is not null || openPositionCount < portfolio.MaxOpenPositions);
        var quantity = canBuy ? maxQty : 0m;

        return new DashboardAction
        {
            Symbol = quote.Symbol,
            Action = "Buy",
            Price = quote.Price,
            Quantity = quantity,
            Notional = quantity * quote.Price,
            Currency = quote.Currency,
            Source = quote.Source,
            IsAvailable = canBuy,
            IsDelayed = quote.IsDelayed,
            IsRealTime = quote.IsRealTime,
            Reason = canBuy ? "Within risk and exposure caps" : "Not eligible under current portfolio limits",
            TimestampUtc = quote.TimestampUtc
        };
    }

    private static DashboardAction BuildSellAction(PortfolioPosition position, MarketQuote quote)
    {
        var canSell = position.Quantity > 0;

        return new DashboardAction
        {
            Symbol = position.Symbol,
            Action = "Sell",
            Price = quote.Price,
            Quantity = position.Quantity,
            Notional = quote.Price * position.Quantity,
            Currency = quote.Currency,
            Source = quote.Source,
            IsAvailable = canSell,
            IsDelayed = quote.IsDelayed,
            IsRealTime = quote.IsRealTime,
            Reason = canSell ? "Position is open and can be liquidated" : "No open quantity",
            TimestampUtc = quote.TimestampUtc
        };
    }
}

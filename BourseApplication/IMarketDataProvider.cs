namespace BourseApplication;

public interface IMarketDataProvider
{
    Task<MarketQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);
}

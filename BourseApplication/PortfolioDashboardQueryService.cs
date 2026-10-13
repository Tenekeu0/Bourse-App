namespace BourseApplication;

public sealed class PortfolioDashboardQueryService(
    IPortfolioRepository portfolioRepository,
    IMarketDataProvider marketDataProvider)
{
    public async Task<Portfolio> GetPortfolioAsync(long? selectedPortfolioId, CancellationToken cancellationToken = default)
    {
        var portfolios = await portfolioRepository.GetPortfoliosAsync(cancellationToken);
        if (portfolios.Count == 0)
        {
            throw new PortfolioNotFoundException("No portfolio is configured in SQL Server.");
        }

        if (selectedPortfolioId is long portfolioId)
        {
            return portfolios.FirstOrDefault(portfolio => portfolio.Id == portfolioId)
                ?? throw new PortfolioNotFoundException($"Configured portfolio {portfolioId} was not found.");
        }

        return portfolios.Count == 1
            ? portfolios[0]
            : throw new PortfolioSelectionRequiredException();
    }

    public async Task<PortfolioDashboard> GetDashboardAsync(long? selectedPortfolioId, CancellationToken cancellationToken = default)
    {
        var portfolio = await GetPortfolioAsync(selectedPortfolioId, cancellationToken);
        var instruments = await portfolioRepository.GetActiveInstrumentsAsync(cancellationToken);
        var currencyBySymbol = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var instrument in instruments)
        {
            AddCurrency(currencyBySymbol, instrument.Symbol, instrument.Currency);
        }

        foreach (var position in portfolio.Positions)
        {
            AddCurrency(currencyBySymbol, position.Symbol, position.Currency);
        }

        var symbols = instruments
            .Select(instrument => instrument.Symbol)
            .Concat(portfolio.Positions.Select(position => position.Symbol))
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var results = await Task.WhenAll(symbols.Select(async symbol =>
        {
            try
            {
                var quote = await marketDataProvider.GetQuoteAsync(symbol, cancellationToken);
                quote.Currency = currencyBySymbol[symbol];
                return new QuoteLoadResult(quote, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (MarketDataUnavailableException)
            {
                return new QuoteLoadResult(null, new UnavailableDashboardQuote
                {
                    Symbol = symbol,
                    Currency = currencyBySymbol[symbol],
                    Reason = "Cours Finnhub indisponible."
                });
            }
        }));

        var dashboard = PortfolioDashboardService.Build(portfolio, results
            .Where(result => result.Quote is not null)
            .Select(result => result.Quote));
        dashboard.UnavailableQuotes.AddRange(results
            .Where(result => result.UnavailableQuote is not null)
            .Select(result => result.UnavailableQuote!));
        return dashboard;
    }

    private static void AddCurrency(Dictionary<string, string> currencies, string symbol, string currency)
    {
        if (currencies.TryGetValue(symbol, out var existingCurrency)
            && !string.Equals(existingCurrency, currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Ticker {symbol} has conflicting currency metadata.");
        }

        currencies[symbol] = currency;
    }

    private sealed record QuoteLoadResult(MarketQuote? Quote, UnavailableDashboardQuote? UnavailableQuote);
}
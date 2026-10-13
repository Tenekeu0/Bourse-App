using BourseApplication;
using Dapper;

namespace BourseInfrastructure;

public sealed class SqlPortfolioRepository(SqlConnectionFactory connectionFactory) : IPortfolioRepository
{
    public async Task<IReadOnlyList<Portfolio>> GetPortfoliosAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        var portfolios = (await connection.QueryAsync<Portfolio>(new CommandDefinition(
            """
            SELECT Id, Currency, InitialCapital, MaxRiskPerPosition, MaxExposurePerAsset,
                   DailyLossLimit, MaxOpenPositions
            FROM dbo.Portfolios
            ORDER BY Id;
            """,
            cancellationToken: cancellationToken))).ToList();

        if (portfolios.Count == 0)
        {
            return portfolios;
        }

        var positions = await connection.QueryAsync<PortfolioPositionRow>(new CommandDefinition(
            """
            SELECT position.PortfolioId, instrument.Ticker AS Symbol, instrument.Currency,
                   position.Quantity, position.AveragePrice
            FROM dbo.Positions AS position
            INNER JOIN dbo.Instruments AS instrument ON instrument.Id = position.InstrumentId
            WHERE position.PortfolioId IN @PortfolioIds
              AND position.Quantity > 0;
            """,
            new { PortfolioIds = portfolios.Select(portfolio => portfolio.Id).ToArray() },
            cancellationToken: cancellationToken));

        var portfoliosById = portfolios.ToDictionary(portfolio => portfolio.Id);
        foreach (var position in positions)
        {
            portfoliosById[position.PortfolioId].Positions.Add(new PortfolioPosition
            {
                Symbol = position.Symbol,
                Currency = position.Currency,
                Quantity = position.Quantity,
                AveragePrice = position.AveragePrice
            });
        }

        return portfolios;
    }

    public async Task<IReadOnlyList<PortfolioInstrument>> GetActiveInstrumentsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        var instruments = await connection.QueryAsync<PortfolioInstrument>(new CommandDefinition(
            """
            SELECT instrument.Ticker AS Symbol, instrument.Currency
            FROM dbo.Instruments AS instrument
            INNER JOIN dbo.MarketExchanges AS exchange ON exchange.Id = instrument.ExchangeId
            WHERE instrument.IsActive = 1
              AND exchange.Country = 'CA'
            ORDER BY instrument.Ticker;
            """,
            cancellationToken: cancellationToken));

        return instruments.AsList();
    }

    private sealed class PortfolioPositionRow
    {
        public long PortfolioId { get; init; }
        public string Symbol { get; init; } = string.Empty;
        public string Currency { get; init; } = string.Empty;
        public decimal Quantity { get; init; }
        public decimal AveragePrice { get; init; }
    }
}
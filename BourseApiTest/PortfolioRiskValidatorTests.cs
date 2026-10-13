using System.Net;
using System.Text.Json;
using BourseApplication;
using BourseInfrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BourseApiTest;

public class PortfolioRiskValidatorTests
{
    [Fact]
    public void ValidateOrder_Allows_Order_Below_Risk_Limits()
    {
        var portfolio = CreatePortfolio();
        var order = new TradeOrder
        {
            Symbol = "CNQ",
            OrderSide = OrderSide.Buy,
            Quantity = 1,
            EstimatedPrice = 4m
        };

        var result = PortfolioRiskValidator.Validate(portfolio, order);

        Assert.True(result.IsValid);
        Assert.Equal(string.Empty, result.Reason);
    }

    [Fact]
    public void ValidateOrder_Rejects_Order_Above_Max_Position_Risk()
    {
        var portfolio = CreatePortfolio();
        var order = new TradeOrder
        {
            Symbol = "SHOP",
            OrderSide = OrderSide.Buy,
            Quantity = 100,
            EstimatedPrice = 100m
        };

        var result = PortfolioRiskValidator.Validate(portfolio, order);

        Assert.False(result.IsValid);
        Assert.Contains("risk", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateOrder_Rejects_Unknown_OrderSide()
    {
        var result = PortfolioRiskValidator.Validate(CreatePortfolio(), new TradeOrder
        {
            Symbol = "CNQ",
            OrderSide = (OrderSide)42,
            Quantity = 1,
            EstimatedPrice = 4m
        });

        Assert.False(result.IsValid);
        Assert.Contains("side", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateOrder_Rejects_Opening_More_Than_Three_Positions()
    {
        var portfolio = CreatePortfolio();
        portfolio.Positions =
        [
            new PortfolioPosition { Symbol = "AAPL", Quantity = 1, AveragePrice = 10m },
            new PortfolioPosition { Symbol = "MSFT", Quantity = 1, AveragePrice = 10m },
            new PortfolioPosition { Symbol = "GOOG", Quantity = 1, AveragePrice = 10m }
        ];

        var order = new TradeOrder
        {
            Symbol = "AMZN",
            OrderSide = OrderSide.Buy,
            Quantity = 1,
            EstimatedPrice = 10m
        };

        var result = PortfolioRiskValidator.Validate(portfolio, order);

        Assert.False(result.IsValid);
        Assert.Contains("position", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dashboard_Builds_Buyable_And_Sellable_Items()
    {
        var portfolio = CreatePortfolio();
        portfolio.Positions =
        [
            new PortfolioPosition { Symbol = "CNQ", Quantity = 2, AveragePrice = 19m },
            new PortfolioPosition { Symbol = "BCE", Quantity = 10, AveragePrice = 22m }
        ];

        var quotes = new[]
        {
            new MarketQuote { Symbol = "CNQ", Price = 20m, Currency = "CAD", TimestampUtc = DateTimeOffset.UtcNow, Source = "Finnhub" },
            new MarketQuote { Symbol = "BCE", Price = 21m, Currency = "CAD", TimestampUtc = DateTimeOffset.UtcNow, Source = "Finnhub" },
            new MarketQuote { Symbol = "SHOP", Price = 4m, Currency = "CAD", TimestampUtc = DateTimeOffset.UtcNow, Source = "Finnhub" },
            new MarketQuote { Symbol = "RY", Price = 140m, Currency = "CAD", TimestampUtc = DateTimeOffset.UtcNow, Source = "Finnhub" }
        };

        var dashboard = PortfolioDashboardService.Build(portfolio, quotes);

        Assert.Contains(dashboard.Buyable, item => item.Symbol == "SHOP");
        Assert.Contains(dashboard.Sellable, item => item.Symbol == "CNQ" && item.Quantity == 2);
        Assert.Contains(dashboard.Sellable, item => item.Symbol == "BCE" && item.Quantity == 10);
    }

    [Fact]
    public void Dashboard_UsesCurrentQuoteForExistingPositionExposure()
    {
        var portfolio = new Portfolio
        {
            Currency = "CAD",
            InitialCapital = 100m,
            MaxRiskPerPosition = 50m,
            MaxExposurePerAsset = 50m,
            DailyLossLimit = 10m,
            MaxOpenPositions = 3,
            Positions = [new PortfolioPosition { Symbol = "CNQ", Currency = "CAD", Quantity = 3m, AveragePrice = 10m }]
        };

        var dashboard = PortfolioDashboardService.Build(portfolio,
        [
            new MarketQuote { Symbol = "CNQ", Price = 15m, Currency = "CAD", TimestampUtc = DateTimeOffset.UtcNow, Source = "Finnhub" }
        ]);

        Assert.DoesNotContain(dashboard.Buyable, item => item.Symbol == "CNQ");
    }

    [Fact]
    public async Task GetQuoteAsync_WhenApiKeyIsMissing_ReportsUnavailableQuote()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var provider = new RealTimeMarketDataProvider(new HttpClient(), configuration);

        await Assert.ThrowsAsync<FinnhubQuoteUnavailableException>(() => provider.GetQuoteAsync("CNQ"));
    }

    [Fact]
    public async Task GetQuoteAsync_WhenFinnhubRejectsKey_DoesNotExposeKeyOrReturnFallback()
    {
        const string apiKey = "unit-test-secret";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Finnhub"] = apiKey })
            .Build();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            (_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var provider = new RealTimeMarketDataProvider(httpClient, configuration);

        var exception = await Assert.ThrowsAsync<FinnhubQuoteUnavailableException>(() => provider.GetQuoteAsync("CNQ"));

        Assert.Contains("401", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetQuoteAsync_UsesFinnhubApiKeySecretPath()
    {
        const string apiKey = "configured-test-secret";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Finnhub:ApiKey"] = apiKey,
                ["MarketData:Finnhub:ApiKey"] = "set-your-finnhub-key-in-user-secrets-or-env"
            })
            .Build();
        string? requestQuery = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            requestQuery = Uri.UnescapeDataString(request.RequestUri!.Query);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"c\":18.75,\"t\":1234567890}")
            };
        }));
        var provider = new RealTimeMarketDataProvider(httpClient, configuration);

        await provider.GetQuoteAsync("CNQ");

        Assert.Contains($"token={apiKey}", requestQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("set-your-finnhub-key", requestQuery, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlConnectionFactory_UnwrapsBourseDbSecret()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BourseDb"] = "ConnectionString=\"Server=sql.example;Database=BourseDB;Trusted_Connection=True;TrustServerCertificate=True;\""
            })
            .Build();

        using var connection = new SqlConnectionFactory(configuration).Create();

        Assert.Equal("sql.example", connection.DataSource);
        Assert.Equal("BourseDB", connection.Database);
    }

    [Fact]
    public async Task GetQuoteAsync_PreservesFinnhubTimestampAndMarksOldQuoteDelayed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Finnhub"] = "unit-test-secret" })
            .Build();
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"c\":18.75,\"t\":1234567890}")
        }));
        var provider = new RealTimeMarketDataProvider(httpClient, configuration);

        var quote = await provider.GetQuoteAsync("cnq");

        Assert.NotNull(quote);
        Assert.Equal(18.75m, quote!.Price);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1234567890), quote.TimestampUtc);
        Assert.Equal("Finnhub", quote.Source);
        Assert.True(quote.IsDelayed);
        Assert.False(quote.IsRealTime);
    }

    [Fact]
    public void Dashboard_UsesFreshQuoteForFractionalPositionAndPreservesSourceTimestamp()
    {
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var portfolio = CreatePortfolio();
        portfolio.Positions.Add(new PortfolioPosition
        {
            Symbol = "CNQ",
            Quantity = 1.5m,
            AveragePrice = 20m,
        });

        var dashboard = PortfolioDashboardService.Build(portfolio,
        [
            new MarketQuote
            {
                Symbol = "CNQ",
                Price = 22m,
                Currency = "CAD",
                TimestampUtc = timestamp,
                Source = "Finnhub",
                IsDelayed = true
            }
        ]);

        var sell = Assert.Single(dashboard.Sellable);
        Assert.Equal(1.5m, sell.Quantity);
        Assert.Equal(22m, sell.Price);
        Assert.Equal(33m, sell.Notional);
        Assert.Equal("Finnhub", sell.Source);
        Assert.Equal(timestamp, sell.TimestampUtc);
    }

    [Fact]
    public async Task DashboardQuery_UsesPortfolioAndInstrumentsFromRepository()
    {
        var portfolio = new Portfolio
        {
            Id = 42,
            Currency = "CAD",
            InitialCapital = 500m,
            MaxRiskPerPosition = 5m,
            MaxExposurePerAsset = 100m,
            DailyLossLimit = 10m,
            MaxOpenPositions = 3,
            Positions = [new PortfolioPosition { Symbol = "CNQ", Currency = "CAD", Quantity = 1.5m, AveragePrice = 19m }]
        };
        var repository = new TestPortfolioRepository(
            [portfolio],
            [new PortfolioInstrument { Symbol = "SHOP", Currency = "CAD" }]);
        var marketData = new RecordingMarketDataProvider(symbol => new MarketQuote
        {
            Symbol = symbol,
            Price = symbol == "CNQ" ? 20m : 4m,
            Currency = string.Empty,
            TimestampUtc = DateTimeOffset.UtcNow,
            Source = "Finnhub",
            IsRealTime = true
        });
        var service = new PortfolioDashboardQueryService(repository, marketData);

        var dashboard = await service.GetDashboardAsync(null);

        Assert.Equal("CAD", dashboard.Currency);
        Assert.Contains("CNQ", marketData.RequestedSymbols);
        Assert.Contains("SHOP", marketData.RequestedSymbols);
        var sell = Assert.Single(dashboard.Sellable);
        Assert.Equal(1.5m, sell.Quantity);
        Assert.Equal(20m, sell.Price);
        Assert.Equal("CAD", sell.Currency);
        Assert.Equal("Finnhub", sell.Source);
    }

    [Fact]
    public async Task DashboardQuery_WhenSqlHasNoPortfolio_ThrowsWithoutCreatingOne()
    {
        var repository = new TestPortfolioRepository([], []);
        var marketData = new RecordingMarketDataProvider(_ => throw new InvalidOperationException("Quote should not be requested."));
        var service = new PortfolioDashboardQueryService(repository, marketData);

        await Assert.ThrowsAsync<PortfolioNotFoundException>(() => service.GetDashboardAsync(null));
        Assert.Empty(marketData.RequestedSymbols);
    }

    [Fact]
    public async Task DashboardQuery_WhenOneQuoteFails_KeepsOtherQuotesAndReportsUnavailableSymbol()
    {
        var portfolio = CreatePortfolio();
        var repository = new TestPortfolioRepository(
            [portfolio],
            [
                new PortfolioInstrument { Symbol = "CNQ", Currency = "CAD" },
                new PortfolioInstrument { Symbol = "SHOP", Currency = "CAD" }
            ]);
        var marketData = new RecordingMarketDataProvider(symbol => symbol == "SHOP"
            ? throw new MarketDataUnavailableException("Test quote failure.")
            : new MarketQuote
            {
                Symbol = symbol,
                Price = 4m,
                Currency = "CAD",
                TimestampUtc = DateTimeOffset.UtcNow,
                Source = "Finnhub",
                IsRealTime = true
            });
        var service = new PortfolioDashboardQueryService(repository, marketData);

        var dashboard = await service.GetDashboardAsync(null);

        Assert.Equal("CNQ", Assert.Single(dashboard.Buyable).Symbol);
        Assert.Equal("SHOP", Assert.Single(dashboard.UnavailableQuotes).Symbol);
        Assert.Single(marketData.RequestedSymbols, symbol => symbol == "CNQ");
    }

    [Fact]
    public async Task DashboardQuery_WhenTickerHasConflictingCurrencies_RejectsAmbiguousMapping()
    {
        var repository = new TestPortfolioRepository(
            [CreatePortfolio()],
            [
                new PortfolioInstrument { Symbol = "XYZ", Currency = "CAD" },
                new PortfolioInstrument { Symbol = "XYZ", Currency = "USD" }
            ]);
        var marketData = new RecordingMarketDataProvider(_ => throw new InvalidOperationException("Quote should not be requested."));
        var service = new PortfolioDashboardQueryService(repository, marketData);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetDashboardAsync(null));
        Assert.Empty(marketData.RequestedSymbols);
    }

    [Fact]
    public async Task DashboardEndpoint_ReturnsThePayloadConsumedByTheFrontend()
    {
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var portfolio = CreatePortfolio();
        portfolio.Positions = [new PortfolioPosition { Symbol = "CNQ", Currency = "CAD", Quantity = 1.5m, AveragePrice = 19m }];
        var repository = new TestPortfolioRepository(
            [portfolio],
            [new PortfolioInstrument { Symbol = "CNQ", Currency = "CAD" }]);
        var provider = new RecordingMarketDataProvider(symbol => new MarketQuote
        {
            Symbol = symbol,
            Price = 20m,
            Currency = "CAD",
            TimestampUtc = timestamp,
            Source = "Finnhub",
            IsRealTime = true
        });
        using var factory = new DashboardApiFactory(repository, provider);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/dashboard");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var portfolioPayload = payload.RootElement.GetProperty("portfolio");
        var sellable = payload.RootElement.GetProperty("sellable")[0];

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("CAD", portfolioPayload.GetProperty("currency").GetString());
        Assert.Equal(1.5m, sellable.GetProperty("quantity").GetDecimal());
        Assert.Equal(20m, sellable.GetProperty("price").GetDecimal());
        Assert.Equal("Finnhub", sellable.GetProperty("source").GetString());
        Assert.Equal(timestamp, sellable.GetProperty("timestampUtc").GetDateTimeOffset());
    }

    private static Portfolio CreatePortfolio() => new()
    {
        Currency = "CAD",
        InitialCapital = 500m,
        MaxRiskPerPosition = 5m,
        MaxExposurePerAsset = 100m,
        DailyLossLimit = 10m,
        MaxOpenPositions = 3
    };

    private sealed class TestPortfolioRepository(
        IReadOnlyList<Portfolio> portfolios,
        IReadOnlyList<PortfolioInstrument> instruments) : IPortfolioRepository
    {
        public Task<IReadOnlyList<Portfolio>> GetPortfoliosAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(portfolios);

        public Task<IReadOnlyList<PortfolioInstrument>> GetActiveInstrumentsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(instruments);
    }

    private sealed class RecordingMarketDataProvider(Func<string, MarketQuote> createQuote) : IMarketDataProvider
    {
        public List<string> RequestedSymbols { get; } = new();

        public Task<MarketQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            RequestedSymbols.Add(symbol);
            return Task.FromResult(createQuote(symbol));
        }
    }

    private sealed class DashboardApiFactory(
        IPortfolioRepository portfolioRepository,
        IMarketDataProvider marketDataProvider) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPortfolioRepository>();
                services.RemoveAll<IMarketDataProvider>();
                services.AddSingleton(portfolioRepository);
                services.AddSingleton(marketDataProvider);
            });
        }
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request, cancellationToken));
    }
}

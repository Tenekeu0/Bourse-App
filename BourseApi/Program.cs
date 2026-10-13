using BourseApplication;
using BourseInfrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddCors(options =>
{
    options.AddPolicy("BourseFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5206", "https://localhost:5206")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddSingleton<SqlConnectionFactory>();
builder.Services.AddSingleton<IMarketDataProvider, RealTimeMarketDataProvider>();
builder.Services.AddScoped<IPortfolioRepository, SqlPortfolioRepository>();
builder.Services.AddScoped<PortfolioDashboardQueryService>();

var app = builder.Build();

app.UseCors("BourseFrontend");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Bourse API v1");
        options.RoutePrefix = "swagger";
    });
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", message = "Bourse API is running" }));

app.MapPost("/api/order/validate", async (
    TradeOrder order,
    IConfiguration configuration,
    PortfolioDashboardQueryService dashboardQuery,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    try
    {
        var configuredPortfolioId = GetConfiguredPortfolioId(configuration);
        var portfolio = await dashboardQuery.GetPortfolioAsync(configuredPortfolioId, cancellationToken);
        var result = PortfolioRiskValidator.Validate(portfolio, order);

        return Results.Ok(new
        {
            valid = result.IsValid,
            reason = result.Reason,
            order = order
        });
    }
    catch (PortfolioNotFoundException exception)
    {
        return Results.NotFound(new { error = exception.Message });
    }
    catch (PortfolioSelectionRequiredException)
    {
        return Results.Conflict(new { error = "Configure Portfolio:Id when more than one portfolio exists." });
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Microsoft.Data.SqlClient.SqlException exception)
    {
        logger.LogError(exception, "Order validation could not load portfolio data.");
        return Results.Problem(title: "Portfolio unavailable", detail: "Portfolio data could not be loaded.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/market/quote", async (
    string symbol,
    IMarketDataProvider marketDataProvider,
    IPortfolioRepository portfolioRepository,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    try
    {
        var instruments = await portfolioRepository.GetActiveInstrumentsAsync(cancellationToken);
        var instrument = instruments.FirstOrDefault(item =>
            string.Equals(item.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        if (instrument is null)
        {
            return Results.NotFound(new { error = $"No active Canadian instrument found for {symbol}." });
        }

        var quote = await marketDataProvider.GetQuoteAsync(symbol, cancellationToken);
        quote.Currency = instrument.Currency;
        return Results.Ok(quote);
    }
    catch (FinnhubQuoteUnavailableException exception)
    {
        return Results.Problem(title: "Market data unavailable", detail: exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Microsoft.Data.SqlClient.SqlException exception)
    {
        logger.LogError(exception, "Market instrument lookup failed for {Symbol}.", symbol);
        return Results.Problem(title: "Market quote unavailable", detail: "The instrument or quote could not be loaded.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/dashboard", async (
    IConfiguration configuration,
    PortfolioDashboardQueryService dashboardQuery,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    try
    {
        var configuredPortfolioId = GetConfiguredPortfolioId(configuration);
        var dashboard = await dashboardQuery.GetDashboardAsync(configuredPortfolioId, cancellationToken);

        return Results.Ok(new
        {
            portfolio = new
            {
                currency = dashboard.Currency,
                initialCapital = dashboard.InitialCapital,
                availableCash = dashboard.AvailableCash,
                positions = dashboard.PositionCount,
                maxPositions = dashboard.MaxPositions,
                riskPerPosition = dashboard.RiskPerPosition,
                maxExposurePerAsset = dashboard.MaxExposurePerAsset,
                dailyLossLimit = dashboard.DailyLossLimit
            },
            unavailableQuotes = dashboard.UnavailableQuotes.Select(item => new
            {
                symbol = item.Symbol,
                currency = item.Currency,
                reason = item.Reason
            }).ToList(),
            buyable = dashboard.Buyable.Select(item => new
            {
                symbol = item.Symbol,
                action = item.Action,
                price = item.Price,
                quantity = item.Quantity,
                notional = item.Notional,
                currency = item.Currency,
                source = item.Source,
                isDelayed = item.IsDelayed,
                isRealTime = item.IsRealTime,
                timestampUtc = item.TimestampUtc,
                reason = item.Reason
            }).ToList(),
            sellable = dashboard.Sellable.Select(item => new
            {
                symbol = item.Symbol,
                action = item.Action,
                price = item.Price,
                quantity = item.Quantity,
                notional = item.Notional,
                currency = item.Currency,
                source = item.Source,
                isDelayed = item.IsDelayed,
                isRealTime = item.IsRealTime,
                timestampUtc = item.TimestampUtc,
                reason = item.Reason
            }).ToList()
        });
    }
    catch (PortfolioNotFoundException exception)
    {
        return Results.NotFound(new { error = exception.Message });
    }
    catch (PortfolioSelectionRequiredException)
    {
        return Results.Conflict(new { error = "Configure Portfolio:Id when more than one portfolio exists." });
    }
    catch (FinnhubQuoteUnavailableException exception)
    {
        return Results.Problem(title: "Market data unavailable", detail: exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Dashboard request failed.");
        return Results.Problem(title: "Dashboard unavailable", detail: "Portfolio data could not be loaded.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();

static long? GetConfiguredPortfolioId(IConfiguration configuration)
{
    var configuredValue = configuration["Portfolio:Id"];
    if (string.IsNullOrWhiteSpace(configuredValue))
    {
        return null;
    }

    if (!long.TryParse(configuredValue, out var portfolioId) || portfolioId <= 0)
    {
        throw new InvalidOperationException("Portfolio:Id must be a positive integer.");
    }

    return portfolioId;
}

public partial class Program;

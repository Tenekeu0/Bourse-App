using System.Text.Json;
using BourseApplication;
using Microsoft.Extensions.Configuration;

namespace BourseInfrastructure;

public sealed class RealTimeMarketDataProvider : IMarketDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public RealTimeMarketDataProvider(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = new[]
            {
                configuration["Finnhub:ApiKey"],
                configuration["Finnhub"],
                configuration["FINNHUB_API_KEY"],
                Environment.GetEnvironmentVariable("FINNHUB_API_KEY"),
                Environment.GetEnvironmentVariable("Finnhub"),
                configuration["MarketData:Finnhub:ApiKey"]
            }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    public async Task<MarketQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = symbol?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new FinnhubQuoteUnavailableException("Finnhub API key is not configured.");
        }

        try
        {
            var requestUri = $"https://finnhub.io/api/v1/quote?symbol={Uri.EscapeDataString(normalized)}&token={Uri.EscapeDataString(_apiKey)}";

            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new FinnhubQuoteUnavailableException($"Finnhub returned HTTP {(int)response.StatusCode}.");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("c", out var currentPriceElement)
                || currentPriceElement.ValueKind != JsonValueKind.Number
                || !currentPriceElement.TryGetDecimal(out var price)
                || price <= 0m)
            {
                throw new FinnhubQuoteUnavailableException("Finnhub returned no valid current price for the symbol.");
            }

            if (!root.TryGetProperty("t", out var timestampElement)
                || timestampElement.ValueKind != JsonValueKind.Number
                || !timestampElement.TryGetInt64(out var timestampSeconds)
                || timestampSeconds <= 0)
            {
                throw new FinnhubQuoteUnavailableException("Finnhub returned no valid quote timestamp for the symbol.");
            }

            var timestampUtc = DateTimeOffset.FromUnixTimeSeconds(timestampSeconds);
            return new MarketQuote
            {
                Symbol = normalized.ToUpperInvariant(),
                Price = price,
                Currency = string.Empty,
                TimestampUtc = timestampUtc,
                Source = "Finnhub",
                IsDelayed = timestampUtc < DateTimeOffset.UtcNow.AddMinutes(-15),
                IsRealTime = timestampUtc >= DateTimeOffset.UtcNow.AddMinutes(-15)
            };
        }
        catch (FinnhubQuoteUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new FinnhubQuoteUnavailableException("Finnhub quote request failed.", exception);
        }
    }
}

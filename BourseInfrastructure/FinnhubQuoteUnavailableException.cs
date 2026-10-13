using BourseApplication;

namespace BourseInfrastructure;

public sealed class FinnhubQuoteUnavailableException : MarketDataUnavailableException
{
    public FinnhubQuoteUnavailableException(string message)
        : base(message)
    {
    }

    public FinnhubQuoteUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
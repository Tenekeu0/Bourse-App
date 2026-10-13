namespace BourseApplication;

public class MarketDataUnavailableException : Exception
{
    public MarketDataUnavailableException(string message)
        : base(message)
    {
    }

    public MarketDataUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
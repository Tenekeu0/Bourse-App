namespace BourseApplication;

public sealed class PortfolioNotFoundException : Exception
{
    public PortfolioNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class PortfolioSelectionRequiredException : Exception
{
    public PortfolioSelectionRequiredException()
        : base("Dashboard:PortfolioId must select one portfolio when more than one exists.")
    {
    }
}
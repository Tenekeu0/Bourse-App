namespace BourseApplication;

public interface IPortfolioRepository
{
    Task<IReadOnlyList<Portfolio>> GetPortfoliosAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PortfolioInstrument>> GetActiveInstrumentsAsync(CancellationToken cancellationToken = default);
}

public sealed class PortfolioInstrument
{
    public string Symbol { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
}
namespace BourseApplication;

public static class PortfolioRiskValidator
{
    public static OrderValidationResult Validate(Portfolio portfolio, TradeOrder order)
    {
        if (portfolio is null)
        {
            return OrderValidationResult.Failure("Portfolio is required.");
        }

        if (order is null)
        {
            return OrderValidationResult.Failure("Order is required.");
        }

        if (string.IsNullOrWhiteSpace(order.Symbol))
        {
            return OrderValidationResult.Failure("Symbol is required.");
        }

        if (!Enum.IsDefined(order.OrderSide))
        {
            return OrderValidationResult.Failure("Order side is invalid.");
        }

        if (order.Quantity <= 0)
        {
            return OrderValidationResult.Failure("Quantity must be greater than zero.");
        }

        if (order.EstimatedPrice <= 0m)
        {
            return OrderValidationResult.Failure("Estimated price must be greater than zero.");
        }

        var orderNotional = order.EstimatedPrice * order.Quantity;
        if (orderNotional > portfolio.MaxRiskPerPosition)
        {
            return OrderValidationResult.Failure("Order exceeds the maximum risk per position.");
        }

        var activePositions = portfolio.Positions.Count;
        var hasPositionForSymbol = portfolio.Positions.Any(p => string.Equals(p.Symbol, order.Symbol, StringComparison.OrdinalIgnoreCase));

        if (!hasPositionForSymbol && activePositions >= portfolio.MaxOpenPositions)
        {
            return OrderValidationResult.Failure("Opening a new position would exceed the maximum number of positions.");
        }

        var existingExposure = portfolio.Positions
            .Where(p => string.Equals(p.Symbol, order.Symbol, StringComparison.OrdinalIgnoreCase))
            .Sum(p => p.AveragePrice * p.Quantity);

        if (existingExposure + orderNotional > portfolio.MaxExposurePerAsset)
        {
            return OrderValidationResult.Failure("Order exceeds the maximum exposure per asset.");
        }

        return OrderValidationResult.Success();
    }
}

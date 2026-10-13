namespace BourseApplication;

public enum OrderSide
{
    Buy,
    Sell
}

public class TradeOrder
{
    public string Symbol { get; set; } = string.Empty;
    public OrderSide OrderSide { get; set; }
    public int Quantity { get; set; }
    public decimal EstimatedPrice { get; set; }
}

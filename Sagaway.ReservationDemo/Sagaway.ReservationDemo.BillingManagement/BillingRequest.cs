namespace Sagaway.ReservationDemo.BillingManagement;

// ReSharper disable once ClassNeverInstantiated.Global
public record BillingRequest
{
    public string CarClass { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public Guid ReservationId { get; set; }
    public ActionType ActionType { get; set; }
}
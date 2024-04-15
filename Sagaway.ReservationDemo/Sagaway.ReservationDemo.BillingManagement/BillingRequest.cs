namespace Sagaway.ReservationDemo.BillingManagement;

// ReSharper disable once ClassNeverInstantiated.Global
public record BillingRequest
{
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public required string CarClass { get; set; }
    public required string CustomerName { get; set; }
    public Guid ReservationId { get; set; }
    public ActionType ActionType { get; set; }
}
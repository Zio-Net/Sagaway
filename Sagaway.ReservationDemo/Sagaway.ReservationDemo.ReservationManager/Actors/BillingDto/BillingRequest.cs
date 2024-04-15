namespace Sagaway.ReservationDemo.ReservationManager.Actors.BillingDto;

public record BillingRequest
{
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public Guid ReservationId { get; set; }
    public required string CarClass { get; set; }
    public required string CustomerName { get; set; }
    public BillingRequestActionType ActionType { get; set; }
}
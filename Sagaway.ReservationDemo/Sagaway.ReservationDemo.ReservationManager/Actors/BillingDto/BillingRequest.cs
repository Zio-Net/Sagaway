namespace Sagaway.ReservationDemo.ReservationManager.Actors.BillingDto;

public record BillingRequest
{
    public Guid ReservationId { get; set; }
    public string CarClass { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public BillingRequestActionType ActionType { get; set; }
}
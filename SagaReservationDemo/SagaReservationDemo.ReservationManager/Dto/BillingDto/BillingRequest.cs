namespace SagaReservationDemo.ReservationManager.Dto.BillingDto;

public record BillingRequest
{
    public string CarClass { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public Guid ReservationId { get; set; }
    public BillingRequestActionType ActionType { get; set; }
}
namespace SagaReservationDemo.BookingManagement;

// ReSharper disable once ClassNeverInstantiated.Global
public record CarReservationRequest
{
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public ActionType ActionType { get; set; }
    public string CarClass { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public Guid ReservationId { get; set; }
}
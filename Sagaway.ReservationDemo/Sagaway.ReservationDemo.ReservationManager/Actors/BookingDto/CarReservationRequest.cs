namespace Sagaway.ReservationDemo.ReservationManager.Actors.BookingDto;

public record CarReservationRequest
{
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public CarReservationRequestActionType ActionType { get; set; }
    public required string CarClass { get; set; }
    public required string CustomerName { get; set; }
    public Guid ReservationId { get; set; }
}
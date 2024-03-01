namespace Sagaway.ReservationDemo.ReservationManager.Actors.BookingDto;

public record CarReservationRequest
{
    public CarReservationRequestActionType ActionType { get; set; }
    public string CarClass { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public Guid ReservationId { get; set; }
}
namespace Sagaway.ReservationDemo.BookingManagement;

// ReSharper disable once ClassNeverInstantiated.Global
public record CarReservationRequest
{
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public ActionType ActionType { get; set; }
    public required string CarClass { get; set; } 
    public required string CustomerName { get; set; }
    public Guid ReservationId { get; set; }
}
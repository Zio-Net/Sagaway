namespace Sagaway.ReservationDemo.ReservationUI.Services;

public class BookingInfo
{
    public Guid ReservationId { get; set; }
    public required string CustomerName { get; set; }
    public required string CarClass { get; set; } 
    public bool IsReserved { get; set; }
}
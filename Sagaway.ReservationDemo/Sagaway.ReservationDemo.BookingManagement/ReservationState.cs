namespace Sagaway.ReservationDemo.BookingManagement;

public record ReservationState 
{
    public required string CustomerName { get; set; }
    public bool IsReserved { get; set; }
    public Guid Id { get; set; }
    public DateTime ReservationStatusUpdateTime { get; init; }
}
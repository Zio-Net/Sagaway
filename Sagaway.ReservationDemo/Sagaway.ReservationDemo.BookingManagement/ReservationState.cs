namespace Sagaway.ReservationDemo.BookingManagement;

public record ReservationState 
{
    public string CustomerName { get; set; } = string.Empty;
    public bool IsReserved { get; set; }
    public Guid Id { get; set; }
}
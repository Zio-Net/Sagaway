namespace Sagaway.ReservationDemo.InventoryManagement;

public record ReservationOperationResult
{
    public Guid ReservationId { get; set; }
    public bool IsSuccess { get; set; }
}
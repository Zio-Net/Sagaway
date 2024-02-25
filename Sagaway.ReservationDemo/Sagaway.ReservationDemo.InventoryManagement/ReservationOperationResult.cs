namespace Sagaway.ReservationDemo.InventoryManagement;

public record ReservationOperationResult
{
    public string Activity { get; set; } = string.Empty;
    public Guid ReservationId { get; set; }
    public bool IsSuccess { get; set; }
}
namespace Sagaway.ReservationDemo.InventoryManagement;

public record ReservationState
{
    public Guid Id { get; set; }
    public string CarClass { get; set; } = string.Empty;
    public bool IsReserved { get; set; }
}


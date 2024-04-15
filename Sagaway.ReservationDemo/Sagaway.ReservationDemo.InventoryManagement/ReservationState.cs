namespace Sagaway.ReservationDemo.InventoryManagement;

public record ReservationState
{
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public Guid Id { get; set; }
    public DateTime LastUpdateTime { get; init; }
    public string CarClass { get; set; } = string.Empty;
    public bool IsReserved { get; set; }
}


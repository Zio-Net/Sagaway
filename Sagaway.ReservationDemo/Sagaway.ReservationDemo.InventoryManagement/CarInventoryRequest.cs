namespace Sagaway.ReservationDemo.InventoryManagement;

// ReSharper disable once ClassNeverInstantiated.Global
public record CarInventoryRequest
{
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public ActionType ActionType { get; set; }
    public required string CarClass { get; set; }
    public Guid OrderId { get; set; }
}
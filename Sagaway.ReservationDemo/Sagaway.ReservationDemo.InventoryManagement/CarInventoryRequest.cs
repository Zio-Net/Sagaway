namespace Sagaway.ReservationDemo.InventoryManagement;

// ReSharper disable once ClassNeverInstantiated.Global
public record CarInventoryRequest
{
    public ActionType ActionType { get; set; }
    public string CarClass { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
}
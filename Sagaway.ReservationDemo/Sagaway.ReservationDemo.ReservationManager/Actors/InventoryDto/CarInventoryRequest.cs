namespace Sagaway.ReservationDemo.ReservationManager.Actors.InventoryDto;

public class CarInventoryRequest
{
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public CarInventoryRequestActionType ActionType { get; set; }
    public required string CarClass { get; set; }
    public Guid OrderId { get; set; }
}
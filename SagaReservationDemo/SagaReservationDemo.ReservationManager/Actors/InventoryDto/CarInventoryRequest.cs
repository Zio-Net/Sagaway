namespace SagaReservationDemo.ReservationManager.Actors.InventoryDto;

public class CarInventoryRequest
{
    public CarInventoryRequestActionType ActionType { get; set; }
    public string CarClass { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
}
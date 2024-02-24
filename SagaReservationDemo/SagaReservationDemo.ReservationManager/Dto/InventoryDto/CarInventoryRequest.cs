namespace SagaReservationDemo.ReservationManager.Dto.InventoryDto;

public class CarInventoryRequest
{
    public CarInventoryRequestActionType ActionType { get; set; }
    public string CarClass { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string ResponseQueueName { get; set; } = string.Empty;
}
namespace SagaReservationDemo.InventoryManagement;

// ReSharper disable once ClassNeverInstantiated.Global
public record CarInventoryRequest
{
    public ActionType ActionType { get; set; }
    public string CarClass { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string ResponseQueueName { get; set; } = string.Empty;
}
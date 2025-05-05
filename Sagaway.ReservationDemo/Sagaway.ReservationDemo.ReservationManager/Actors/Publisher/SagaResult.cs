namespace Sagaway.ReservationDemo.ReservationManager.Actors.Publisher;

public record SagaResult
{
    public required string Outcome{ get; set; }
    public required Guid ReservationId { get; set; }
    public required string Log { get; set; }
    public required string CustomerName { get; set; }
    public required string CarClass { get; set; }
}
namespace Sagaway.ReservationDemo.ReservationManager.Actors;

// ReSharper disable once ClassNeverInstantiated.Global
public record ReservationOperationResult
{
    public Guid ReservationId { get; set; }
    public bool IsSuccess { get; set; }
}
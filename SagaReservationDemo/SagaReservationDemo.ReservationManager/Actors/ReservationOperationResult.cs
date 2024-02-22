namespace SagaReservationDemo.ReservationManager.Actors;

// ReSharper disable once ClassNeverInstantiated.Global
public record ReservationOperationResult
{
    public CarReservationActivity Activity { get; set; }
    public Guid ReservationId { get; set; }
    public bool IsSuccess { get; set; }
}
namespace SagaReservationDemo.ReservationManager.Workflows;

public record CancelCarReservationResult
{
    public bool IsSucceeded { get; init; }
}
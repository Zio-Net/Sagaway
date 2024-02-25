namespace Sagaway.ReservationDemo.ReservationManager.Actors;

public interface ICarReservationCancellationActor
{
    Task CancelCarReservationAsync(Guid reservationId);
    Task<bool> HandleCancellationActionResultAsync(string resultJson);
}

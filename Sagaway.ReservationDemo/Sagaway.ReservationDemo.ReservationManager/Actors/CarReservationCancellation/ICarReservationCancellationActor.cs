using Dapr.Actors;

namespace Sagaway.ReservationDemo.ReservationManager.Actors.CarReservationCancellation;

public interface ICarReservationCancellationActor : IActor
{
    Task CancelCarReservationAsync(ReservationInfo reservationInfo);
}
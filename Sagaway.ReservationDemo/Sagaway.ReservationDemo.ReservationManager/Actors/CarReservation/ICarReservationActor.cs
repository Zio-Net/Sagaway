using Dapr.Actors;

namespace Sagaway.ReservationDemo.ReservationManager.Actors.CarReservation;
public interface ICarReservationActor : IActor
{
    Task ReserveCarAsync(ReservationInfo reservationInfo);
}
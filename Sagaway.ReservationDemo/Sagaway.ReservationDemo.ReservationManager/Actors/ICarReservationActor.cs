using Dapr.Actors;

namespace Sagaway.ReservationDemo.ReservationManager.Actors;
public interface ICarReservationActor : IActor
{
    Task ReserveCarAsync(ReservationInfo reservationInfo);
}
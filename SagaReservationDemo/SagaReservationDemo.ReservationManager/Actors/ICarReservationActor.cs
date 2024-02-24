using System.Text.Json.Nodes;
using Dapr.Actors;
using Microsoft.Extensions.Configuration;

namespace SagaReservationDemo.ReservationManager.Actors;
public interface ICarReservationActor : IActor
{
    Task ReserveCarAsync(ReservationInfo reservationInfo);
}
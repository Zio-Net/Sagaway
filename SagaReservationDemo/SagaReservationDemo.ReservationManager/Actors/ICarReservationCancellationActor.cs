using System.Text.Json.Nodes;
using Dapr.Actors.Runtime;
using Sagaway.Hosts;

namespace SagaReservationDemo.ReservationManager.Actors;

public interface ICarReservationCancellationActor
{
    Task CancelCarReservationAsync(Guid reservationId);
    Task<bool> HandleCancellationActionResultAsync(string resultJson);
}

namespace Sagaway.ReservationDemo.ReservationManager.Actors.Publisher;

public interface ISagaResultPublisher
{
    Task PublishMessageToSignalRAsync(SagaResult result);
}
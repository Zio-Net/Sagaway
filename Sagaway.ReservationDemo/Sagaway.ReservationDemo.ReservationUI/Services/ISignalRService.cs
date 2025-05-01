namespace Sagaway.ReservationDemo.ReservationUI.Services;

public interface ISignalRService
{
    event Action<SagaUpdate>? OnSagaCompleted;
    Task InitializeAsync();
    bool IsConnected { get; }
}
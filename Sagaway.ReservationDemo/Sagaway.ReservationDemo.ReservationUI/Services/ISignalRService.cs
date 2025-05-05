using Sagaway.ReservationDemo.ReservationUI.Services.DTOs;

namespace Sagaway.ReservationDemo.ReservationUI.Services;

public interface ISignalRService
{
    event Action<SagaUpdate>? OnSagaCompleted;
    Task InitializeAsync();
    bool IsConnected { get; }
}
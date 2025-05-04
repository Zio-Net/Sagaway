using Microsoft.Azure.SignalR.Management;

namespace Sagaway.ReservationDemo.ReservationManager;

public interface IHubContextStore
{
    public ServiceHubContext? AccountManagerCallbackHubContext { get; }
}
using Microsoft.Azure.SignalR.Management;

namespace Sagaway.IntegrationTests.OrchestrationService;

public interface IHubContextStore
{
    public ServiceHubContext? AccountManagerCallbackHubContext { get; }
}
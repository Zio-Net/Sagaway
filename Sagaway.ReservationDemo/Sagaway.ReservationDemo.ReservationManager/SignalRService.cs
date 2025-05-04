using Microsoft.Azure.SignalR.Management;

namespace Sagaway.ReservationDemo.ReservationManager;

public class SignalRService(IConfiguration configuration, ILoggerFactory loggerFactory)
    : IHostedService, IHubContextStore
{
    private const string AccountManagerCallbackHub = "reservationcallback";

    public ServiceHubContext? AccountManagerCallbackHubContext { get; private set; }

    async Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        using var serviceManager = new ServiceManagerBuilder()
            .WithConfiguration(configuration)
            .WithLoggerFactory(loggerFactory)
            .BuildServiceManager();
        AccountManagerCallbackHubContext = await serviceManager.CreateHubContextAsync(AccountManagerCallbackHub, cancellationToken);
    }

    Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        if (AccountManagerCallbackHubContext != null)
        {
            return AccountManagerCallbackHubContext.DisposeAsync();
        }
        return Task.CompletedTask;
    }

    // ReSharper disable once UnusedMember.Local
    private static Task Dispose(IServiceHubContext? hubContext)
    {
        return hubContext == null ? Task.CompletedTask : hubContext.DisposeAsync();
    }
}
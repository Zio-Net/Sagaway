using Microsoft.Extensions.DependencyInjection;

namespace Sagaway.Routing.Tracking;

public static class TrackingRegistration
{
    public static void RegisterTracking(this IServiceCollection services)
    {
        services.AddScoped<ISignalRPublisher, SignalRPublisher>();
    }
}
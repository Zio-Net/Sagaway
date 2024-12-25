using Dapr.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Sagaway.Hosts.DaprActorHost;

public static class DaprStoreStepRecorderRegistrar
{
    public static void RegisterDaprStoreSagawayStepRecorder(this IServiceCollection services, string daprStateStoreName)
    {
        services.AddSingleton<IStepRecorder>(sp =>
        {
            var daprClient = sp.GetRequiredService<DaprClient>();
            return new DaprStoreStepRecorder(daprStateStoreName, daprClient);
        });
    }
}
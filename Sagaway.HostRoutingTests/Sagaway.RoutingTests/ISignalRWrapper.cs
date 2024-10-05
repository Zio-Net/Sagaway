using System.Text.Json;

namespace Sagaway.RoutingTests;

public interface ISignalRWrapper : IDisposable
{
    Task<bool> WaitForSignalREventAsync(int timeoutInSeconds = 1000);
    Task<bool> WaitForSignalREventWithConditionAsync(int timeoutInSeconds, Func<IReadOnlyList<string>, bool> condition);
    IReadOnlyList<string> Messages { get; }
    Task SwitchUserAsync(string alternativeUser, string eventName);
    Task StartSignalRAsync(params string[] eventNames);
    void ListenToSignalR(params string[] eventNames);
    void ClearMessages();
}
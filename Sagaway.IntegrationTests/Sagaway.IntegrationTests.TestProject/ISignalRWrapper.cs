using System.Text.Json.Nodes;

namespace Sagaway.IntegrationTests.TestProject;

public interface ISignalRWrapper : IDisposable
{
    Task<bool> WaitForSignalREventAsync(int timeoutInSeconds = 1000);
    Task<bool> WaitForSignalREventWithConditionAsync(int timeoutInSeconds, Func<IReadOnlyList<JsonObject>, bool> condition);
    IReadOnlyList<JsonObject> Messages { get; }
    Task SwitchUserAsync(string alternativeUser, string eventName);
    Task StartSignalRAsync(params string[] eventNames);
    void ListenToSignalR(params string[] eventNames);
    void ClearMessages();
}
using System.Text;
using Dapr.Client;

namespace Sagaway.Hosts.DaprActorHost;

public class DaprStoreStepRecorder : IStepRecorder
{
    private readonly string _daprStateStoreName;
    private readonly DaprClient _daprClient;
    private readonly StringBuilder _stepRecorder = new();

    // ReSharper disable once ConvertToPrimaryConstructor
    public DaprStoreStepRecorder(string daprStateStoreName, DaprClient daprClient)
    {
        _daprStateStoreName = daprStateStoreName;
        _daprClient = daprClient;
    }

    public async Task LoadSagaLogAsync(string sagaId)
    {
        var sagaLog = await _daprClient.GetStateAsync<string>(_daprStateStoreName, sagaId);
        _stepRecorder.Clear();
        _stepRecorder.Append(sagaLog);
    }

    public async Task SaveSagaLogAsync(string sagaId)
    {
        await _daprClient.SaveStateAsync(_daprStateStoreName, sagaId, _stepRecorder.ToString());
    }

    public Task RecordStepAsync(string sagaId, string step)
    {
        _stepRecorder.AppendLine(step);
        return Task.CompletedTask;
    }

    public Task<string> GetSagaLogAsync()
    {
        return Task.FromResult(_stepRecorder.ToString());
    }
}
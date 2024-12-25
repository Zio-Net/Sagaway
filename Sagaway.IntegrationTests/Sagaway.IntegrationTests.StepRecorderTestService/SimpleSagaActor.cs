using Dapr.Actors.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sagaway;
using Sagaway.Hosts;
using Sagaway.IntegrationTests.StepRecorderTestService;

[Actor(TypeName = "SimpleSagaActor")]
public class SimpleSagaActor : DaprActorHost<SimpleActorOperations>, ISimpleSagaActor
{
    private string? _stepRecorderType;
    private readonly IStepRecorder _stepRecorder;

    // ReSharper disable once ConvertToPrimaryConstructor
    public SimpleSagaActor(ActorHost host, ILogger logger, IServiceProvider? serviceProvider = null) : base(host, logger, serviceProvider)
    {
        _stepRecorder = serviceProvider!.GetRequiredService<IStepRecorder>();
    }

    protected override ISaga<SimpleActorOperations> ReBuildSaga()
    {
        if (string.IsNullOrEmpty(_stepRecorderType))
        {
            Task.Run(async () =>
            {
                var actorId = Guid.Parse(ActorHost.Id.ToString()).ToString("D");
                _stepRecorderType = await DaprClient.GetStateAsync<string>("statestore", actorId);
            }).Wait();
        }

        var sagaBuilder = Saga<SimpleActorOperations>.Create(ActorHost.Id.ToString(), this, Logger)
            .WithOnSuccessCompletionCallback(OnSuccessCompletionCallbackAsync);

        switch (_stepRecorderType)
        {
            case "empty":
                sagaBuilder.WithNullStepRecorder();
                break;
            case "Internal":
                //do nothing, the default behavior is to use internal step recorder
                break;
            case "statestore":
                sagaBuilder.WithStepRecorder(_stepRecorder);
                break;
            default:
                throw new InvalidOperationException($"Unknown step recorder type: {_stepRecorderType}");
        }

        sagaBuilder.WithOperation(SimpleActorOperations.Step1)
            .WithDoOperation()
        
        var saga = sagaBuilder.Build();

        saga.OnSagaCompleted += async (s, e) => await OnSagaCompletedAsync(s, e);

        return saga;
    }

    private async Task<object> OnSagaCompletedAsync(object? sender, SagaCompletionEventArgs sagaCompletionEventArgs)
    {
        throw new NotImplementedException();
    }

    public async Task RunSagaAsync(string stepRecorderType)
    {
        _stepRecorderType = stepRecorderType;
        try
        {
            if (Saga is { Completed: true })
            {
                Logger.LogInformation("The saga is already completed. Skip execution.");
                return;
            }

            Logger.LogInformation("RunSagaAsync called with step recorder type: {stepRecorderType}", stepRecorderType);

            _stepRecorderType = stepRecorderType;

            await StateManager.SetStateAsync("stepRecorderType", stepRecorderType);

            await SagaRunAsync();
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error in run test step recorde for step recorder type: {stepRecorderType}", stepRecorderType);
            throw;
        }
    }



    protected override string GetCallbackBindingName()
    {
        return "test-response-queue";
    }

    private void OnSuccessCompletionCallbackAsync(string obj)
    {
        throw new NotImplementedException();
    }
}
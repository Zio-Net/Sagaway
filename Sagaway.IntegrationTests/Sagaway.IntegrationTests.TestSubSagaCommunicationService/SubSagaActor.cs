using Dapr.Actors.Runtime;
using Sagaway.Hosts;

namespace Sagaway.IntegrationTests.TestSubSagaCommunicationService;

[Actor(TypeName = "SubSagaActor")]

// ReSharper disable once ClassNeverInstantiated.Global
public class SubSagaActor : DaprActorHost<SubSagaActorOperations>, ISubSagaActor
{
    // ReSharper disable once ConvertToPrimaryConstructor
    public SubSagaActor(ActorHost host, ILogger<SubSagaActor> logger, IServiceProvider? serviceProvider)
        : base(host, logger, serviceProvider)
    {
    }

    protected override ISaga<SubSagaActorOperations> ReBuildSaga()
    {
        var sagaBuilder = Saga<SubSagaActorOperations>.Create(ActorHost.Id.ToString(), this, Logger)
            .WithOnSuccessCompletionCallback(OnSuccessCompletionCallbackAsync)
            .WithOnRevertedCallback(OnRevertedCallbackAsync)
            .WithOnFailedRevertedCallback(OnFailedRevertedCallbackAsync)
            .WithOnFailedCallback(OnFailedCallbackAsync)

            .WithOperation(SubSagaActorOperations.Add)
            .WithDoOperation(OnAddInSubSagaAsync)
            .WithNoUndoAction()

            .WithOperation(SubSagaActorOperations.Done)
            .WithDoOperation(OnEndSagaAsync);


        var saga = sagaBuilder.Build();

        saga.OnSagaCompleted += (_, args) =>
        {
            Logger.LogInformation(args.Log);
        };

        return saga;
    }

    public async Task AddAsync(int a, int b, TimeSpan delay)
    {
        try
        {
            if (Saga is { Completed: true })
            {
                Logger.LogInformation("The sub-saga is already completed. Skip execution.");
                return;
            }

            Logger.LogInformation("AddAsync called to test the sub-saga");

            await StateManager.SetStateAsync("a", a);
            await StateManager.SetStateAsync("b", b);
            await StateManager.SetStateAsync("delay", delay.TotalSeconds);

            await SagaRunAsync();
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error in run sub-saga");
            throw;
        }
    }

    private async Task OnAddInSubSagaAsync()
    {
        //take delay from state store
        var delay = StateManager.GetStateAsync<double>("delay").Result;
        Logger.LogInformation("AddInSubSagaAsync called to test the sub-saga with delay {Delay}", delay);

        await Task.Delay(TimeSpan.FromSeconds(delay));

        //take a and b
        var a = await StateManager.GetStateAsync<int>("a");
        var b = await StateManager.GetStateAsync<int>("b");

        var result = a + b;

        Logger.LogInformation("AddInSubSagaAsync completed with result {Result}", result);

        await CallbackMainSagaAsync(new AddResult {Result = result});

        await ReportCompleteOperationOutcomeAsync(SubSagaActorOperations.Add, true);
    }

    public async Task DoneAsync()
    {
        Logger.LogInformation("DoneAsync called to test the sub-saga");
        await ReportCompleteOperationOutcomeAsync(SubSagaActorOperations.Done, true);
        await CallbackMainSagaAsync(new DoneResult {Result = true});
    }

    private void OnSuccessCompletionCallbackAsync(string obj)
    {
        Logger.LogInformation("MainSagaActor completed successfully.");
    }

    private void OnRevertedCallbackAsync(string obj)
    {
        Logger.LogInformation("MainSagaActor reverted successfully.");
    }

    private void OnFailedRevertedCallbackAsync(string obj)
    {
        Logger.LogInformation("MainSagaActor failed to revert.");
    }

    private void OnFailedCallbackAsync(string obj)
    {
        Logger.LogInformation("MainSagaActor failed.");
    }

    private Task OnEndSagaAsync()
    {
        Logger.LogInformation("SubSagaActor last operation");
        return Task.CompletedTask;
    }

    protected override string GetCallbackBindingName()
    {
        return "test-response-queue";
    }
}
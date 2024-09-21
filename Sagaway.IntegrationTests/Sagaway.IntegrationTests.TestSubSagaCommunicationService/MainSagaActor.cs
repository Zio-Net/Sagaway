using Dapr.Actors.Runtime;
using Sagaway.Hosts;

namespace Sagaway.IntegrationTests.TestSubSagaCommunicationService;

[Actor(TypeName = "MainSagaActor")]

// ReSharper disable once ClassNeverInstantiated.Global
public class MainSagaActor : DaprActorHost<MainSagaActorOperations>, IMainSagaActor
{
    // ReSharper disable once ConvertToPrimaryConstructor
    public MainSagaActor(ActorHost host, ILogger<MainSagaActor> logger, IServiceProvider? serviceProvider)
        : base(host, logger, serviceProvider)
    {
    }

    protected override ISaga<MainSagaActorOperations> ReBuildSaga()
    {
        var sagaBuilder = Saga<MainSagaActorOperations>.Create(ActorHost.Id.ToString(), this, Logger)
            .WithOnSuccessCompletionCallback(OnSuccessCompletionCallbackAsync)
            .WithOnRevertedCallback(OnRevertedCallbackAsync)
            .WithOnFailedRevertedCallback(OnFailedRevertedCallbackAsync)
            .WithOnFailedCallback(OnFailedCallbackAsync)

            .WithOperation(MainSagaActorOperations.CallSubSaga)
            .WithDoOperation(OnCallSubSagaAsync)
            .WithNoUndoAction()

            .WithOperation(MainSagaActorOperations.EndSaga)
            .WithDoOperation(OnEndSagaAsync);

        return sagaBuilder.Build();
    }

    public async Task RunTestAsync()
    {
        try
        {
            if (Saga is { Completed: true })
            {
                Logger.LogInformation("The saga is already completed. Skip execution.");
                return;
            }

            Logger.LogInformation("RunTestAsync called to test sub-saga");

            await SagaRunAsync();
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error in run sub-saga test");
            throw;
        }
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

    private async Task OnCallSubSagaAsync()
    {
        await CallSubSagaAsync<ISubSagaActor, SubSagaActor>(subSaga => subSaga.AddAsync(38, 4, TimeSpan.FromSeconds(5)),
            "Sub" + ActorHost.Id, nameof(OnAddResultAsync));
    }

    private async Task OnAddResultAsync(int result)
    {
        Logger.LogInformation("SubSagaActor completed with result {Result}", result);
        await ReportCompleteOperationOutcomeAsync(MainSagaActorOperations.CallSubSaga, true);

        await CallSubSagaAsync<ISubSagaActor, SubSagaActor>(subSaga => subSaga.DoneAsync(),
            "Sub" + ActorHost.Id);
    }

    private Task OnEndSagaAsync()
    {
        Logger.LogInformation("MainSagaActor completed successfully.");
        return Task.CompletedTask;
    }

    protected override string GetCallbackBindingName()
    {
        return "test-response-queue";
    }
}
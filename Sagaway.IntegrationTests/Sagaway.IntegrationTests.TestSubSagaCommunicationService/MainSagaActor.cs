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

        var saga = sagaBuilder.Build();
        
        saga.OnSagaCompleted += (_, args) =>
        {
            Logger.LogInformation(args.Log);
        };

        return saga;
    }

    private TestResult _testResult = TestResult.Running;

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

    public Task<TestResult> GetTestResultAsync()
    {
        return Task.FromResult(_testResult);
    }


    private void OnSuccessCompletionCallbackAsync(string obj)
    {
        _testResult = TestResult.Succeeded;
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
        _testResult = TestResult.Failed;
        Logger.LogInformation("MainSagaActor failed.");
    }

    private async Task OnCallSubSagaAsync()
    {
        Logger.LogInformation("Start calling sub-saga...");

        await CallSubSagaAsync<ISubSagaActor>(subSaga => subSaga.AddAsync(38, 4, TimeSpan.FromSeconds(5)),
            "SubSagaActor","Sub" + ActorHost.Id, nameof(OnAddResultAsync));
    }

    private async Task OnAddResultAsync(AddResult addResult)
    {
        Logger.LogInformation("SubSagaActor completed with result {Result}", addResult.Result);

        if (addResult.Result == 42)
        {
            Logger.LogInformation("Telling SubSagaActor to complete...");
            await CallSubSagaAsync<ISubSagaActor>(subSaga => subSaga.DoneAsync(),
                "SubSagaActor", "Sub" + ActorHost.Id, nameof(OnSubSagaEndAsync));

            // Wait for the sub-saga to be fully done before marking the operation complete.
            await ReportCompleteOperationOutcomeAsync(MainSagaActorOperations.CallSubSaga, true);
        }
        else
        {
            Logger.LogError("SubSagaActor failed or returned an unexpected result.");
            await ReportCompleteOperationOutcomeAsync(MainSagaActorOperations.CallSubSaga, false);
        }
    }

    private async Task OnSubSagaEndAsync(DoneResult doneResult)
    {
        Logger.LogInformation("SubSagaActor completed {result}", doneResult.Result ? "successfully" : "with a failure");
        await ReportCompleteOperationOutcomeAsync(MainSagaActorOperations.EndSaga, true);
    }

    private Task OnEndSagaAsync()
    {
        Logger.LogInformation("MainSagaActor last operation.");
        return Task.CompletedTask;
    }

    protected override string GetCallbackBindingName()
    {
        return "test-response-queue";
    }
}
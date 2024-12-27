using Dapr.Actors.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sagaway.Hosts;

namespace Sagaway.IntegrationTests.StepRecorderTestService;

[Actor(TypeName = nameof(StepRecorderTestActor))]
// ReSharper disable once ClassNeverInstantiated.Global
public class StepRecorderTestActor : DaprActorHost<TestRecorderActorOperations>, IStepRecorderTestActor
{
    private string? _stepRecorderType;
    private readonly IStepRecorder _stepRecorder;

    // ReSharper disable once ConvertToPrimaryConstructor
    public StepRecorderTestActor(ActorHost host, ILogger<StepRecorderTestActor> logger, IServiceProvider? serviceProvider = null) : base(host, logger, serviceProvider)
    {
        _stepRecorder = serviceProvider!.GetRequiredService<IStepRecorder>();
    }

    protected override ISaga<TestRecorderActorOperations> ReBuildSaga()
    {
        if (string.IsNullOrEmpty(_stepRecorderType))
        {
            _stepRecorderType = DaprClient.GetStateAsync<string>("statestore", "stepRecorderType").Result;
        }

        var sagaBuilder = Saga<TestRecorderActorOperations>.Create(ActorHost.Id.ToString(), this, Logger)
            .WithOnSuccessCompletionCallback(log => OnSuccessCompletionCallbackAsync(log).Wait())
            .WithOnFailedCallback(log => OnFailedCompletionCallbackAsync(log).Wait());

        switch (_stepRecorderType)
        {
            case "empty":
                sagaBuilder.WithNullStepRecorder();
                break;
            case "internal":
                //do nothing, the default behavior is to use internal step recorder
                break;
            case "statestore":
                sagaBuilder.WithStepRecorder(_stepRecorder);
                break;
            default:
                throw new InvalidOperationException($"Unknown step recorder type: {_stepRecorderType}");
        }

        sagaBuilder.WithOperation(TestRecorderActorOperations.Step1)
            .WithDoOperation(DoStep1)
            .WithNoUndoAction()
            .WithOperation(TestRecorderActorOperations.Step2)
            .WithDoOperation(DoStep2)
            .WithNoUndoAction()
            .WithOperation(TestRecorderActorOperations.Step3)
            .WithDoOperation(DoStep3)
            .WithNoUndoAction();

        var saga = sagaBuilder.Build();

        return saga;
    }

    private async Task DoStep1()
    {
        await ReportCompleteOperationOutcomeAsync(TestRecorderActorOperations.Step1, true);
    }

    private async Task DoStep2()
    {
        await ReportCompleteOperationOutcomeAsync(TestRecorderActorOperations.Step2, true);
    }

    private async Task DoStep3()
    {
        await ReportCompleteOperationOutcomeAsync(TestRecorderActorOperations.Step3, true);
    }

    public async Task RunSagaAsync()
    {
        try
        {
            if (Saga is { Completed: true })
            {
                Logger.LogInformation("The saga is already completed. Skip execution.");
                return;
            }

            Logger.LogInformation("RunSagaAsync called with step recorder type: {stepRecorderType}", _stepRecorderType);

            await SagaRunAsync();
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error in run test step recorde for step recorder type: {stepRecorderType}", _stepRecorderType);
            throw;
        }
    }

    protected override string GetCallbackBindingName()
    {
        return "test-response-queue";
    }

    private async Task OnSuccessCompletionCallbackAsync(string log)
    {
        switch (_stepRecorderType)
        {
            case "empty":
                await DaprClient.SaveStateAsync("statestore", "TestResult", string.IsNullOrEmpty(log));
                break;
            case "internal":
            case "statestore":
                bool testResult = log.Contains("Step1") && log.Contains("Step2") && log.Contains("Step3");
                await DaprClient.SaveStateAsync("statestore", "TestResult", testResult);
                break;
            default:
                throw new InvalidOperationException($"Unknown step recorder type: {_stepRecorderType}");
        }
    }

    private async Task OnFailedCompletionCallbackAsync(string _)
    {
        await DaprClient.SaveStateAsync("statestore", "TestResult", false);
    }
}
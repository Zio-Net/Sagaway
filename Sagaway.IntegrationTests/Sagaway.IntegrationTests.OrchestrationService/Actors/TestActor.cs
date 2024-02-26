using Dapr.Actors.Runtime;
using Microsoft.AspNetCore.Mvc;
using Sagaway.Hosts;
using System.Text.Json;

namespace Sagaway.IntegrationTests.OrchestrationService.Actors;

[Actor(TypeName = "TestActor")]
// ReSharper disable once UnusedType.Global
// ReSharper disable once ClassNeverInstantiated.Global
public class TestActor : DaprActorHost<TestActorOperations>, ITestActor
{
    private readonly ILogger<TestActor> _logger;
    private readonly ActorHost _actorHost;
    private TestInfo? _testInfo;

    // ReSharper disable once ConvertToPrimaryConstructor
    public TestActor(ActorHost host, 
        ILogger<TestActor> logger)
        : base(host, logger)
    {
        _actorHost = host;
        _logger = logger;
    }

    protected override ISaga<TestActorOperations> ReBuildSaga()
    {
        var saga = Saga<TestActorOperations>.Create(_actorHost.Id.ToString(), this, _logger)
            .WithOnSuccessCompletionCallback(OnSuccessCompletionCallbackAsync)
            .WithOnRevertedCallback(OnRevertedCallbackAsync)
            .WithOnFailedRevertedCallback(OnFailedRevertedCallbackAsync)
            .WithOnFailedCallback(OnFailedCallbackAsync)

            .WithOperation(TestActorOperations.CallA)
            .WithDoOperation(async ()=> await CallTestServiceAsync(TestActorOperations.CallA))
            .WithMaxRetries(5)
            .WithRetryIntervalTime(TimeSpan.FromSeconds(10))
            .WithValidateFunction(async ()=> await ValidateCallTestServiceAsync(TestActorOperations.CallA))
            .WithUndoOperation(async () => await RevertCallTestServiceAsync(TestActorOperations.CallA))
            .WithMaxRetries(5)
            .WithUndoRetryInterval(TimeSpan.FromSeconds(10))
            .WithValidateFunction(async () => await ValidateRevertCallTestServiceAsync(TestActorOperations.CallA))

            .WithOperation(TestActorOperations.CallB)
            .WithDoOperation(async () => await CallTestServiceAsync(TestActorOperations.CallB))
            .WithMaxRetries(5)
            .WithRetryIntervalTime(TimeSpan.FromSeconds(10))
            .WithValidateFunction(async () => await ValidateCallTestServiceAsync(TestActorOperations.CallB))
            .WithUndoOperation(async () => await RevertCallTestServiceAsync(TestActorOperations.CallB))
            .WithMaxRetries(5)
            .WithUndoRetryInterval(TimeSpan.FromSeconds(10))
            .WithValidateFunction(async () => await ValidateRevertCallTestServiceAsync(TestActorOperations.CallB))

            .Build();

        saga.OnSagaCompleted += async (s, e) => await OnSagaCompletedAsync(s, e);

        return saga;
    }


    #region Saga Activation methods

    protected override string GetCallbackQueueName()
    {
        return "test-response-queue";
    }

    protected override async Task OnActivateSagaAsync()
    {
        if (_testInfo == null)
        {
            _logger.LogInformation("The test info is empty. Assuming actor activation.");
            _testInfo = (await StateManager.TryGetStateAsync<TestInfo>("testInfo")).Value;
        }
    }

    public async Task RunTestAsync(TestInfo? testInfo)
    {
        try
        {
            _logger.LogInformation("RunTestAsync called with test info: {testInfo}", testInfo);

            if (_testInfo == null || _testInfo.Id == Guid.Empty)
            {
                _logger.LogError("RunTestAsync is called with an invalid test info");
                throw new Exception("RunTestAsync is called with an invalid test info");
            }

            await StateManager.SetStateAsync("testInfo", testInfo);
           
            _testInfo = testInfo;

            await SagaRunAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in run test async for test name: {testName}", _testInfo?.TestName);
            throw;
        }
    }

    private async Task CallTestServiceAsync(TestActorOperations testActorOperations)
    {
        //get from the state the iteration number
        var iterationId = $"iteration_{_testInfo!.Id}_{testActorOperations}";
        

        var storedIteration = await StateManager.TryGetStateAsync<int>(iterationId);

        int iteration = storedIteration.HasValue ? storedIteration.Value + 1 : 1;
        
        var info = testActorOperations switch
        {
            TestActorOperations.CallA => _testInfo!.ServiceACall,
            TestActorOperations.CallB => _testInfo!.ServiceBCall,
            _ => throw new ArgumentException("Invalid testActorOperations")
        };

        var testInfo = new ServiceTestInfo()
        {
            CallId = info!.CallId,
            IsReverting = false,
            DelayOnCallInSeconds = info.DelayOnCallInSeconds?[iteration - 1] ?? 0,
            ShouldSucceed = info.SuccessOnCall == iteration - 1,
            ShouldReturnCallbackResult = info.ShouldReturnCallbackResultOnCall?[iteration - 1] ?? true
        };

        var callbackFunctionName = testActorOperations switch
        {
            TestActorOperations.CallA => nameof(OnServiceAResultAsync),
            TestActorOperations.CallB => nameof(OnServiceBResultAsync),
            _ => throw new ArgumentException("Invalid testActorOperations")
        };

        _logger.LogInformation("{callOperation} for {testInfo}",
            testActorOperations, _testInfo);

        await DaprClient.InvokeBindingAsync("test-queue", "create", testInfo,
            GetCallbackMetadata(callbackFunctionName));

        await StateManager.SetStateAsync(iterationId, iteration);
    }

    private async Task OnServiceAResultAsync(bool result)
    {
        await ReportCompleteOperationOutcomeAsync(TestActorOperations.CallA, result);
    }

    private async Task OnServiceBResultAsync(bool result)
    {
        await ReportCompleteOperationOutcomeAsync(TestActorOperations.CallB, result);
    }

    private async Task<bool> ValidateCallTestServiceAsync(TestActorOperations testActorOperations)
    {
        var info = testActorOperations switch
        {
            TestActorOperations.CallA => _testInfo!.ServiceACall,
            TestActorOperations.CallB => _testInfo!.ServiceBCall,
            _ => throw new ArgumentException("Invalid testActorOperations")
        };

        var callId = info!.CallId;

        try
        {
            var successState =
                await DaprClient.InvokeMethodAsync<bool>(HttpMethod.Get, "testservice",
                    $"/test/{callId}");

            return successState;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in ValidateCallAAsync for call id: {callId}", callId);
            return false;
        }
    }

    private async Task RevertCallTestServiceAsync(TestActorOperations testActorOperations)
    {
        //get from the state the iteration number
        var iterationId = $"iteration_{_testInfo!.Id}_{testActorOperations}";

        var storedIteration = await StateManager.TryGetStateAsync<int>(iterationId);

        var iteration = storedIteration.HasValue ? storedIteration.Value : 1;

        var info = testActorOperations switch
        {
            TestActorOperations.CallA => _testInfo!.ServiceARevert,
            TestActorOperations.CallB => _testInfo!.ServiceBRevert,
            _ => throw new ArgumentException("Invalid testActorOperations")
        };

        var testInfo = new ServiceTestInfo()
        {
            CallId = info!.CallId,
            IsReverting = true,
            DelayOnCallInSeconds = info.DelayOnCallInSeconds?[iteration - 1] ?? 0,
            ShouldSucceed = info.SuccessOnCall == iteration - 1,
            ShouldReturnCallbackResult = info.ShouldReturnCallbackResultOnCall?[iteration - 1] ?? true
        };

        var callbackFunctionName = testActorOperations switch
        {
            TestActorOperations.CallA => nameof(OnRevertServiceAAsync),
            TestActorOperations.CallB => nameof(OnRevertServiceBAsync),
            _ => throw new ArgumentException("Invalid testActorOperations")
        };

        _logger.LogInformation("{callOperation} for {testInfo}",
            testActorOperations, _testInfo);

        await DaprClient.InvokeBindingAsync("test-queue", "create", testInfo,
            GetCallbackMetadata(callbackFunctionName));
    }

    private async Task OnRevertServiceAAsync(bool result)
    {
        await ReportUndoOperationOutcomeAsync(TestActorOperations.CallA, result);
    }

    private async Task OnRevertServiceBAsync(bool result)
    {
        await ReportUndoOperationOutcomeAsync(TestActorOperations.CallB, result);
    }

    private async Task<bool> ValidateRevertCallTestServiceAsync(TestActorOperations testActorOperations)
    {
        return !await ValidateCallTestServiceAsync(testActorOperations);
    }

    #endregion

    #region Saga Completion Methods

    private async void OnFailedRevertedCallbackAsync(string sagaLog)
    {
        _logger.LogError("The Test {TestName} has resulted a failure and left some unused resources. log: {sagaLog}", _testInfo!.TestName,
            Environment.NewLine + sagaLog);

        var testResult = new TestResult
        {
            TestInfo = _testInfo,
            IsSuccess = false,
            SagaLog = sagaLog
        };

        await PublishMessageToSignalRAsync(testResult);
    }

    private async void OnRevertedCallbackAsync(string sagaLog)
    {
        _logger.LogError("The Test {TestName} has resulted a failure. log: {sagaLog}", _testInfo!.TestName,
            Environment.NewLine + sagaLog);

        var testResult = new TestResult
        {
            TestInfo = _testInfo,
            IsSuccess = false,
            SagaLog = sagaLog
        };

        await PublishMessageToSignalRAsync(testResult);
    }

    private async void OnFailedCallbackAsync(string sagaLog)
    {
        _logger.LogError("The Test {TestName} has resulted a failure, starting reverting resources.", _testInfo!.TestName);

        await Task.CompletedTask;
    }

    private async void OnSuccessCompletionCallbackAsync(string sagaLog)
    {
        _logger.LogError("The Test {TestName} has resulted a success. log: {sagaLog}", _testInfo!.TestName,
            Environment.NewLine + sagaLog);

        var testResult = new TestResult
        {
            TestInfo = _testInfo,
            IsSuccess = true,
            SagaLog = sagaLog
        };

        await PublishMessageToSignalRAsync(testResult);
    }

    private async Task OnSagaCompletedAsync(object? _, SagaCompletionEventArgs e)
    {
        _logger.LogInformation($"Saga {e.SagaId} completed with status {e.Status}");
        await Task.CompletedTask;
    }

    #endregion

    private async Task PublishMessageToSignalRAsync(TestResult testResult)
    {
        var callbackRequest = JsonSerializer.Serialize(testResult);
        var argument = new Argument
        {
            Text = callbackRequest
        };

        SignalRMessage message = new()
        {
            Target = _testInfo!.Id.ToString(),
            Arguments = [argument]
        };

        _logger.LogInformation("publishing message to SignalR: {argumentText}", argument.Text);

        await DaprClient.InvokeBindingAsync("test-callback", "create", message); //sending through dapr to the signalR Hub
    }
}
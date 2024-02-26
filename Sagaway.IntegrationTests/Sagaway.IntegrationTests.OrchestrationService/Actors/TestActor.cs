using Dapr.Actors.Runtime;
using Sagaway.Hosts;

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
            .WithDoOperation(CallAAsync)
            .WithMaxRetries(5)
            .WithRetryIntervalTime(TimeSpan.FromSeconds(10))
            .WithValidateFunction(ValidateCallAAsync)
            .WithUndoOperation(RevertCallAAsync)
            .WithMaxRetries(5)
            .WithUndoRetryInterval(TimeSpan.FromSeconds(10))
            .WithValidateFunction(ValidateRevertCallAAsync)

            .WithOperation(TestActorOperations.CallB)
            .WithDoOperation(CallBAsync)
            .WithMaxRetries(5)
            .WithRetryIntervalTime(TimeSpan.FromSeconds(10))
            .WithValidateFunction(ValidateCallBAsync)
            .WithUndoOperation(RevertCallBAsync)
            .WithMaxRetries(5)
            .WithUndoRetryInterval(TimeSpan.FromSeconds(10))
            .WithValidateFunction(ValidateRevertCallBAsync)


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

    #endregion


    #region Call Service A metods

    private async Task CallAAsync()
    {
        _logger.LogInformation("Calling service A for test name: {testName} with info:{serviceA}", 
            _testInfo!.TestName, _testInfo.ServiceA);

        await DaprClient.InvokeBindingAsync("test-a-queue", "create", _testInfo.ServiceA, 
            GetCallbackMetadata(nameof(OnServiceAResultAsync)));
    }

    private async Task OnServiceAResultAsync(bool result)
    {
        await ReportCompleteOperationOutcomeAsync(TestActorOperations.CallA, result);
    }

    private async Task<bool> ValidateCallAAsync()
    {
        var callId = _testInfo!.ServiceA!.CallId;

        try
        {
            var successState =
                await DaprClient.InvokeMethodAsync<bool>(HttpMethod.Get, "testservicea",
                    $"/test/{callId}");

            return successState;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in ValidateCallAAsync for call id: {callId}", callId);
            return false;
        }
    }

    private async Task RevertCallAAsync()
    {
        _logger.LogInformation("Reverting call A for test name: {testName} with info:{serviceA}",
            _testInfo!.TestName, _testInfo.ServiceA);

        _testInfo.ServiceA!.IsReverting = true;
        await DaprClient.InvokeBindingAsync("testservicea", "create", _testInfo.ServiceA,
            GetCallbackMetadata(nameof(OnRevertServiceAAsync)));
    }

    private async Task OnRevertServiceAAsync(bool result)
    {
        await ReportUndoOperationOutcomeAsync(TestActorOperations.CallA, result);
    }

    private async Task<bool> ValidateRevertCallAAsync()
    {
        _logger.LogInformation("Validating revert call A for test name: {testName} with info:{serviceA}",
            _testInfo!.TestName, _testInfo.ServiceA);
        return !await ValidateCallAAsync();
    }

    #endregion

    #region Call Service A metods

    private async Task CallBAsync()
    {
        _logger.LogInformation("Calling service B for test name: {testName} with info:{serviceB}",
            _testInfo!.TestName, _testInfo.ServiceB);

        await DaprClient.InvokeBindingAsync("test-b-queue", "create", _testInfo.ServiceB,
            GetCallbackMetadata(nameof(OnServiceBResultAsync)));
    }

    private async Task OnServiceBResultAsync(bool result)
    {
        await ReportCompleteOperationOutcomeAsync(TestActorOperations.CallB, result);
    }

    private async Task<bool> ValidateCallBAsync()
    {
        var callId = _testInfo!.ServiceB!.CallId;

        try
        {
            var successState =
                await DaprClient.InvokeMethodAsync<bool>(HttpMethod.Get, "testserviceb",
                    $"/test/{callId}");

            return successState;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in ValidateCallBAsync for call id: {callId}", callId);
            return false;
        }
    }

    private async Task RevertCallBAsync()
    {
        _logger.LogInformation("Reverting call B for test name: {testName} with info:{serviceB}",
            _testInfo!.TestName, _testInfo.ServiceB);

        _testInfo.ServiceB!.IsReverting = true;
        await DaprClient.InvokeBindingAsync("testserviceb", "create", _testInfo.ServiceB,
            GetCallbackMetadata(nameof(OnRevertServiceBAsync)));
    }

    private async Task OnRevertServiceBAsync(bool result)
    {
        await ReportUndoOperationOutcomeAsync(TestActorOperations.CallB, result);
    }

    private async Task<bool> ValidateRevertCallBAsync()
    {
        _logger.LogInformation("Validating revert call B for test name: {testName} with info:{serviceB}",
            _testInfo!.TestName, _testInfo.ServiceB);
        return !await ValidateCallBAsync();
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

        await DaprClient.InvokeBindingAsync("test-callback", "create", testResult);
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

        await DaprClient.InvokeBindingAsync("test-callback", "create", testResult);
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

        await DaprClient.InvokeBindingAsync("test-callback", "create", testResult);
    }

    private async Task OnSagaCompletedAsync(object? _, SagaCompletionEventArgs e)
    {
        _logger.LogInformation($"Saga {e.SagaId} completed with status {e.Status}");
        await Task.CompletedTask;
    }

    #endregion
}
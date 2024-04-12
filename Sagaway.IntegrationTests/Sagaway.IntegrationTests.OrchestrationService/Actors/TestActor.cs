using Dapr.Actors.Runtime;
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
    public TestActor(ActorHost host, ILogger<TestActor> logger, IServiceProvider? serviceProvider)
        : base(host, logger, serviceProvider)
    {
        _actorHost = host;
        _logger = logger;
    }

    protected override ISaga<TestActorOperations> ReBuildSaga()
    {
        if (_testInfo == null)
        {
            Task.Run(async () =>
            {
                var actorId = Guid.Parse(ActorHost.Id.ToString()).ToString("D");
                _testInfo = await DaprClient.GetStateAsync<TestInfo>("statestore", actorId);
            }).Wait();
        }

        var sagaBuilder = Saga<TestActorOperations>.Create(_actorHost.Id.ToString(), this, _logger)
            .WithOnSuccessCompletionCallback(OnSuccessCompletionCallbackAsync)
            .WithOnRevertedCallback(OnRevertedCallbackAsync)
            .WithOnFailedRevertedCallback(OnFailedRevertedCallbackAsync)
            .WithOnFailedCallback(OnFailedCallbackAsync);

        if (_testInfo?.ServiceACall?.InUse ?? false)
        {
            var sagaOperationBuilder = sagaBuilder.WithOperation(TestActorOperations.CallA)
                .WithDoOperation(async () => await CallTestServiceAsync(TestActorOperations.CallA))
                .WithMaxRetries(_testInfo!.ServiceACall!.MaxRetries)
                .WithRetryIntervalTime(TimeSpan.FromSeconds(_testInfo.ServiceACall.RetryDelayInSeconds))
                .WithValidateFunction(async () => await ValidateCallTestServiceAsync(TestActorOperations.CallA));

            if (_testInfo?.ServiceARevert?.InUse ?? false)
            {
                sagaOperationBuilder
                    .WithUndoOperation(async () => await RevertCallTestServiceAsync(TestActorOperations.CallA))
                    .WithMaxRetries(_testInfo.ServiceARevert!.MaxRetries)
                    .WithUndoRetryInterval(TimeSpan.FromSeconds(_testInfo.ServiceARevert.RetryDelayInSeconds))
                    .WithValidateFunction(async () =>
                        await ValidateRevertCallTestServiceAsync(TestActorOperations.CallA));
            }
        }

        if (_testInfo?.ServiceBCall?.InUse ?? false)
        {
            var sagaOperationBuilder = sagaBuilder.WithOperation(TestActorOperations.CallB)
                .WithDoOperation(async () => await CallTestServiceAsync(TestActorOperations.CallB))
                .WithMaxRetries(_testInfo.ServiceBCall!.MaxRetries)
                .WithRetryIntervalTime(TimeSpan.FromSeconds(_testInfo.ServiceBCall.RetryDelayInSeconds))
                .WithValidateFunction(async () => await ValidateCallTestServiceAsync(TestActorOperations.CallB));

            if (_testInfo?.ServiceBRevert?.InUse ?? false)
            {
                sagaOperationBuilder
                    .WithUndoOperation(async () => await RevertCallTestServiceAsync(TestActorOperations.CallB))
                    .WithMaxRetries(_testInfo.ServiceBRevert!.MaxRetries)
                    .WithUndoRetryInterval(TimeSpan.FromSeconds(_testInfo.ServiceBRevert.RetryDelayInSeconds))
                    .WithValidateFunction(async () =>
                        await ValidateRevertCallTestServiceAsync(TestActorOperations.CallB));
            }
        }
        var saga = sagaBuilder.Build();

        saga.OnSagaCompleted += async (s, e) => await OnSagaCompletedAsync(s, e);

        return saga;
    }


    #region Saga Activation methods

    protected override string GetCallbackBindingName()
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

            if (testInfo == null || testInfo.Id == Guid.Empty)
            {
                _logger.LogError("RunTestAsync is called with an invalid test info");
                throw new Exception("RunTestAsync is called with an invalid test info");
            }

            if ((testInfo.ServiceACall!.ShouldReturnCallbackResultOnCall?.Length ?? 0) < testInfo.ServiceACall.SuccessOnCall)
            {
                _logger.LogError("The test A info is invalid. The success call index is less than the callback result length");
                throw new Exception("The test A info is invalid. The success call index is less than the callback result length");
            }

            if ((testInfo.ServiceBCall!.ShouldReturnCallbackResultOnCall?.Length ?? 0) < testInfo.ServiceBCall.SuccessOnCall)
            {
                _logger.LogError("The test B info is invalid. The success call index is less than the callback result length");
                throw new Exception("The test B info is invalid. The success call index is less than the callback result length");
            }

            if ((testInfo.ServiceACall.DelayOnCallInSeconds?.Length ?? 0) < testInfo.ServiceACall.SuccessOnCall)
            {
                _logger.LogError("The test A info is invalid. The delay call index is less than the callback result length");
                throw new Exception("The test A info is invalid. The delay call index is less than the callback result length");
            }

            if ((testInfo.ServiceBCall.DelayOnCallInSeconds?.Length ?? 0) < testInfo.ServiceBCall.SuccessOnCall)
            {
                _logger.LogError("The test B info is invalid. The delay call index is less than the callback result length");
                throw new Exception("The test B info is invalid. The delay call index is less than the callback result length");
            }

            _testInfo = testInfo;

            await InitiateIterationId(TestActorOperations.CallA, false);
            await InitiateIterationId(TestActorOperations.CallA, true);
            await InitiateIterationId(TestActorOperations.CallB, false);
            await InitiateIterationId(TestActorOperations.CallB, true);

            await StateManager.SetStateAsync("testInfo", testInfo);

            await SagaRunAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in run test async for test name: {testName}", _testInfo?.TestName);
            throw;
        }
    }

    private string GetIterationId(TestActorOperations testActorOperations, bool isReverting)
    {
        var prefix = isReverting ? "revert" : "iteration";
        return $"{prefix}_{_testInfo?.Id}_{testActorOperations}";
    }

    private async Task InitiateIterationId(TestActorOperations testActorOperations, bool isReverting)
    {
        var iterationId = GetIterationId(testActorOperations, isReverting);
        await StateManager.SetStateAsync(iterationId, 0);
    }

    private async Task CallTestServiceAsync(TestActorOperations testActorOperations)
    {
        var iterationId = GetIterationId(testActorOperations, false);
        var storedIteration = await StateManager.TryGetStateAsync<int>(iterationId);

        int iteration = storedIteration.HasValue ? storedIteration.Value + 1 : 1;
        
        var info = testActorOperations switch
        {
            TestActorOperations.CallA => _testInfo?.ServiceACall,
            TestActorOperations.CallB => _testInfo?.ServiceBCall,
            _ => throw new ArgumentException("Invalid testActorOperations")
        };

        var testInfo = new ServiceTestInfo()
        {
            CallId = info!.CallId,
            IsReverting = false,
            DelayOnCallInSeconds = info.DelayOnCallInSeconds?[iteration - 1] ?? 0,
            ShouldSucceed = info.SuccessOnCall == iteration,
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
        var iterationId = GetIterationId(testActorOperations, true);

        var storedIteration = await StateManager.TryGetStateAsync<int>(iterationId);

        int iteration = storedIteration.HasValue ? storedIteration.Value + 1 : 1;

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
            ShouldSucceed = info.SuccessOnCall == iteration,
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
        var jsonSerializationOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var callbackRequest = JsonSerializer.Serialize(testResult, jsonSerializationOptions);
        var jsonDocument = JsonDocument.Parse(callbackRequest);

        var argument = new Argument
        {
            Sender = "dapr",
            Text = jsonDocument
        };

        SignalRMessage message = new()
        {
            UserId = "testUser", 
            Target = _testInfo!.Id.ToString(),
            Arguments = [argument]
        };

        _logger.LogInformation("publishing message to SignalR: {argumentText}", argument.Text);

        await DaprClient.InvokeBindingAsync("testcallback", "create", message); //sending through dapr to the signalR Hub
    }
}
using Dapr.Actors.Runtime;
using Sagaway.Hosts;
using Sagaway.Routing.Tracking;

namespace Sagaway.Routing.TestActorA;

[Actor(TypeName = "TestActorA")]
// ReSharper disable once UnusedType.Global
// ReSharper disable once ClassNeverInstantiated.Global
public class TestActorA : DaprActorHost<TestActorOperations>, ITestActorA
{
    private readonly ILogger<TestActorA> _logger;
    private readonly ISignalRPublisher _signalRPublisher;
    private readonly ActorHost _actorHost;
    private CallChainInfo? _callChainInfo;

    // ReSharper disable once ConvertToPrimaryConstructor
    public TestActorA(ActorHost host, ILogger<TestActorA> logger, IServiceProvider serviceProvider,
        ISignalRPublisher signalRPublisher)
        : base(host, logger, serviceProvider)
    {
        _actorHost = host;
        _logger = logger;
        _signalRPublisher = signalRPublisher;
    }

    protected override ISaga<TestActorOperations> ReBuildSaga()
    {

        var sagaBuilder = Saga<TestActorOperations>.Create(_actorHost.Id.ToString(), this, _logger)
            .WithOnSuccessCompletionCallback(OnSuccessCompletionCallbackAsync)
            .WithOnRevertedCallback(OnRevertedCallbackAsync)
            .WithOnFailedRevertedCallback(OnFailedRevertedCallbackAsync)
            .WithOnFailedCallback(OnFailedCallbackAsync)

            .WithOperation(TestActorOperations.CallNextService)
            .WithDoOperation(CallNextServiceAsync)
            .WithNoUndoAction()

            .WithOperation(TestActorOperations.DoneAsync)
            .WithDoOperation(DoneAsync);


        var saga = sagaBuilder.Build();

        saga.OnSagaCompleted += async (s, e) => await OnSagaCompletedAsync(s, e);
        return saga;
    }

    protected override string GetCallbackBindingName()
    {
        return "TestActorAQueue";
    }


    public async Task InvokeAsync(CallChainInfo request)
    {
        try
        {
            if (Saga is { Completed: true })
            {
                _logger.LogInformation("The saga is already completed. Skip execution.");
                return;
            }

            _logger.LogInformation("InvokeAsync called with call chain info: {request}", request);


            _callChainInfo = request;
            await StateManager.SetStateAsync("callChainInfo", _callChainInfo);

            await SagaRunAsync();
        }
        catch (Exception e)
        {
            await _signalRPublisher.PublishMessageToSignalRAsync(_callChainInfo!.TestName, $"Failure from {nameof(TestActorA)} {_callChainInfo?.CallChainHistory}");
            _logger.LogError(e, "Error in run InvokeAsync");
            throw;
        }
    }


    private async Task CallNextServiceAsync()
    {
        _callChainInfo = await StateManager.GetStateAsync<CallChainInfo>("callChainInfo");   

        _logger.LogInformation("Actor Saga: Test {name} Call Chain instructions:{OriginalCallInstructions}  Actual test call chain so far: {ActualCallChain}, Left call chain: {callInstructions}",
            _callChainInfo!.TestName, _callChainInfo.OriginalCallInstructions, _callChainInfo.CallChainHistory, _callChainInfo.CallInstructions);
        var nextCall = _callChainInfo.PopNextInstruction();

        _logger.LogInformation("Actor A will call {nextCall}", string.IsNullOrEmpty(nextCall) ? "back" : nextCall);

        if (string.IsNullOrEmpty(nextCall)) //no more downstream calls
        {
            await DaprClient.InvokeBindingAsync(GetCallbackBindingName(), "create", _callChainInfo);
            return;
        }

        //else calling downstream

        await DaprClient.InvokeBindingAsync(nextCall + "queue", "create", _callChainInfo,
            GetCallbackMetadata(nameof(OnResultAsync)));

    }

    private async Task OnResultAsync(CallChainInfo result)
    {
        _callChainInfo = result;
        await StateManager.SetStateAsync("callChainInfo", _callChainInfo);

        _logger.LogInformation(
            "Actor A received result. Test {testName} Call Chain instructions:{OriginalCallInstructions}  Actual test call chain so far: {ActualCallChain}, Left call chain: {callInstructions}",
            result.TestName, result.OriginalCallInstructions, result.CallChainHistory, result.CallInstructions);

        var nextCall = result.PopNextInstruction();

        _logger.LogInformation("Actor A will call {nextCall}",
            string.IsNullOrEmpty(nextCall) || nextCall.ToLower() == "callback" ? "back" : nextCall);

        if (string.IsNullOrEmpty(nextCall)) //no more calls, we back to the first call, call to the test with SignalR
        {
            await ReportCompleteOperationOutcomeAsync(TestActorOperations.CallNextService, true);
            return;
        }

        if (nextCall.ToLower() == "callback") // calling back
        {
            await DaprClient.InvokeBindingAsync(GetCallbackBindingName(), "create", result);
            return;
        }

        //else calling downstream

        await DaprClient.InvokeBindingAsync(nextCall + "queue", "create", _callChainInfo,
            GetCallbackMetadata(nameof(OnResultAsync)));

    }

    private async Task DoneAsync()
    {
        _callChainInfo = await StateManager.GetStateAsync<CallChainInfo>("callChainInfo");

        _logger.LogInformation("Actor A is done. Test {TestName} Call Chain instructions:{OriginalCallInstructions}  Actual test call chain so far: {ActualCallChain}, Left call chain: {callInstructions}",
            _callChainInfo!.TestName, _callChainInfo.OriginalCallInstructions, _callChainInfo.CallChainHistory, _callChainInfo.CallInstructions);

        await _signalRPublisher.PublishMessageToSignalRAsync(_callChainInfo!.TestName, _callChainInfo.CallChainHistory);
        await ReportCompleteOperationOutcomeAsync(TestActorOperations.DoneAsync, true);
    }

    #region Saga Completion Methods

    private async void OnFailedRevertedCallbackAsync(string sagaLog)
    {
        _logger.LogError("The has resulted a failure and left some unused resources. log: {sagaLog}", Environment.NewLine + sagaLog);

        await _signalRPublisher.PublishMessageToSignalRAsync(_callChainInfo!.TestName, $"Failure from {nameof(TestActorA)} {_callChainInfo?.CallChainHistory}");
    }

    private async void OnRevertedCallbackAsync(string sagaLog)
    {
        _logger.LogError("The has resulted a failure a. log: {sagaLog}", Environment.NewLine + sagaLog);

        await _signalRPublisher.PublishMessageToSignalRAsync(_callChainInfo!.TestName, $"Failure from {nameof(TestActorA)} {_callChainInfo?.CallChainHistory}");
    }

    private async void OnFailedCallbackAsync(string sagaLog)
    {
        _logger.LogError("The Test  has resulted a failure, starting reverting resources.");
        await _signalRPublisher.PublishMessageToSignalRAsync(_callChainInfo!.TestName, $"Failure from {nameof(TestActorA)} {_callChainInfo?.CallChainHistory}");
        await Task.CompletedTask;
    }

    private async void OnSuccessCompletionCallbackAsync(string sagaLog)
    {
        _logger.LogError("The Test has resulted a success. log: {sagaLog}", Environment.NewLine + sagaLog);
        await Task.CompletedTask;
    }

    private async Task OnSagaCompletedAsync(object? _, SagaCompletionEventArgs e)
    {
        _logger.LogInformation($"Saga {e.SagaId} completed with status {e.Status}");
        await Task.CompletedTask;
    }

    #endregion

    
}
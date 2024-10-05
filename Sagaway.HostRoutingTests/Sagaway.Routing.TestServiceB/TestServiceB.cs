using Dapr.Client;
using Sagaway.Callback.Propagator;
using Sagaway.Routing.Tracking;

namespace Sagaway.Routing.TestServiceB;

public class TestServiceB(
    ILogger<TestServiceB> logger,
    DaprClient daprClient,
    ISignalRPublisher signalRPublisher,
    ISagawayCallbackMetadataProvider callbackMetadataProvider) : ITestServiceB
{

    public async Task InvokeAsync(CallChainInfo request)
    {
        logger.LogInformation("Service B invoked. Test {testName} Call Chain instructions:{OriginalCallInstructions}  Call Chain history: {ActualCallChain}, Left call chain: {callInstructions}",
            request.TestName, request.OriginalCallInstructions, request.CallChainHistory, request.CallInstructions);
        var nextCall = request.PopNextInstruction();

        logger.LogInformation("Service B will call {nextCall}", string.IsNullOrEmpty(nextCall) ? "back" : nextCall);

        if (string.IsNullOrEmpty(nextCall)) //no more downstream calls
        {
            await daprClient.InvokeBindingAsync(callbackMetadataProvider.CallbackBindingName, "create", request);
            return;
        }

        //else calling downstream

        await daprClient.InvokeBindingAsync(nextCall + "queue", "create", request,
            callbackMetadataProvider.GetCallbackMetadata(nameof(OnResultAsync), typeof(ITestServiceB),
                "TestServiceBQueue"));
    }

    public async Task OnResultAsync(CallChainInfo result)
    {

        logger.LogInformation("Service B received result. Test {testName}  Call Chain instructions:{OriginalCallInstructions}  Actual test call chain so far: {CallChainHistory}, Left call chain: {callInstructions}",
            result.TestName, result.OriginalCallInstructions, result.CallChainHistory, result.CallInstructions);

        var nextCall = result.PopNextInstruction();

        logger.LogInformation("Service B will call {nextCall}", string.IsNullOrEmpty(nextCall) || nextCall.ToLower() == "callback" ? "back" : nextCall);

        if (string.IsNullOrEmpty(nextCall)) //no more calls, we back to the first call, call to the test with SignalR
        {
            await signalRPublisher.PublishMessageToSignalRAsync(result.TestName, result.CallChainHistory);
        }

        if (nextCall.ToLower() == "callback") // calling back
        {
            await daprClient.InvokeBindingAsync(callbackMetadataProvider.CallbackBindingName, "create", result);
            return;
        }


        //else calling downstream

        await daprClient.InvokeBindingAsync(nextCall + "queue", "create", result,
            callbackMetadataProvider.GetCallbackMetadata(nameof(OnResultAsync), typeof(ITestServiceB),
                "TestServiceBQueue"));
    }
}
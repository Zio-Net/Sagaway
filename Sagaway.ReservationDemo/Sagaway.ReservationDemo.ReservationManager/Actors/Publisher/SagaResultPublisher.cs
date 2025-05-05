using System.Text.Json;
using Dapr.Client;

namespace Sagaway.ReservationDemo.ReservationManager.Actors.Publisher;

public class SagaResultPublisher : ISagaResultPublisher
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<SagaResultPublisher> _logger;

    // ReSharper disable once ConvertToPrimaryConstructor
    public SagaResultPublisher(DaprClient daprClient, ILogger<SagaResultPublisher> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    public async Task PublishMessageToSignalRAsync(SagaResult result)
    {
        try
        {
            var metadata = new Dictionary<string, string>
            {
                { "ttlInSeconds", "900" } // 15 minutes TTL
            };

            var key = result.ReservationId.ToString();

            //first, try to fetch the existing state
            var existingState = await _daprClient.GetStateAsync<string>(
                "statestore",
                $"saga-log-{key}");
            
            existingState = existingState == null ? string.Empty : 
                existingState + Environment.NewLine + Environment.NewLine +
                "***************************************************************************" + 
                Environment.NewLine + Environment.NewLine;

            result.Log = existingState + result.Log;

            await _daprClient.SaveStateAsync(
                "statestore",
                $"saga-log-{key}",
                result.Log,
                metadata: metadata);

            _logger.LogInformation($"Saved saga log for reservation {key} with 15 minutes TTL");

            var jsonSerializationOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var callbackRequest = JsonSerializer.Serialize(result, jsonSerializationOptions);
            var jsonDocument = JsonDocument.Parse(callbackRequest);

            var argument = new Argument
            {
                Sender = "dapr",
                Text = jsonDocument
            };

            SignalRMessage message = new()
            {
                UserId = "DemoUser",
                Target = "SagaCompleted",
                Arguments = [argument]
            };

            _logger.LogInformation("publishing message to SignalR: {argumentText}", argument.Text);

            await _daprClient.InvokeBindingAsync("reservationcallback", "create",
                message); //sending through dapr to the signalR Hub
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing message to SignalR: {Message}", ex.Message);
        }
    }
}
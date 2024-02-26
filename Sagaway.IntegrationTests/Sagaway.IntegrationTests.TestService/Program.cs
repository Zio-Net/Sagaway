using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sagaway.Callback.Propagator;
using Sagaway.IntegrationTests.TestService;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders(); 
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Register DaprClient that support Sagaway context propagator
builder.Services.AddControllers().AddDaprWithSagawayContextPropagator().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddSagawayContextPropagator();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost("/test-queue", async (
        [FromBody] ServiceTestInfo request,
        [FromServices] ILogger<Program> logger,
        [FromServices] ICallbackQueueNameProvider callbackQueueNameProvider,
        [FromServices] DaprClient daprClient) =>
    {
        logger.LogInformation("Received test request: {request}", request);

        await Task.Delay(TimeSpan.FromSeconds(request.DelayOnCallInSeconds));

        if (!request.ShouldSucceed)
        {
            logger.LogError("Test {request.CallId} did nothing and return a failure as requested",
                request.CallId);

            if (request.ShouldReturnCallbackResult)
            {
                logger.LogInformation("Sending callback failure result for test {request.CallId}",
                                       request.CallId);
                await daprClient.InvokeBindingAsync(callbackQueueNameProvider.CallbackQueueName, "create", false);
            }

            return Results.Ok();
        }

        var result = true;

        // Set time to live for 10 minutes to reset the state for demo purposes
        var metadata = new Dictionary<string, string>
        {
            { "ttlInSeconds", "600" } // TTL set for 10 minutes
        };

        try
        {
            if (request.IsReverting)
            {
                await daprClient.DeleteStateAsync("statestore", request.CallId,
                    new StateOptions()
                    {
                        Consistency = ConsistencyMode.Strong
                    });
            }
            else
            {
                await daprClient.SaveStateAsync("statestore", request.CallId, request.CallId,
                    new StateOptions()
                    {
                        Consistency = ConsistencyMode.Strong
                    }, metadata);
            }
        }
        catch (Exception e)
        {
            result = false;
            logger.LogError(e, "Failed to do {action} for call id: {TestCallId} ", request.IsReverting ? "revert" : "set", request.CallId);
        }

        // Send the response to the response queue
        await daprClient.InvokeBindingAsync(callbackQueueNameProvider.CallbackQueueName, "create", result);

        return Results.Ok();
    })
    .WithName("CarReservationQueue")
    .WithOpenApi();


app.MapGet("/test/{callId}", async ([FromRoute] Guid callId, [FromServices] DaprClient daprClient, [FromServices] ILogger<Program> logger) =>
    {
        logger.LogInformation($"Fetching test status for caller id: {callId}");

        try
        {
            var reservationState = await daprClient.GetStateAsync<Guid?>("statestore", callId.ToString());

            if (reservationState == null)
            {
                logger.LogWarning($"CallId: {callId} not found.");
                return Results.NotFound(new { Message = $"CallId: {callId} not found." });
            }

            return Results.Ok(callId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error fetching callId: {callId}");
            return Results.Problem($"An error occurred while fetching the callId: {callId}");
        }
    })
    .WithName("GetTestCallIdStatus")
    .WithOpenApi(); // This adds the endpoint to OpenAPI/Swagger documentation if enabled

app.UseSagawayContextPropagator();
app.MapControllers();
app.MapSubscribeHandler();
app.UseRouting();

app.Run();
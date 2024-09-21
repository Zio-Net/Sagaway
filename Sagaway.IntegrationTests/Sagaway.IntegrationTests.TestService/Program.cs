using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sagaway.Callback.Propagator;
using Sagaway.IntegrationTests.TestService;
using System.Globalization;
using System.Net;
using Dapr;
using OpenTelemetry.Resources;
using Polly;
using OpenTelemetry.Trace;


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


builder.Services.AddOpenTelemetry().WithTracing(tracing =>
{
    tracing.AddAspNetCoreInstrumentation(options =>
    {
        options.Filter = (httpContext) => httpContext.Request.Path != "/healthz";
    }); 
    tracing.AddHttpClientInstrumentation();
    tracing.AddZipkinExporter(options =>
    {
        options.Endpoint = new Uri("http://zipkin:9411/api/v2/spans");
    }).SetResourceBuilder(
        ResourceBuilder.CreateDefault().AddService("TestService"));
});

builder.Services.AddHealthChecks();
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
    [FromHeader(Name = "x-sagaway-dapr-message-dispatch-time")]
    string messageDispatchTimeHeader,
    [FromServices] ILogger<Program> logger,
    [FromServices] ICallbackBindingNameProvider callbackBindingNameProvider,
    [FromServices] DaprClient daprClient) =>
{
    logger.LogInformation("Received test request: {request}", request);

    var reservationId = request.CallId;

    await Task.Delay(TimeSpan.FromSeconds(request.DelayOnCallInSeconds));

    // Parse the dispatch time from the header
    var decodedMessageDispatchTimeHeader = WebUtility.UrlDecode(messageDispatchTimeHeader);

    if (DateTime.TryParseExact(decodedMessageDispatchTimeHeader, "o", CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var messageDispatchTime))
    {
        logger.LogInformation("Reservation: {reservationId}, Message dispatch time: {messageDispatchTime}",
            reservationId, messageDispatchTime);
    }
    else
    {
        logger.LogError("Reservation: {reservationId}, Invalid message dispatch time format.",
            reservationId);
        return;
    }

    if (!request.ShouldSucceed)
    {
        logger.LogError("Test {request.CallId} did nothing and return a failure as requested",
            request.CallId);

        if (request.ShouldReturnCallbackResult)
        {
            logger.LogInformation("Sending callback failure result for test {request.CallId}",
                request.CallId);
            await daprClient.InvokeBindingAsync(callbackBindingNameProvider.CallbackBindingName, "create", false);
        }

        return;
    }

    // Set time to live for 2 seconds for delete, and 10 minutes to reset the test state for house cleaning
    var metadata = new Dictionary<string, string>
    {
        { "ttlInSeconds", request.IsReverting ? "2" : "600" }
    };


    var retryPolicy = Policy
        .Handle<Exception>()
        .RetryAsync(3, (exception, retryCount) =>
        {
            logger.LogWarning(
                $"Retrying save state operation due to exception: {exception.Message}. Retry count: {retryCount}");
        });

    var result = true;
    try
    {
        SaveState? savedState;
        string? etag = null;

        await retryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                (savedState, etag) =
                    await daprClient.GetStateAndETagAsync<SaveState>("statestore", reservationId);

            }
            catch (DaprException ex) when (ex.HResult == 404) //not found
            {

            }

            savedState = new()
            {
                CallerId = request.CallId,
                MessageDispatchTime = messageDispatchTime
            };

            var saveResult = await daprClient.TrySaveStateAsync("statestore", request.CallId, savedState,
                etag, new StateOptions()
                {
                    Consistency = ConsistencyMode.Strong
                }, metadata);

            if (!saveResult)
            {
                throw new Exception("Failed to save state");
            }
        });
    }
    catch (Exception e)
    {
        result = false;
        logger.LogError(e, "Failed to do {action} for call id: {TestCallId} ",
            request.IsReverting ? "revert" : "set", request.CallId);
    }

    if (request.ShouldReturnCallbackResult)
    {
        logger.LogInformation("Sending callback result for test {request.CallId}",
            request.CallId);
        await daprClient.InvokeBindingAsync(callbackBindingNameProvider.CallbackBindingName, "create", result);
    }
}).ExcludeFromDescription();
    
    


app.MapGet("/test/{callId}", async ([FromRoute] Guid callId, [FromServices] DaprClient daprClient, [FromServices] ILogger<Program> logger) =>
    {
        logger.LogInformation($"Fetching test status for caller id: {callId}");

        try
        {
            var reservationState = await daprClient.GetStateAsync<SaveState>("statestore", callId.ToString());
            if (reservationState == null)
            {
                return Results.Ok(false);
            }
            return Results.Ok(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error fetching callId: {callId}");
            return Results.Ok(false);
        }
    })
    .WithName("GetTestCallIdStatus")
    .WithOpenApi(); // This adds the endpoint to OpenAPI/Swagger documentation if enabled

app.MapHealthChecks("/healthz");
app.UseSagawayContextPropagator();
app.MapControllers();
app.MapSubscribeHandler();
app.UseRouting();

app.Run();
using System.Text.Json.Serialization;
using System.Text.Json;
using Dapr.Actors;
using Microsoft.AspNetCore.Mvc;
using Dapr.Actors.Client;
using Dapr.Client;
using OpenTelemetry.Resources;
using Sagaway.Callback.Router;
using Sagaway.OpenTelemetry;
using OpenTelemetry.Trace;
using Sagaway.IntegrationTests.TestSubSagaCommunicationService;

var builder = WebApplication.CreateBuilder(args);


builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddDaprClient();

builder.Services.AddHealthChecks();

// Register DaprClient
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddActors(options =>
{
    // Register actor types and configure actor settings
    options.Actors.RegisterActor<MainSagaActor>();
    options.Actors.RegisterActor<SubSagaActor>();
    // Configure default settings
    options.ActorIdleTimeout = TimeSpan.FromMinutes(3);
    options.ActorScanInterval = TimeSpan.FromSeconds(60);
    options.DrainOngoingCallTimeout = TimeSpan.FromSeconds(120);
    options.DrainRebalancedActors = true;
    options.JsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        PropertyNameCaseInsensitive = true
    };
});

//add and configure open telemetry to trace all
builder.Services.AddSagawayOpenTelemetry(configureTracerProvider =>
{
    configureTracerProvider
        .AddAspNetCoreInstrumentation(options =>
        { options.Filter = (httpContext) => httpContext.Request.Path != "/healthz"; }) // Instruments incoming requests
        .AddHttpClientInstrumentation() // Instrument outgoing HTTP requests
        .AddZipkinExporter(options =>
        {
            options.Endpoint = new Uri("http://zipkin:9411/api/v2/spans");
        }).SetResourceBuilder(
            ResourceBuilder.CreateDefault().AddService("IntegrationTests"))
        .SetSampler(new AlwaysOnSampler()); // Collect all samples. Adjust as necessary for production.
}, "TestSubSagaCommunicationService");

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod(); // This includes OPTIONS
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/healthz");

//enable callback router
app.UseSagawayCallbackRouter("test-response-queue");

app.MapPost("/run-test", async (
    [FromServices] IActorProxyFactory actorProxyFactory,
    [FromServices] ILogger<Program> logger,
    [FromServices] DaprClient daprClient) =>
{
    try
    {
        logger.LogInformation("Starting sub-saga test");

        var proxy = actorProxyFactory.CreateActorProxy<IMainSagaActor>(
          new ActorId("main_" + Guid.NewGuid()), "MainSagaActor");

        await proxy.RunTestAsync();

        var startTime = DateTime.UtcNow;  // Take the start time
        const int timeoutInSeconds = 10;  // Timeout duration

        while (true)
        {
            // Check if the time elapsed exceeds the timeout duration
            if ((DateTime.UtcNow - startTime).TotalSeconds > timeoutInSeconds)
            {
                logger.LogError("Sub-saga test timed out after {timeoutInSeconds} seconds", timeoutInSeconds);
                return Results.Ok("Test Timed Out");
            }

            var endStatus = await proxy.GetTestResultAsync();
            if (endStatus == TestResult.Running)
            {
                await Task.Delay(500);
                continue;
            }

            if (endStatus == TestResult.Failed)
            {
                logger.LogError("Sub-saga test failed");
                return Results.Ok("Test Failed");
            }

            logger.LogInformation("Test succeeded");
            return Results.Ok("Test Succeeded");
        }
    }
    catch (Exception e)
    {
        logger.LogError(e, "Error running sub-saga test");
        throw;
    }
})
.WithName("run-test")
.WithOpenApi();


app.UseCors("AllowAll");

app.MapSubscribeHandler();
app.UseRouting();
app.MapActorsHandlers();

app.Run();


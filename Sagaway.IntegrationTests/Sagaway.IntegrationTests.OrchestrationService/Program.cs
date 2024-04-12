using System.Text.Json.Serialization;
using System.Text.Json;
using Dapr.Actors;
using Microsoft.AspNetCore.Mvc;
using Dapr.Actors.Client;
using Dapr.Client;
using Sagaway.Callback.Router;
using Sagaway.IntegrationTests.OrchestrationService;
using Sagaway.IntegrationTests.OrchestrationService.Actors;
using Sagaway.OpenTelemetry;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);


builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddSingleton<SignalRService>()
#pragma warning disable CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
#pragma warning disable CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
    .AddHostedService(sp => sp.GetService<SignalRService>()!)
#pragma warning restore CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
#pragma warning restore CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
    .AddSingleton<IHubContextStore>(sp => sp.GetService<SignalRService>()!)
    .AddDaprClient();

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
    options.Actors.RegisterActor<TestActor>();
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
        .AddAspNetCoreInstrumentation() // Instruments incoming requests
        .AddHttpClientInstrumentation() // Instrument outgoing HTTP requests
        .AddZipkinExporter(options =>
        {
            options.Endpoint = new Uri("http://localhost:9411/api/v2/spans");
        })
        .SetSampler(new AlwaysOnSampler()); // Collect all samples. Adjust as necessary for production.
}, "OrchestrationService");

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

builder.Services.AddHostedService<SignalRService>();

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
        HttpContext context,
        [FromServices] IActorProxyFactory actorProxyFactory,
        [FromServices] ILogger < Program > logger,
        [FromServices] DaprClient daprClient,
        [FromBody] TestInfo? testInfo) =>
{

    if (string.IsNullOrEmpty(testInfo?.TestName))
    {
        logger.LogError("Test name is required");
        return Results.BadRequest("Test name is required");
    }

    testInfo.ServiceACall ??= new ();
    testInfo.ServiceBCall ??= new ();
    testInfo.ServiceARevert ??= new ();
    testInfo.ServiceBRevert ??= new ();

    logger.LogInformation("Starting test {TestName}", testInfo.TestName);

    var actorId = testInfo.Id.ToString("D");
    var proxy = actorProxyFactory.CreateActorProxy<ITestActor>(
        new ActorId(actorId), "TestActor");

    //a hack for testing
    var metadata = new Dictionary<string, string>
    {
        { "ttlInSeconds", "300" }
    };

    await daprClient.SaveStateAsync("statestore", actorId, testInfo, null, metadata);

    await proxy.RunTestAsync(testInfo);

    return Results.Ok();
})
.WithName("run-test")
.WithOpenApi();

app.MapPost("/demo1", async () =>
{
    await Task.Delay(100);
    return Results.Ok("Post");
});

app.MapPut("/demo2", async () =>
{
    await Task.Delay(100);
    return Results.Ok("Put");
});

app.MapDelete("/demo3", async () =>
{
    await Task.Delay(100);
    return Results.Ok("Delete");
});

app.MapGet("/demo4", async () =>
{
    await Task.Delay(100);
    return Results.Ok("Get");
});

app.MapPost("/negotiate", async (
    [FromServices] IHubContextStore store,
    [FromServices] ILogger<Program> logger) =>
{
    var accountManagerCallbackHubContext = store.AccountManagerCallbackHubContext;

    logger.LogInformation("MessageHubNegotiate: SignalR negotiate for user");

    var negotiateResponse = await accountManagerCallbackHubContext!.NegotiateAsync(new()
    {
        UserId = "testUser", //user,
        EnableDetailedErrors = true
    });

    return Results.Json(new Dictionary<string, string>()
    {
        { "url", negotiateResponse.Url! },
        { "accessToken", negotiateResponse.AccessToken! }
    });
});

app.UseCors("AllowAll");

app.MapControllers();
app.MapSubscribeHandler();
app.UseRouting();
app.MapActorsHandlers();

app.Run();
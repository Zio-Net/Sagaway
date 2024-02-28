using System.Text.Json.Serialization;
using System.Text.Json;
using Dapr.Actors;
using Microsoft.AspNetCore.Mvc;
using Dapr.Actors.Client;
using Sagaway.Callback.Router;
using Sagaway.IntegrationTests.OrchestrationService;
using Sagaway.IntegrationTests.OrchestrationService.Actors;

var builder = WebApplication.CreateBuilder(args);


builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddSingleton<SignalRService>()
#pragma warning disable CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
#pragma warning disable CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
    .AddHostedService(sp => sp.GetService<SignalRService>())
#pragma warning restore CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
#pragma warning restore CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
    .AddSingleton<IHubContextStore>(sp => sp.GetService<SignalRService>()!)
    .AddDaprClient();


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
    options.ActorIdleTimeout = TimeSpan.FromMinutes(10);
    options.ActorScanInterval = TimeSpan.FromSeconds(35);
    options.DrainOngoingCallTimeout = TimeSpan.FromSeconds(35);
    options.DrainRebalancedActors = true;

    options.JsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        PropertyNameCaseInsensitive = true
    };
});

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

//enable callback router
app.UseSagawayCallbackRouter("response-queue");

app.MapPost("/run-test", async (
        [FromServices] IActorProxyFactory actorProxyFactory,
        [FromServices] ILogger < Program > logger,
        [FromBody] TestInfo? testInfo) =>
{

    if (string.IsNullOrEmpty(testInfo?.TestName))
    {
        logger.LogError("Test name is required");
        return Results.BadRequest("Test name is required");
    }

    logger.LogInformation("Starting test {TestName}", testInfo.TestName);

    var proxy = actorProxyFactory.CreateActorProxy<ITestActor>(
        new ActorId(testInfo.Id.ToString("D")), "TestActor");
    
    await proxy.RunTestAsync(testInfo);

    return Results.Ok();
})
.WithName("run-test")
.WithOpenApi();


app.MapPost("/negotiate", async (
    [FromServices] IHubContextStore store,
    [FromServices] ILogger<Program> logger) =>
{
    var accountManagerCallbackHubContext = store.AccountManagerCallbackHubContext;

    logger.LogInformation($"MessageHubNegotiate: SignalR negotiate for user: Test");

    var negotiateResponse = await accountManagerCallbackHubContext!.NegotiateAsync(new()
    {
        UserId = "Test", //user,
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
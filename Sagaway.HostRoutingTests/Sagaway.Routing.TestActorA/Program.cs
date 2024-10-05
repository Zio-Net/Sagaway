using System.Text.Json.Serialization;
using System.Text.Json;
using Dapr.Actors;
using Microsoft.AspNetCore.Mvc;
using Dapr.Actors.Client;
using OpenTelemetry.Trace;
using Sagaway.Callback.Router;
using Sagaway.OpenTelemetry;
using Sagaway.Routing.TestActorA;
using Sagaway.Routing.Tracking;
using Sagaway.Callback.Propagator;

var builder = WebApplication.CreateBuilder(args);


builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Register DaprClient
builder.Services.AddDaprWithSagawayContextPropagator().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddActors(options =>
{
    // Register actor types and configure actor settings
    options.Actors.RegisterActor<TestActorA>();

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

builder.Services.AddSagawayOpenTelemetry(configureTracerProvider =>
{
    configureTracerProvider
        .AddAspNetCoreInstrumentation(options =>
        { options.Filter = (httpContext) => httpContext.Request.Path != "/healthz"; }) // Instruments incoming requests
        .AddHttpClientInstrumentation() // Instrument outgoing HTTP requests
        .AddConsoleExporter()
        .AddZipkinExporter(options =>
        {
            options.Endpoint = new Uri("http://zipkin:9411/api/v2/spans");
        })
        .SetSampler(new AlwaysOnSampler()); // Collect all samples. Adjust as necessary for production.
}, "TestActorA");

builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.RegisterTracking();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//enable callback router
app.UseSagawayCallbackRouter("TestActorAQueue", async (
        [FromBody] CallChainInfo request,
        [FromServices] IActorProxyFactory actorProxyFactory,
        [FromServices] ILogger<Program> logger) =>
{
    logger.LogInformation("Received test request: {request}", request);

    var actorAProxy = actorProxyFactory.CreateActorProxy<ITestActorA>(new ActorId("TestActorA_" + Guid.NewGuid()), "TestActorA");
    await actorAProxy.InvokeAsync(request);
})
.WithName("TestActorA")
.WithOpenApi();


app.MapHealthChecks("/healthz");
app.MapControllers();
app.MapSubscribeHandler();
app.UseRouting();

app.MapActorsHandlers();

app.Run();
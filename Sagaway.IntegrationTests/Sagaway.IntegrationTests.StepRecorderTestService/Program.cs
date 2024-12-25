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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);


builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

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
    options.Actors.RegisterActor<SimpleSagaActor>();
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
            ResourceBuilder.CreateDefault().AddService("StepRecorderTest"))
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
        [FromServices] DaprClient daprClient,
        [FromQuery] string? stepRecorderType) =>
{

   
    if (string.IsNullOrEmpty(stepRecorderType))
    {
        logger.LogError("stepRecorderType is required");
        return Results.BadRequest("Test name is required");
    }

    try
    {
        
        logger.LogInformation("Starting step recorder test: {stepRecorderType}", stepRecorderType);

        var actorId = Guid.NewGuid().ToString();
        var proxy = actorProxyFactory.CreateActorProxy<ISimpleSagaActor>(
            new ActorId(actorId), "SimpleSagaActor");

        //a hack for testing
        var metadata = new Dictionary<string, string>
        {
            { "ttlInSeconds", "300" }
        };

       
        await proxy.RunSagaAsync(stepRecorderType);

        return Results.Ok();
    }
    catch (Exception e)
    {
        logger.LogError(e, "Error running step recorder test: {stepRecorderType}", stepRecorderType);
        throw;
    }
})
.WithName("run-test")
.WithOpenApi();



app.UseCors("AllowAll");

app.MapControllers();
app.MapSubscribeHandler();
app.UseRouting();
app.MapActorsHandlers();

app.Run();
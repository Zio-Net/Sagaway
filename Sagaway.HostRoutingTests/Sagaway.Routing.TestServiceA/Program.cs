using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sagaway.Callback.Propagator;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Sagaway.Routing.TestServiceA;
using Sagaway.Routing.Tracking;
using Sagaway.Callback.Router;


var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Register DaprClient that support Sagaway context propagator
builder.Services.AddDaprWithSagawayContextPropagator().AddJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
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
        ResourceBuilder.CreateDefault().AddService("TestServiceA"));
});

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

app.UseSagawayCallbackRouter("TestServiceAQueue",
    async (
    [FromBody] CallChainInfo request,
    [FromServices] ILogger<Program> logger,
    [FromServices] ITestServiceA testServiceA,
    [FromServices] DaprClient daprClient) =>
{
    logger.LogInformation("Received test request: {request}", request);

    await testServiceA.InvokeAsync(request);

}).ExcludeFromDescription();


app.MapHealthChecks("/healthz");
app.UseSagawayContextPropagator();
app.MapSubscribeHandler();
app.UseRouting();

app.Run();
using System.Text.Json.Serialization;
using System.Text.Json;
using Dapr.Actors;
using Microsoft.AspNetCore.Mvc;
using SagaReservationDemo.ReservationManager.Actors;
using Dapr.Actors.Client;
using SagaReservationDemo.ReservationManager.Dto.ReservationDto;

var builder = WebApplication.CreateBuilder(args);


builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Register DaprClient
builder.Services.AddControllers().AddDapr().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddActors(options =>
{
    // Register actor types and configure actor settings
    options.Actors.RegisterActor<CarReservationActor>();
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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.MapPost("/reserve", async (
        [FromQuery] Guid? reservationId, 
        [FromQuery] string customerName, 
        [FromQuery] string carClass, 
        [FromServices] IActorProxyFactory actorProxyFactory,
        [FromServices] ILogger < Program > logger) =>
{

    if (reservationId == null || reservationId == Guid.Empty)
    {
        reservationId = Guid.NewGuid();
    }

    logger.LogInformation("Received car reservation request for {CarClass} from {CustomerName}",
               carClass, customerName);

    var proxy = actorProxyFactory.CreateActorProxy<ICarReservationActor>(
        new ActorId(reservationId.Value.ToString("D")), "CarReservationActor");
    
    var reservationInfo = new ReservationInfo
    {
        ReservationId = reservationId.Value,
        CustomerName = customerName,
        CarClass = carClass
    };

    await proxy.ReserveCarAsync(reservationInfo);

    return reservationInfo;
})
.WithName("Reserve")
.WithOpenApi();

app.MapPost("/cancel", async (
    [FromQuery] Guid reservationId,
    [FromServices] IActorProxyFactory actorProxyFactory,
    [FromServices] ILogger<Program> logger) =>
{
    logger.LogInformation("Received car reservation cancellation request for {ReservationId}",
               reservationId);

    await Task.CompletedTask;

    //todo: implement cancel with Dapr Workflow for comparison
})
    .WithName("Cancel")
    .WithOpenApi();

app.MapPost("/reservation-response-queue", async (
    [FromBody] ReservationOperationResult reservationOperationResult,
    [FromServices] IActorProxyFactory actorProxyFactory,
    [FromServices] ILogger<Program> logger) =>
{
    if (reservationOperationResult.ReservationId == Guid.Empty)
    {
        logger.LogError("Reservation response queue message does not contain a valid reservation id");
        return;
    }

    logger.LogInformation("Received reservation response for {ReservationId}", reservationOperationResult.ReservationId);

        var proxy = actorProxyFactory.CreateActorProxy<ICarReservationActor>(
        new ActorId(reservationOperationResult.ReservationId.ToString("D")), "CarReservationActor");

    var result = await proxy.HandleReservationActionResultAsync(reservationOperationResult);

    logger.LogInformation("Reservation response for {ReservationId} handled: {Result}",
        reservationOperationResult.ReservationId, result);
})
    .ExcludeFromDescription();

app.MapControllers();
app.MapSubscribeHandler();
app.UseRouting();
app.MapActorsHandlers();

app.Run();
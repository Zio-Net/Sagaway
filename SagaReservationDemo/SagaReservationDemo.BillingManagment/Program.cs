using System.Text.Json.Serialization;
using System.Text.Json;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using SagaReservationDemo.BillingManagement;

var builder = WebApplication.CreateBuilder(args);

var rnd = new Random();

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


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost("/billing-queue",  (
        [FromBody] BillingRequest request,
        [FromServices] ILogger<Program> logger,
        [FromServices] DaprClient daprClient) =>
{
    logger.LogInformation("Received car reservation request for {CarClass} from {CustomerName}",
        request.CarClass, request.CustomerName);

    var reservationId = request.ReservationId.ToString();

    switch (request.ActionType)
    {
        case ActionType.Charge:
            logger.LogInformation("Charging car class {CarClass} reservation id {reservationId} for {CustomerName}",
                request.CarClass, reservationId, request.CustomerName);
            break;
        case ActionType.Refund:
            logger.LogInformation("Compensating car class {CarClass} reservation id {reservationId} for {CustomerName}",
                               request.CarClass, reservationId, request.CustomerName);
            break;
        default:
            logger.LogError("Unknown action type {ActionType}", request.ActionType);
            break;
    }

})
    .WithName("CarReservationQueue")
    .WithOpenApi();


//api for get the billing status
app.MapGet("/billing-status/{reservationId}", ([FromRoute] Guid reservationId, [FromServices] ILogger<Program> logger, [FromServices] DaprClient daprClient) => 
{
    var randomNumber = rnd.Next(0, 6);
    var charged = randomNumber  < 4;
    var chargeCancelled = randomNumber == 4;

    logger.LogInformation("Billing status for reservation id {reservationId} is {charged}", 
        reservationId, charged);

    var billingState = new BillingState
    {
        Status = charged ? "Charged" :
            chargeCancelled ? "chargeCancelled" : "Not Charged"
    };

    return billingState;
})
    .WithName("BillingStatus")
    .WithOpenApi();

app.MapControllers();
app.MapSubscribeHandler();
app.UseRouting();

app.Run();

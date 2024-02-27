using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sagaway.Callback.Propagator;
using Sagaway.ReservationDemo.BookingManagement;
using System.Globalization;
using System.Net;

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

app.MapPost("/booking-queue", async (
        [FromBody] CarReservationRequest request,
        [FromHeader(Name = "x-sagaway-message-dispatch-time")] string messageDispatchTimeHeader,
        [FromServices] ILogger<Program> logger,
        [FromServices] ICallbackQueueNameProvider callbackQueueNameProvider,
        [FromServices] DaprClient daprClient) =>
    {
        logger.LogInformation("Received car reservation request for {CarClass} from {CustomerName}",
            request.CarClass, request.CustomerName);

        var reservationId = request.ReservationId.ToString();

        // Parse the dispatch time from the header
        var decodedMessageDispatchTimeHeader = WebUtility.UrlDecode(messageDispatchTimeHeader);

        if (DateTime.TryParseExact(decodedMessageDispatchTimeHeader, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var messageDispatchTime))
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

        var reservationOperationResult = new ReservationOperationResult()
        {
            ReservationId = request.ReservationId
        };

        var (reservationState, etag) = await daprClient.GetStateAndETagAsync<ReservationState>("statestore", reservationId);

        //supporting out-of-order message
        if (reservationState != null && messageDispatchTime < reservationState.ReservationStatusUpdateTime)
        {
            logger.LogInformation("Receive an out of order message. Ignoring. {CustomerName}",
                request.CustomerName);
            return;
        }

        reservationState ??= new ReservationState
        {
            Id = request.ReservationId,
            ReservationStatusUpdateTime = messageDispatchTime,
            CustomerName = request.CustomerName,
            IsReserved = false
        };

        var stateOptions = new StateOptions()
        {
            Consistency = ConsistencyMode.Strong,
        };


        switch (request.ActionType)
        {
            case ActionType.Reserve:
                await ReserveCarAsync();
                break;
            case ActionType.Cancel:
                await CancelCarReservationAsync();
                break;
            default:
                logger.LogError("Unknown action type {ActionType}", request.ActionType);
                break;
        }

        async Task ReserveCarAsync()
        {
            logger.LogInformation("Reserving car class {CarClass} for {CustomerName}", request.CarClass, request.CustomerName);

            reservationState.IsReserved = true;

            try
            {
                var result = await daprClient.TrySaveStateAsync("statestore", reservationId, 
                    reservationState, etag, stateOptions);

                logger.LogInformation("Car class {CarClass} {result} reserved for {CustomerName}", 
                    request.CarClass, result ? "has" : "failed to", request.CustomerName);
                reservationOperationResult.IsSuccess = result;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to reserve car class {CarClass} for {CustomerName}", request.CarClass, request.CustomerName);
                reservationOperationResult.IsSuccess = false;
            }

            // Send the response to the response queue
            await daprClient.InvokeBindingAsync(callbackQueueNameProvider.CallbackQueueName, "create", reservationOperationResult);
        }


        async Task CancelCarReservationAsync()
        {

            logger.LogInformation("Cancelling car class {CarClass} reservation id {reservationId} for {CustomerName}",
                request.CarClass, reservationId, request.CustomerName);

            reservationState.IsReserved = false;

            // TTL set for 5 minutes, this has the effect of deleting the entry
            // but only after the Saga is done, support for compensation
            var metadata = new Dictionary<string, string>
            {
                { "ttlInSeconds", "300" }
            };


            try
            {
                var result = await daprClient.TrySaveStateAsync("statestore", reservationId, reservationState, 
                    etag, stateOptions, metadata);

                reservationOperationResult.IsSuccess = result;
                logger.LogInformation("Reservation id {reservationId} {result} cancelled for {CustomerName}",
                    reservationId, result ? "has" : "failed to", request.CustomerName);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to cancel reservation id {reservationId} for {CustomerName}", reservationId,
                    request.CustomerName);
                reservationOperationResult.IsSuccess = false;
            }

            // Send the response to the response queue
            await daprClient.InvokeBindingAsync(callbackQueueNameProvider.CallbackQueueName, "create", reservationOperationResult);
        }
    })
    .WithName("CarReservationQueue")
    .WithOpenApi();


app.MapGet("/reservations/{reservationId}", async ([FromRoute] Guid reservationId, [FromServices] DaprClient daprClient, [FromServices] ILogger<Program> logger) =>
    {
        logger.LogInformation($"Fetching reservation status for reservation ID: {reservationId}");

        try
        {
            var reservationState = await daprClient.GetStateAsync<ReservationState>("statestore", reservationId.ToString());

            if (reservationState == null)
            {
                logger.LogWarning($"Reservation with ID: {reservationId} not found.");
                return Results.NotFound(new { Message = $"Reservation with ID: {reservationId} not found." });
            }

            return Results.Ok(reservationState);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error fetching reservation status for reservation ID: {reservationId}");
            return Results.Problem("An error occurred while fetching the reservation status. Please try again later.");
        }
    })
    .WithName("GetReservationStatus")
    .WithOpenApi(); // This adds the endpoint to OpenAPI/Swagger documentation if enabled

app.UseSagawayContextPropagator();
app.MapControllers();
app.MapSubscribeHandler();
app.UseRouting();

app.Run();
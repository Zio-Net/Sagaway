using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using SagaReservationDemo.BookingManagement;
using System.Text.Json.Serialization;
using Sagaway.Callback.Propagator;

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
        [FromServices] ILogger<Program> logger,
        [FromServices] ICallbackQueueNameProvider callbackQueueNameProvider,
        [FromServices] DaprClient daprClient) =>
    {
        logger.LogInformation("Received car reservation request for {CarClass} from {CustomerName}",
            request.CarClass, request.CustomerName);

        var reservationId = request.ReservationId.ToString();

        var reservationOperationResult = new ReservationOperationResult()
        {
            ReservationId = request.ReservationId,
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
            reservationOperationResult.Activity = "CarBooking";

            var reservationState = new ReservationState
            {
                CustomerName = request.CustomerName,
                Id = request.ReservationId
            };

            // Check if the car class is already reserved
            var state = await daprClient.GetStateAsync<ReservationState>("statestore", reservationId);

            if (state?.IsReserved ?? false)
            {
                logger.LogInformation("Reservation id is already reserved for {CustomerName}", request.CustomerName);

                reservationOperationResult.IsSuccess = true;

                // Send the response to the response queue
                await daprClient.InvokeBindingAsync(callbackQueueNameProvider.CallbackQueueName, "create", reservationOperationResult);
                return;
            }

            //else
            logger.LogInformation("Reserving car class {CarClass} for {CustomerName}", request.CarClass, request.CustomerName);

            reservationState.IsReserved = true;

            // Set time to live for 10 minutes to reset the state for demo purposes
            var metadata = new Dictionary<string, string>
            {
                { "ttlInSeconds", "600" } // TTL set for 10 minutes
            };

            try
            {
                await daprClient.SaveStateAsync("statestore", reservationId, reservationState,
                    new StateOptions()
                    {
                        Consistency = ConsistencyMode.Strong
                    }, metadata);

                logger.LogInformation("Car class {CarClass} reserved for {CustomerName}", request.CarClass, request.CustomerName);
                reservationOperationResult.IsSuccess = true;
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
           reservationOperationResult.Activity = "cancellingCarBooking";

           var state = await daprClient.GetStateAsync<ReservationState>("statestore", reservationId);

            if (state?.IsReserved ?? false)
            {
                logger.LogInformation("Cancelling car class {CarClass} reservation id {reservationId} for {CustomerName}",
                    request.CarClass, reservationId, request.CustomerName);

                try
                {
                    await daprClient.DeleteStateAsync("statestore", reservationId);
                    reservationOperationResult.IsSuccess = true;
                    logger.LogInformation("Reservation id {reservationId} cancelled for {CustomerName}", reservationId, request.CustomerName);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to cancel reservation id {reservationId} for {CustomerName}", reservationId, request.CustomerName);
                    reservationOperationResult.IsSuccess = false;
                }
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
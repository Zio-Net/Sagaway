using System.Text.Json.Serialization;
using System.Text.Json;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using Sagaway.Callback.Propagator;
using Sagaway.ReservationDemo.InventoryManagement;

var builder = WebApplication.CreateBuilder(args);


builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Register DaprClient
builder.Services.AddControllers().AddDapr(b => b.AddSagawayContextPropagator()).AddJsonOptions(options =>
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


app.MapPost("/inventory-queue", async (
        [FromBody] CarInventoryRequest request,
        [FromServices] ILogger<Program> logger,
        [FromServices] ICallbackQueueNameProvider callbackQueueNameProvider,
        [FromServices] DaprClient daprClient) =>
{
    logger.LogInformation("Received car inventory request for {CarClass} order id: {orderId}",
        request.CarClass, request.OrderId);

    var orderId = request.OrderId.ToString();

    var reservationOperationResult = new ReservationOperationResult()
    {
        ReservationId = request.OrderId,
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
        reservationOperationResult.Activity = "inventoryReserving";

        // Check if the car class is already reserved
        var (state, etag) = await daprClient.GetStateAndETagAsync<ReservationState>("statestore", orderId);

        //supporting idempotency
        if (state?.IsReserved ?? false)
        {
            logger.LogInformation("Car class {CarClass} is already reserved", request.CarClass);

            reservationOperationResult.IsSuccess = true;
            // Send the response to the response queue
            await daprClient.InvokeBindingAsync(callbackQueueNameProvider.CallbackQueueName, "create", reservationOperationResult);
            return;
        }

        //get the number of car reserved in the class
        var (carClassState, carClassEtag) = await daprClient.GetStateAndETagAsync<int>("statestore", request.CarClass);

        //demonstrating 2 cars per class limits that can be reserved
        if (carClassState == 2)
        {
            logger.LogInformation("Car class {CarClass} is not available", request.CarClass);

            reservationOperationResult.IsSuccess = false;

            // Send the response to the response queue
            await daprClient.InvokeBindingAsync(callbackQueueNameProvider.CallbackQueueName, "create", reservationOperationResult);
            return;
        }

        //else

        logger.LogInformation("Car class {CarClass} is available", request.CarClass);

        var reservationState = new ReservationState
        {
            Id = request.OrderId,
            IsReserved = true,
            CarClass = request.CarClass
        };

        //we need to update the number of cars reserved in the class and the reservation state
        //in a transactional way

        // Set time to live for 10 minutes to reset the state for demo purposes
        var metadata = new Dictionary<string, string>
        {
            { "ttlInSeconds", "600" } // TTL set for 10 minutes
        };

        var carClassStateUpdate = new StateTransactionRequest(request.CarClass, 
            System.Text.Encoding.UTF8.GetBytes((carClassState + 1).ToString()),
            StateOperationType.Upsert, carClassEtag, metadata);

        var reservationStateUpdate = new StateTransactionRequest(orderId, 
                       JsonSerializer.SerializeToUtf8Bytes(reservationState),
                                  StateOperationType.Upsert, etag, metadata);

        var transactionOperations = new List<StateTransactionRequest>()
        {
            carClassStateUpdate,
            reservationStateUpdate
        };

        try
        {
            await daprClient.ExecuteStateTransactionAsync("statestore", transactionOperations);
            logger.LogInformation("Car class {CarClass} reserved for order id {orderId} - {CarClassState} cars reserved",
                               request.CarClass, request.OrderId, carClassState + 1);

            reservationOperationResult.IsSuccess = true;
            await daprClient.InvokeBindingAsync(callbackQueueNameProvider.CallbackQueueName, "create", reservationOperationResult);

        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to reserve car class {CarClass} for order id {orderId}", request.CarClass, request.OrderId);
            reservationOperationResult.IsSuccess = false;
            await daprClient.InvokeBindingAsync(callbackQueueNameProvider.CallbackQueueName, "create", reservationOperationResult);
        }
    }


    async Task CancelCarReservationAsync()
    {
        reservationOperationResult.Activity = "inventoryCancelling";
        //check if there is a reservation with the order id
        var state = await daprClient.GetStateAsync<ReservationState>("statestore", orderId);

        if (!state?.IsReserved ?? true) //not reserved
        {
            logger.LogInformation("Car class {CarClass} is not reserved for order id: {orderId}", 
                request.CarClass, orderId);

            reservationOperationResult.IsSuccess = true;

            await daprClient.InvokeBindingAsync(callbackQueueNameProvider.CallbackQueueName, "create", reservationOperationResult);
            return;
        }

        //else
        logger.LogInformation("Car class {CarClass} is reserved for order id: {orderId}, canceling reservation",
            request.CarClass, orderId);

        // Cancel the reservation
        state.IsReserved = false;

        //get the number of car reserved in the class
        var (carClassState, carClassEtag) = await daprClient.GetStateAndETagAsync<int>("statestore", request.CarClass);

        //we need transactional update to the number of cars reserved in the class and the reservation state
        var carClassStateUpdate = new StateTransactionRequest(request.CarClass,
            System.Text.Encoding.UTF8.GetBytes((carClassState - 1).ToString()),
            StateOperationType.Delete, carClassEtag);

        var reservationStateUpdate = new StateTransactionRequest(orderId,
            JsonSerializer.SerializeToUtf8Bytes(carClassState),
            StateOperationType.Delete);

        var transactionOperations = new List<StateTransactionRequest>()
        {
            carClassStateUpdate,
            reservationStateUpdate
        };

        try
        {
            await daprClient.ExecuteStateTransactionAsync("statestore", transactionOperations);
            logger.LogInformation("Car class {CarClass} for order id {orderId} canceled - {CarClassState} cars reserved",
                request.CarClass, request.OrderId, carClassState - 1);

            reservationOperationResult.IsSuccess = true;

            await daprClient.InvokeBindingAsync(callbackQueueNameProvider.CallbackQueueName, "create", reservationOperationResult);

        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to cancel car class {CarClass} for order id {orderId}", request.CarClass, request.OrderId);
            reservationOperationResult.IsSuccess = false;
            await daprClient.InvokeBindingAsync(callbackQueueNameProvider.CallbackQueueName, "create", reservationOperationResult);
        }
    }
})
    .WithName("CarReservationQueue")
    .WithOpenApi();

app.MapGet("/reservation-state/{orderId}", async (
        [FromRoute] Guid orderId, 
        [FromServices] DaprClient daprClient,
        [FromServices] ILogger<Program> logger) =>
{
    try
    {
        // Attempt to fetch the reservation state for the given order ID from the Dapr state store
        var reservationState = await daprClient.GetStateAsync<ReservationState>("statestore", orderId.ToString());

        if (reservationState == null)
        {
            logger.LogInformation("No reservation found for order id: {OrderId}", orderId);
            // Return a NotFound result if no reservation state is found
            return Results.NotFound(new { Message = $"No reservation found for order id: {orderId}" });
        }

        // If a reservation state is found, return it
        return Results.Ok(reservationState);
    }
    catch (Exception e)
    {
        logger.LogError(e, "Failed to retrieve reservation state for order id {OrderId}", orderId);
        // Return an InternalServerError result in case of exceptions
        return Results.Problem("An error occurred while retrieving the reservation state.");
    }
})
.WithName("GetReservationState")
.WithOpenApi();

app.UseSagawayContextPropagator();
app.MapControllers();
app.MapSubscribeHandler();
app.UseRouting();

app.Run();
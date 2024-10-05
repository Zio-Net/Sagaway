using System.Text.Json.Serialization;
using System.Text.Json;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using Sagaway.Callback.Propagator;
using Sagaway.ReservationDemo.InventoryManagement;
using System.Globalization;
using System.Net;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;

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

builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
        ResourceBuilder.CreateDefault().AddService("InventoryManagementService"));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.MapPost("/inventory-queue", async (
        [FromBody] CarInventoryRequest request,
        [FromHeader(Name = "x-sagaway-dapr-message-dispatch-time")] string messageDispatchTimeHeader,
        [FromServices] ILogger<Program> logger,
        [FromServices] ICallbackBindingNameProvider callbackBindingNameProvider,
        [FromServices] DaprClient daprClient) =>
{
    logger.LogInformation("Received car inventory request for {CarClass} order id: {orderId}",
        request.CarClass, request.OrderId);

    var orderId = request.OrderId.ToString();

    // Parse the dispatch time from the header
    var decodedMessageDispatchTimeHeader = WebUtility.UrlDecode(messageDispatchTimeHeader);

    if (DateTime.TryParseExact(decodedMessageDispatchTimeHeader, "o", CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var messageDispatchTime))
    {
        logger.LogInformation("Order: {orderId}, Message dispatch time: {messageDispatchTime}",
            orderId, messageDispatchTime);
    }
    else
    {
        logger.LogError("Order: {orderId}, Invalid message dispatch time format.",
            orderId);
        return;
    }

    var reservationOperationResult = new ReservationOperationResult()
    {
        ReservationId = request.OrderId,
    };

    ReservationState? reservationState = null;
    string? orderIdEtag = null;

    try
    {
        (reservationState, orderIdEtag) = await daprClient.GetStateAndETagAsync<ReservationState>("statestore", orderId);
    }
    catch (Exception e)
    {
       logger.LogInformation(e, "Order id {orderId} is not exist", orderId);
    }

    //supporting out-of-order message
    if (reservationState != null && messageDispatchTime < reservationState.LastUpdateTime)
    {
        logger.LogInformation("Receive an out of order message. Ignoring. {orderId}",
            orderId);
        return;
    }

    reservationState ??= new ReservationState()
    {
        Id = request.OrderId,
        LastUpdateTime = messageDispatchTime,
        CarClass = request.CarClass,
        IsReserved = false
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
        //get the number of car reserved in the class
        var (carClassState, carClassEtag) = await daprClient.GetStateAndETagAsync<int>("statestore", request.CarClass);

        //demonstrating 2 cars per class limits that can be reserved
        if (carClassState == 2)
        {
            logger.LogInformation("Car class {CarClass} is not available", request.CarClass);

            reservationOperationResult.IsSuccess = false;

            // Send the response to the response binding
            await daprClient.InvokeBindingAsync(callbackBindingNameProvider.CallbackBindingName, "create", reservationOperationResult);
            return;
        }

        //else

        logger.LogInformation("Car class {CarClass} is available", request.CarClass);

        if (!reservationState.IsReserved)
        {
            reservationState.IsReserved = true;
            // Increment the count only if not previously reserved
            carClassState += 1;
        }

        var carClassStateUpdate = new StateTransactionRequest(request.CarClass, 
            System.Text.Encoding.UTF8.GetBytes(carClassState.ToString()),
            StateOperationType.Upsert, carClassEtag);

        var reservationStateUpdate = new StateTransactionRequest(orderId, 
                       JsonSerializer.SerializeToUtf8Bytes(reservationState),
                                  StateOperationType.Upsert, orderIdEtag);

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
            await daprClient.InvokeBindingAsync(callbackBindingNameProvider.CallbackBindingName, "create", reservationOperationResult);

        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to reserve car class {CarClass} for order id {orderId}", request.CarClass, request.OrderId);
            reservationOperationResult.IsSuccess = false;
            await daprClient.InvokeBindingAsync(callbackBindingNameProvider.CallbackBindingName, "create", reservationOperationResult);
        }
    }


    async Task CancelCarReservationAsync()
    {
        // TTL set for 5 minutes, this has the effect of deleting the entry
        // but only after the Saga is done, support for compensation
        var metadata = new Dictionary<string, string>
        {
            { "ttlInSeconds", "300" } 
        };

        //get the number of car reserved in the class
        var (carClassState, carClassEtag) = await daprClient.GetStateAndETagAsync<int>("statestore", request.CarClass);

        // Cancel the reservation
        if (reservationState.IsReserved)
        {
            reservationState.IsReserved = false;
            // Decrement the count only if it was previously reserved
            carClassState = Math.Max(0, carClassState - 1); // Ensure it doesn't go below 0
        }

        //we need transactional update to the number of cars reserved in the class and the reservation state
        var carClassStateUpdate = new StateTransactionRequest(request.CarClass,
            System.Text.Encoding.UTF8.GetBytes(carClassState.ToString()),
            StateOperationType.Upsert, carClassEtag);

        var reservationStateUpdate = new StateTransactionRequest(orderId,
            JsonSerializer.SerializeToUtf8Bytes(carClassState),
            StateOperationType.Upsert, orderIdEtag, metadata);

        var transactionOperations = new List<StateTransactionRequest>()
        {
            carClassStateUpdate,
            reservationStateUpdate
        };

        try
        {
            await daprClient.ExecuteStateTransactionAsync("statestore", transactionOperations);
            logger.LogInformation("Car class {CarClass} for order id {orderId} canceled - {CarClassState} cars reserved",
                request.CarClass, request.OrderId, carClassState);

            reservationOperationResult.IsSuccess = true;

            await daprClient.InvokeBindingAsync(callbackBindingNameProvider.CallbackBindingName, "create", reservationOperationResult);

        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to cancel car class {CarClass} for order id {orderId}", request.CarClass, request.OrderId);
            reservationOperationResult.IsSuccess = false;
            await daprClient.InvokeBindingAsync(callbackBindingNameProvider.CallbackBindingName, "create", reservationOperationResult);
        }
    }
})
    .WithName("CarReservation")
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

app.MapHealthChecks("/healthz");
app.UseSagawayContextPropagator();
app.MapControllers();
app.MapSubscribeHandler();
app.UseRouting();

app.Run();
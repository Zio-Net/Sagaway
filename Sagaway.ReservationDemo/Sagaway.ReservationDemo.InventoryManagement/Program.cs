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
builder.Services.AddControllers().AddDapr(b => b.AddSagawayContextPropagator()).AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddHealthChecks();
builder.Services.AddSagawayContextPropagator();
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

        // Get the max allocation for this car class (default to 2 if not set)
        int maxAllocation = 2; // Default
        try
        {
            var maxValue = await daprClient.GetStateAsync<int?>("statestore", $"{request.CarClass}_max");
            if (maxValue.HasValue)
            {
                maxAllocation = maxValue.Value;
            }
        }
        catch
        {
            // Use default if not found
        }

        // Check against the dynamic max allocation
        if (carClassState >= maxAllocation)
        {
            logger.LogInformation("Car class {CarClass} is not available (reserved: {Reserved}/{MaxAllocation})",
                request.CarClass, carClassState, maxAllocation);

            reservationOperationResult.IsSuccess = false;

            // Send the response to the response binding
            await daprClient.InvokeBindingAsync(callbackBindingNameProvider.CallbackBindingName, "create", reservationOperationResult);
            return;
        }

        //else - the rest of the method remains the same
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
            request.CarClass, request.OrderId, carClassState);

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
        // TTL set for 30 seconds, this has the effect of deleting the entry
        // but only after the Saga is done, support for compensation
        var metadata = new Dictionary<string, string>
        {
            { "ttlInSeconds", "30" } 
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
            JsonSerializer.SerializeToUtf8Bytes(reservationState),
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



app.MapGet("/car-inventory", async (
    [FromServices] DaprClient daprClient,
    [FromServices] ILogger<Program> logger) =>
{
    logger.LogInformation("Retrieving car inventory information");

    try
    {
        var allCarClasses = new HashSet<string>();
        var reservations = new Dictionary<string, ReservationState>();

        // 1. Discover car classes from existing reservations
        try
        {
            // Query all ReservationState items. Adjust query if needed for performance.
            // An empty query might be inefficient on large datasets.
            var reservationQuery = "{}"; // Assuming JSON query format for "get all"
            var queryResponse = await daprClient.QueryStateAsync<ReservationState>("statestore", reservationQuery);

            foreach (var item in queryResponse.Results)
            {
                if (item.Data != null && !string.IsNullOrEmpty(item.Data.CarClass))
                {
                    reservations[item.Key] = item.Data; // Store for later fallback count
                    allCarClasses.Add(item.Data.CarClass);
                }
            }
            logger.LogInformation("Discovered {Count} car classes from reservations.", allCarClasses.Count);
        }
        catch (Exception ex)
        {
            // QueryState might not be supported or might fail
            logger.LogWarning(ex, "Unable to query ReservationState from state store. Inventory might be incomplete if allocations are not set.");
        }

        // 2. Discover car classes from allocation keys (ending with _max)
        // Note: Dapr query capabilities depend on the underlying state store.
        // This regex query might not work with all state stores (e.g., basic Redis).
        // Consider maintaining an explicit index if this query is unreliable.
        try
        {
            var allocationQuery = "{\"filter\": {\"REGEX\": {\"key\": \".*_max$\"}}}"; // Example query syntax
            var allocationQueryResponse = await daprClient.QueryStateAsync<object>("statestore", allocationQuery); // Type doesn't matter much here

            int initialCount = allCarClasses.Count;
            foreach (var item in allocationQueryResponse.Results)
            {
                var key = item.Key;
                if (!string.IsNullOrEmpty(key) && key.EndsWith("_max"))
                {
                    var carClass = key.Substring(0, key.Length - "_max".Length);
                    if (!string.IsNullOrEmpty(carClass))
                    {
                        allCarClasses.Add(carClass);
                    }
                }
            }
            logger.LogInformation("Discovered {Count} additional car classes from allocation keys.", allCarClasses.Count - initialCount);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to query allocation keys (_max) from state store. Inventory might be incomplete.");
            // Continue even if this fails, relying on reservation data.
        }

        // 3. Build the response for each unique car class found
        var carClassInfoList = new List<CarClassInfo>();

        if (!allCarClasses.Any())
        {
             logger.LogInformation("No car classes found from reservations or allocations.");
        }


        foreach (var carClass in allCarClasses)
        {
            // Get current count of reservations by car class (primary source)
            int reserved;
            try
            {
                // This key should store the aggregated count of active reservations
                reserved = await daprClient.GetStateAsync<int>("statestore", carClass);
            }
            catch (Exception ex) // Catch specific DaprException if preferred
            {
                logger.LogWarning(ex, "Failed to get reservation count for {CarClass} directly. Falling back to counting loaded reservations.", carClass);
                // Fallback: Count from the reservations dictionary loaded earlier
                // This might be less accurate if the direct count key (`carClass`) is the source of truth
                reserved = reservations.Values.Count(r => r.CarClass == carClass && r.IsReserved);
            }

            // Get max allocation setting
            int maxAllocation = 2; // Default if not set
            try
            {
                var maxValue = await daprClient.GetStateAsync<int?>("statestore", $"{carClass}_max");
                if (maxValue.HasValue)
                {
                    maxAllocation = maxValue.Value;
                }
                // If maxValue is null (key exists but value is null), default is used.
                // If key doesn't exist, GetStateAsync might throw or return default(T), handle accordingly.
            }
            catch(Exception ex) // Catch specific DaprException if preferred
            {
                 logger.LogWarning(ex, "Failed to get max allocation for {CarClass}. Using default {DefaultAllocation}.", carClass, maxAllocation);
                // Use default if key not found or other error
            }

            carClassInfoList.Add(new CarClassInfo
            {
                Code = carClass,
                Reserved = reserved,
                MaxAllocation = maxAllocation
            });
        }

        logger.LogInformation("Returning inventory for {Count} car classes.", carClassInfoList.Count);
        return Results.Ok(new CarInventoryResponse { CarClasses = carClassInfoList });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error retrieving car inventory information");
        return Results.Problem("Failed to retrieve car inventory information");
    }
})
.WithName("GetCarInventory")
.WithOpenApi();

// POST API to update max allocation for a car class
app.MapPost("/car-inventory", async (
    [FromBody] CarClassAllocationRequest request,
    [FromServices] DaprClient daprClient,
    [FromServices] ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.CarClass))
    {
        return Results.BadRequest("Car class code is required");
    }

    if (request.MaxAllocation < 0)
    {
        return Results.BadRequest("Maximum allocation must be non-negative");
    }

    logger.LogInformation("Setting max allocation for car class {CarClass} to {MaxAllocation}",
        request.CarClass, request.MaxAllocation);

    try
    {
        // Save the max allocation setting
        await daprClient.SaveStateAsync("statestore", $"{request.CarClass}_max", request.MaxAllocation);

        // Get current reservation count
        int reserved;
        try
        {
            reserved = await daprClient.GetStateAsync<int>("statestore", request.CarClass);
        }
        catch
        {
            reserved = 0;
        }

        return Results.Ok(new CarClassInfo
        {
            Code = request.CarClass,
            Reserved = reserved,
            MaxAllocation = request.MaxAllocation
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error updating car class allocation for {CarClass}", request.CarClass);
        return Results.Problem($"Failed to update car class allocation for {request.CarClass}");
    }
})
.WithName("UpdateCarClassAllocation")
.WithOpenApi();




app.MapHealthChecks("/healthz");
app.UseSagawayContextPropagator();
app.MapControllers();
app.MapSubscribeHandler();
app.UseRouting();

app.Run();
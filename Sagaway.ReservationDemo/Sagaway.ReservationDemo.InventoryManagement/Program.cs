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

const string carClassIndexKey = "carclass:index";

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

string MakeStateStoreKey(string id) => "Inventory_" + id;

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

    var stateStoreKey = MakeStateStoreKey(orderId);
    try
    {
        (reservationState, orderIdEtag) = await daprClient.GetStateAndETagAsync<ReservationState>("statestore", stateStoreKey);
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
        else
        {
            // If already reserved (e.g., duplicate message), no need to proceed further with state changes
            logger.LogInformation("Order {OrderId} for car class {CarClass} is already marked as reserved. No state change needed.", orderId, request.CarClass);
            // Still send success as the state matches the request
            reservationOperationResult.IsSuccess = true;
            await daprClient.InvokeBindingAsync(callbackBindingNameProvider.CallbackBindingName, "create", reservationOperationResult);
            return;
        }

        var pkMetadataForClass = new Dictionary<string, string>
        {
            
            ["partitionKey"] = request.CarClass
        };

        var carClassStateUpdate = new StateTransactionRequest(request.CarClass,
            JsonSerializer.SerializeToUtf8Bytes(carClassState), // Use JsonSerializer
            StateOperationType.Upsert, carClassEtag,pkMetadataForClass);

        var pkMetadataForReservation = new Dictionary<string, string>
        {
       
            ["partitionKey"] = stateStoreKey
        };

        var reservationStateUpdate = new StateTransactionRequest(stateStoreKey,
                       JsonSerializer.SerializeToUtf8Bytes(reservationState),
                                  StateOperationType.Upsert, orderIdEtag,pkMetadataForReservation);

        var transactionOperations = new List<StateTransactionRequest>()
        {
            carClassStateUpdate,
            reservationStateUpdate
        };

        // Ensure the car class exists in the index
        try
        {
            var (currentIndex, indexEtag) = await daprClient.GetStateAndETagAsync<HashSet<string>>("statestore", carClassIndexKey);
            currentIndex ??= new HashSet<string>();

            if (currentIndex.Add(request.CarClass)) // Add returns true if the item was added (i.e., it wasn't there before)
            {
                logger.LogInformation("Adding car class {CarClass} to index '{IndexKey}' during reservation.", request.CarClass, carClassIndexKey);
                transactionOperations.Add(new StateTransactionRequest(carClassIndexKey, JsonSerializer.SerializeToUtf8Bytes(currentIndex), StateOperationType.Upsert, indexEtag));
            }
        }
        catch (Exception ex)
        {
            // Log the error but proceed with the reservation if possible.
            // Index update failure shouldn't necessarily block the reservation itself,
            // but it means the inventory list might be temporarily incomplete.
            logger.LogWarning(ex, "Failed to update car class index '{IndexKey}' while reserving {CarClass}. Inventory list might be incomplete.", carClassIndexKey, request.CarClass);
        }

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
        //get the number of car reserved in the class
        var (carClassState, carClassEtag) = await daprClient.GetStateAndETagAsync<int>("statestore", request.CarClass);

        // Cancel the reservation
        if (reservationState.IsReserved)
        {
            reservationState.IsReserved = false;
            // Decrement the count only if it was previously reserved
            carClassState = Math.Max(0, carClassState - 1); // Ensure it doesn't go below 0
        }

        var pkMetadataForClass = new Dictionary<string, string>
        {
            // must match your CosmosDB container’s partition-key path (e.g. "/carClass")
            ["partitionKey"] = request.CarClass
        };

        //we need transactional update to the number of cars reserved in the class and the reservation state
        var carClassStateUpdate = new StateTransactionRequest(request.CarClass,
            JsonSerializer.SerializeToUtf8Bytes(carClassState), // Use JsonSerializer
            StateOperationType.Upsert, carClassEtag,pkMetadataForClass);

        var pkMetadataForReservation = new Dictionary<string, string>
        {
            // if your container’s partition-key path is "/id" or "/Id", this should be the reservationId
            ["partitionKey"] = stateStoreKey
        };
        var reservationStateUpdate = new StateTransactionRequest(stateStoreKey,
            null, // No data needed for delete operation
            StateOperationType.Delete, orderIdEtag, pkMetadataForReservation);

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
        var stateStoreKey = MakeStateStoreKey(orderId.ToString());

        // Attempt to fetch the reservation state for the given order ID from the Dapr state store
        var reservationState = await daprClient.GetStateAsync<ReservationState>("statestore", stateStoreKey);

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
    logger.LogInformation("Retrieving car inventory information using index key '{IndexKey}'", carClassIndexKey);
    var carClassInfoList = new List<CarClassInfo>();

    try
    {
        // 1. Get the list of car class codes from the index
        var carClassCodes = await daprClient.GetStateAsync<HashSet<string>>("statestore", carClassIndexKey);

        if (carClassCodes == null || !carClassCodes.Any())
        {
            logger.LogInformation("Car class index '{IndexKey}' is empty or not found.", carClassIndexKey);
            // Return empty list or potentially seed default classes here if desired
            return Results.Ok(new CarInventoryResponse { CarClasses = carClassInfoList });
        }

        logger.LogInformation("Found {Count} car classes in index: {CarClasses}", carClassCodes.Count, string.Join(", ", carClassCodes));

        // 2. For each car class code, get its reservation count and max allocation
        foreach (var carClass in carClassCodes)
        {
            if (string.IsNullOrWhiteSpace(carClass)) continue; // Skip empty entries if any

            // Get current count of reservations by car class
            int reserved;
            try
            {
                // This key stores the aggregated count of active reservations
                reserved = await daprClient.GetStateAsync<int>("statestore", carClass);
            }
            catch (Exception ex) // More specific: catch DaprException when key not found?
            {
                // Key might not exist if no reservations made yet, default to 0
                logger.LogWarning(ex, "Failed to get reservation count for {CarClass} or key not found. Assuming 0.", carClass);
                reserved = 0;
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
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get max allocation for {CarClass} or key not found. Using default {DefaultAllocation}.", carClass, maxAllocation);
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
        return Results.Problem("Failed to retrieve car inventory information due to an internal error.");
    }
})
.WithName("GetCarInventory")
.WithOpenApi();

// POST API to update max allocation for a car class AND update the index
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

    var carClass = request.CarClass.Trim(); // Ensure no leading/trailing spaces
    var allocationKey = $"{carClass}_max";

    logger.LogInformation("Setting max allocation for car class {CarClass} to {MaxAllocation}",
        carClass, request.MaxAllocation);

    try
    {
        // Use a transaction to save allocation and update index atomically
        var transactionRequests = new List<StateTransactionRequest>();

        // 1. Save the max allocation setting
        transactionRequests.Add(new StateTransactionRequest(allocationKey, JsonSerializer.SerializeToUtf8Bytes(request.MaxAllocation), StateOperationType.Upsert));

        // 2. Update the car class index
        var (currentIndex, etag) = await daprClient.GetStateAndETagAsync<HashSet<string>>("statestore", carClassIndexKey);
        currentIndex ??= new HashSet<string>(); // Initialize if null

        bool indexChanged = currentIndex.Add(carClass); // Add returns true if the item was added

        if (indexChanged)
        {
            logger.LogInformation("Adding car class {CarClass} to index '{IndexKey}'", carClass, carClassIndexKey);
            transactionRequests.Add(new StateTransactionRequest(carClassIndexKey, JsonSerializer.SerializeToUtf8Bytes(currentIndex), StateOperationType.Upsert, etag));
        }
        else
        {
            logger.LogInformation("Car class {CarClass} already exists in index '{IndexKey}'", carClass, carClassIndexKey);
        }

        // Execute the transaction
        await daprClient.ExecuteStateTransactionAsync("statestore", transactionRequests);


        // Get current reservation count for the response (best effort after transaction)
        int reserved = 0;
        try
        {
            reserved = await daprClient.GetStateAsync<int>("statestore", carClass);
        }
        catch { /* Ignore if not found, default is 0 */ }

        return Results.Ok(new CarClassInfo
        {
            Code = carClass,
            Reserved = reserved,
            MaxAllocation = request.MaxAllocation
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error updating car class allocation for {CarClass}", carClass);
        return Results.Problem($"Failed to update car class allocation for {carClass}");
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
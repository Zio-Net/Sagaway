using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sagaway.Callback.Propagator;
using Sagaway.ReservationDemo.BookingManagement;
using System.Globalization;
using System.Net;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using Dapr;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders(); 
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Register DaprClient that supports Sagaway context propagator
builder.Services.AddControllers().AddDaprWithSagawayContextPropagator().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
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
        ResourceBuilder.CreateDefault().AddService("BookingManagementService"));
});

builder.Services.AddHealthChecks();
builder.Services.AddSagawayContextPropagator();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

string MakeStateStoreKey(string reservationId) => "Booking_" + reservationId;

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}



Dictionary<string, string> jsonMetadata = new() { { "contentType", "application/json" } };

app.MapPost("/booking-queue", async (
        [FromBody] CarReservationRequest request,
        [FromHeader(Name = "x-sagaway-dapr-message-dispatch-time")] string messageDispatchTimeHeader,
        [FromServices] ILogger<Program> logger,
        [FromServices] ICallbackBindingNameProvider callbackBindingNameProvider,
        [FromServices] DaprClient daprClient) =>
    {
        logger.LogInformation("Received car {request} request for {CarClass} from {CustomerName}",
           request.ActionType, request.CarClass, request.CustomerName);

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

        ReservationState? reservationState = null;
        string etag = string.Empty;

        try
        {
            (reservationState, etag) = await daprClient.GetStateAndETagAsync<ReservationState>("statestore", MakeStateStoreKey(reservationId), metadata: jsonMetadata);

        }
        catch (DaprException ex) when (ex.InnerException is Grpc.Core.RpcException { Status.StatusCode: Grpc.Core.StatusCode.Internal } grpcEx)
        {
            // Check specific RPC error message details if necessary
            if (grpcEx.Status.Detail.Contains("redis: nil"))
            {
                logger.LogWarning("Reservation state not found for reservation id {reservationId}. Detail: {Detail}", reservationId, grpcEx.Status.Detail);
            }
            else
            {
                logger.LogError("An internal error occurred when accessing the state store: {Detail}", grpcEx.Status.Detail);
                throw;
            }
        }

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
            CarClass = request.CarClass,
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
                var result = await daprClient.TrySaveStateAsync("statestore", MakeStateStoreKey(reservationId), 
                    reservationState, etag, stateOptions, jsonMetadata);

                logger.LogInformation("Car class {CarClass} {result} reserved for {CustomerName}", 
                    request.CarClass, result ? "has" : "failed to", request.CustomerName);
                reservationOperationResult.IsSuccess = result;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to reserve car class {CarClass} for {CustomerName}", request.CarClass, request.CustomerName);
                reservationOperationResult.IsSuccess = false;
            }

            // Send the response to the response binding
            await daprClient.InvokeBindingAsync(callbackBindingNameProvider.CallbackBindingName, "create", reservationOperationResult);
        }


        async Task CancelCarReservationAsync()
        {

            logger.LogInformation("Cancelling car class {CarClass} reservation id {reservationId} for {CustomerName}",
                request.CarClass, reservationId, request.CustomerName);

            reservationState.IsReserved = false;

            try
            {
                var result = await daprClient.TrySaveStateAsync<ReservationState?>("statestore", MakeStateStoreKey(reservationId), reservationState, 
                    etag, stateOptions, jsonMetadata);

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

            // Send the response to the response binding
            await daprClient.InvokeBindingAsync(callbackBindingNameProvider.CallbackBindingName, "create", reservationOperationResult);
        }
    })
    .WithName("CarBooking")
    .WithOpenApi();


app.MapGet("/reservations/{reservationId}", async ([FromRoute] Guid reservationId, [FromServices] DaprClient daprClient, [FromServices] ILogger<Program> logger) =>
    {
        logger.LogInformation($"Fetching reservation status for reservation ID: {reservationId}");
        var stateStoreId = MakeStateStoreKey(reservationId.ToString());

        try
        {
            var reservationState = await daprClient.GetStateAsync<ReservationState>("statestore", stateStoreId, metadata: jsonMetadata);

            if (reservationState == null)
            {
                logger.LogWarning($"Reservation with ID: {reservationId} not found.");
                return Results.NotFound(new { Message = $"Reservation with ID: {reservationId} not found." });
            }

            return Results.Ok(reservationState);
        }
        catch (DaprException ex) when (ex.InnerException is Grpc.Core.RpcException { Status.StatusCode: Grpc.Core.StatusCode.Internal } grpcEx)
        {
            if (grpcEx.Status.Detail.Contains("redis: nil"))
            {
                logger.LogWarning("Reservation state not found for reservation id {reservationId}. Detail: {Detail}", reservationId, grpcEx.Status.Detail);
                return Results.NotFound(new { Message = $"Reservation with ID: {reservationId} not found." });
            }

            logger.LogError("An internal error occurred when accessing the state store: {Detail}", grpcEx.Status.Detail);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error fetching reservation status for reservation ID: {reservationId}");
        }
        return Results.Problem("An error occurred while fetching the reservation status. Please try again later.");
    })
    .WithName("GetReservationStatus")
    .WithOpenApi(); // This adds the endpoint to OpenAPI/Swagger documentation if enabled

app.MapGet("/customer-reservations", async ([FromQuery] string customerName, [FromServices] DaprClient daprClient, [FromServices] ILogger<Program> logger) =>
    {
        logger.LogInformation($"Fetching reservations for customer: {customerName}");

        try
        {
            var allResults = new List<StateQueryItem<ReservationState?>>();
            string? paginationToken = null;

            do
            {
                // Using JsonSerializer approach
               var queryObject = new Dictionary<string, object>
                {
                    ["filter"] = new Dictionary<string, object>
                    {
                        ["EQ"] = new Dictionary<string, string> { ["customerName"] = customerName }
                    },
                    ["page"] = new Dictionary<string, object> 
                    {
                        ["limit"] = 100  // Request up to 100 items instead of default (likely 10)
                    }
                };
                // Add pagination if token exists
                if (paginationToken != null)
                {
                    queryObject["page"] = new Dictionary<string, string> { ["token"] = paginationToken };
                }

                var queryJson = JsonSerializer.Serialize(queryObject);

                var metadata = new Dictionary<string, string>
                {
                    { "contentType", "application/json" },
                    { "queryIndexName", "customerNameIndex" }
                };

                var queryResponse = await daprClient.QueryStateAsync<ReservationState?>("statestore", queryJson, metadata);

                if (queryResponse?.Results != null)
                {
                    allResults.AddRange(queryResponse.Results);
                }

                paginationToken = queryResponse?.Token;

                //log if we have a token for pagination
                if (!string.IsNullOrEmpty(paginationToken))
                {
                    logger.LogInformation("Pagination token received: {Token}", paginationToken);
                }
                else
                {
                    logger.LogInformation("No more pages to fetch.");
                }

            } while (!string.IsNullOrEmpty(paginationToken));


            // Now use allResults instead of customerReservations
            if (allResults == null || allResults.Count == 0)
            {
                logger.LogInformation("No reservations found for customer: {CustomerName}", customerName);
                return Results.NotFound(new { Message = $"No reservations found for customer: {customerName}" });
            }
            else
            {
                logger.LogInformation("Found {Count} total reservations for customer: {CustomerName}", allResults.Count, customerName);

                var reservedCount = allResults.Count(r => r.Data?.IsReserved == true);
                logger.LogInformation("Customer {CustomerName} has {ReservedCount} reserved cars.", customerName, reservedCount);
            }

            return Results.Ok(allResults.Select(r => r.Data).ToArray());
        }
        catch (DaprException daprException)
            when (daprException.InnerException is Grpc.Core.RpcException { StatusCode: Grpc.Core.StatusCode.Internal } grpcEx
                  && grpcEx.Status.Detail.Contains("invalid output"))
        {
            // Workaround for Dapr bug: treat "invalid output" as empty result set - dapr bug #3787
            logger.LogWarning(grpcEx,
                "Dapr QueryStateAsync returned invalid output for customer {CustomerName}. Returning empty list.",
                customerName);
            return Results.Ok(Array.Empty<ReservationState>());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error fetching reservations for customer: {customerName}");
            return Results.Problem("An error occurred while fetching the reservations. Please try again later.");
        }
    })
    .WithName("GetCustomerReservations")
    .WithOpenApi();



app.MapHealthChecks("/healthz");
app.UseSagawayContextPropagator();
app.MapControllers();
app.MapSubscribeHandler();
app.UseRouting();

app.Run();
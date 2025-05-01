using System.Text.Json.Serialization;
using System.Text.Json;
using Dapr.Actors;
using Microsoft.AspNetCore.Mvc;
using Dapr.Actors.Client;
using Dapr.Client;
using OpenTelemetry.Trace;
using Sagaway.Callback.Router;
using Sagaway.OpenTelemetry;
using Sagaway.ReservationDemo.ReservationManager.Actors;
using Sagaway.ReservationDemo.ReservationManager.Actors.CarReservation;
using Sagaway.ReservationDemo.ReservationManager.Actors.CarReservationCancellation;
using Sagaway.ReservationDemo.ReservationManager;
using Sagaway.ReservationDemo.ReservationManager.Actors.Publisher;

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
    options.Actors.RegisterActor<CarReservationCancellationActor>();

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

builder.Services.AddSingleton<SignalRService>()
#pragma warning disable CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
#pragma warning disable CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
    .AddHostedService(sp => sp.GetService<SignalRService>()!)
#pragma warning restore CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
#pragma warning restore CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
    .AddSingleton<IHubContextStore>(sp => sp.GetService<SignalRService>()!)
    .AddDaprClient();

builder.Services.AddSingleton<ISagaResultPublisher, SagaResultPublisher>();

builder.Services.AddSagawayOpenTelemetry(configureTracerProvider =>
{
    configureTracerProvider
        .AddAspNetCoreInstrumentation(options => 
            { options.Filter = (httpContext) => httpContext.Request.Path != "/healthz"; }) // Instruments incoming requests
        .AddHttpClientInstrumentation() // Instrument outgoing HTTP requests
        .AddConsoleExporter()
        .AddZipkinExporter(options =>
        {
            options.Endpoint = new Uri("http://zipkin:9411/api/v2/spans");
        })
        .SetSampler(new AlwaysOnSampler()); // Collect all samples. Adjust as necessary for production.
}, "ReservationManagerService");

builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHostedService<SignalRService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//enable callback router
app.UseSagawayCallbackRouter("reservation-response-queue");

app.MapGet("/reservation/{reservationId}", async ([FromRoute] Guid reservationId, [FromServices] DaprClient daprClient,
    [FromServices] ILogger<Program> logger) =>
    {
        logger.LogInformation("Received request to get reservation details for reservation: {ReservationId}", reservationId);

        try
        {
            var reservationInfo = await daprClient.InvokeMethodAsync<BookingInfo>(HttpMethod.Get, "booking-management",
                $"/reservations/{reservationId}");

            return Results.Ok(reservationInfo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting reservation details for reservation: {ReservationId}", reservationId);
            return Results.Problem("An error occurred while getting reservation details. Please try again later.");
        }
    })
    .WithName("GetReservation")
    .WithOpenApi();


app.MapGet("/reservations/{customerName}", async (
        [FromRoute] string customerName,
        [FromServices] DaprClient daprClient,
        [FromServices] ILogger<Program> logger) =>
    {
        logger.LogInformation("Received request to get reservations for customer: {CustomerName}", customerName);

        try
        {
            var customerReservation = 
                await daprClient.InvokeMethodAsync<IList<BookingInfo>>(HttpMethod.Get, "booking-management",
                $"/customer-reservations?customerName={customerName}");

            return Results.Ok(customerReservation);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting reservations for customer: {CustomerName}", customerName);
            return Results.Problem("An error occurred while getting reservations. Please try again later.");
        }
    })
    .WithName("GetReservations")
    .WithOpenApi();


app.MapGet("/saga-log/{reservationId}", async (
        [FromRoute] Guid reservationId,
        [FromServices] DaprClient daprClient,
        [FromServices] ILogger<Program> logger) =>
    {
        logger.LogInformation("Received request to get saga log for reservation: {ReservationId}", reservationId);

        try
        {
            var key = $"saga-log-{reservationId:D}";

            // Attempt to retrieve the saga log from Dapr state
            var sagaLog = await daprClient.GetStateAsync<string>("statestore", key);

            if (string.IsNullOrEmpty(sagaLog))
            {
                logger.LogWarning("Saga log not found for reservation: {ReservationId}", reservationId);
                return Results.NotFound("Saga log not found.");
            }

            logger.LogInformation("Successfully retrieved saga log for reservation: {ReservationId}", reservationId);
            return Results.Ok(sagaLog);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving saga log for reservation: {ReservationId}", reservationId);
            return Results.Problem("An error occurred while retrieving the saga log. Please try again later.");
        }
    })
    .WithName("GetSagaLog")
    .WithOpenApi();

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
        [FromServices] DaprClient daprClient,
        [FromServices] ILogger<Program> logger) =>
    {
        logger.LogInformation("Received car reservation cancellation request for {ReservationId}", reservationId);

        if (reservationId == Guid.Empty)
        {
            return Results.BadRequest("Invalid reservation ID.");
        }

        BookingInfo? bookingInfo = null;
        InventoryInfo? inventoryInfo = null;

        //Get the reservation details from the booking service
        try
        {
            bookingInfo =
                await daprClient.InvokeMethodAsync<BookingInfo>(HttpMethod.Get, "booking-management",
                    $"/reservations/{reservationId}");

            inventoryInfo =
                await daprClient.InvokeMethodAsync<InventoryInfo>(HttpMethod.Get, "inventory-management",
                                       $"/reservation-state/{reservationId}");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error in ValidateBookCarReservationAsync for reservation id: {reservationId}", 
                reservationId);
            Results.Problem("An error occurred while fetching the reservation details. Please try again later.");
        }

        if (bookingInfo == null || inventoryInfo == null)
        {
            return Results.NotFound("Reservation not found.");
        }

        if (!bookingInfo.IsReserved)
        {
            return Results.BadRequest("Reservation is not exist.");
        }

        var reservationInfo = new ReservationInfo
        {
            ReservationId = reservationId,
            CustomerName = bookingInfo.CustomerName,
            CarClass = inventoryInfo.CarClass,
        };

        try
        {
            var proxy = actorProxyFactory.CreateActorProxy<ICarReservationCancellationActor>(
                new ActorId(reservationId.ToString("D")), "CarReservationCancellationActor");

            await proxy.CancelCarReservationAsync(reservationInfo);

            logger.LogInformation("Successfully cancelled car reservation for {ReservationId}", reservationId);
            return Results.Ok($"Cancelling process has started for Reservation {reservationId}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error cancelling reservation {ReservationId}", reservationId);
            return Results.Problem("An error occurred while cancelling the reservation. Please try again later.");
        }
    })
    .WithName("Cancel")
    .WithOpenApi();


app.MapPost("/negotiate", async (
    [FromServices] IHubContextStore store,
    [FromServices] ILogger<Program> logger) =>
{
    var accountManagerCallbackHubContext = store.AccountManagerCallbackHubContext;

    logger.LogInformation("MessageHubNegotiate: SignalR negotiate for user");

    var negotiateResponse = await accountManagerCallbackHubContext!.NegotiateAsync(new()
    {
        UserId = "demoUser", //user,
        EnableDetailedErrors = true
    });

    return Results.Json(new Dictionary<string, string>()
    {
        { "url", negotiateResponse.Url! },
        { "accessToken", negotiateResponse.AccessToken! }
    });
});

app.MapHealthChecks("/healthz");
app.MapControllers();
app.MapSubscribeHandler();
app.UseRouting();

app.MapActorsHandlers();

app.Run();
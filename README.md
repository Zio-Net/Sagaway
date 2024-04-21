# Sagaway - A Distributed Application Saga

## The Saga Pattern

Sagaway embodies the Saga pattern, a sophisticated approach to managing transactions and ensuring consistency within distributed systems. This pattern delineates a sequence of local transactions, each updating the system's state and paving the way for the subsequent step. In the event of a transaction failure, compensating transactions are initiated to reverse the effects of prior operations. Sagas can operate sequentially, where operations follow one another or execute multiple operations simultaneously in parallel.

Implementing Sagas can be straightforward, involving synchronous request-reply interactions with participant services. Yet, embracing asynchronous communication proves superior for optimal integration within a Microservices Architecture (MSA). It entails employing queues or publish/subscribe models and awaiting results. This strategy allows the coordinating service to halt its operations, thereby liberating resources. The orchestration of the Saga resumes once a response is received from any of the participant services, typically through callback mechanisms like queues or a publish/subscribe system. This advanced pattern of Saga management necessitates asynchronous service calls, resource allocation efficiency, and mechanisms to revisit operational states. Additionally, it encompasses handling unacknowledged requests through status checks and retries, along with executing asynchronous compensations.

Despite the inherent complexity, the asynchronous Saga is favored for its contribution to several critical architectural qualities, including scalability, robustness, resilience, high availability, and consistency, all of which are integral to an efficient MSA messaging exchange pattern. Sagaway not only facilitates the creation of straightforward Sagas but also excels by providing a robust foundation for managing asynchronous Sagas, thus embodying the essence of simplicity in software architecture while catering to a broader spectrum of quality attributes.

## The Car Reservation Demo

To understand Saga and the Sagaway framework, first look at the [Sagaway.ReservationDemo](https://github.com/alonf/Sagaway/tree/master/Sagaway.ReservationDemo), which is part of the Sagaway solution.

The Demo Car Reservation System exemplifies a contemporary approach to distributed transaction management within a microservice architecture. At its core, the system is designed to facilitate the reserving and canceling of car bookings while maintaining a consistent and reliable state across various services, each with a distinct responsibility.

The system allows users to reserve cars from an inventory and manage the billing associated with these reservations. Should a user wish to cancel a reservation, the system ensures that the inventory is updated, the reservation is annulled, and any charges are refunded. The complexity of this process lies in the system's distributed nature, where different components need to coordinate state changes consistently despite the potential for network failures, service outages, or other distributed system anomalies.

The intricacy of maintaining a consistent state across services is addressed by implementing Sagas. A Saga is a sequence of local transactions spread across multiple services, with each local transaction updating the system state and triggering the next transaction. If a local transaction in a Saga fails, compensating transactions are triggered to roll back the changes made by previous transactions, ensuring system-wide consistency.

The system is composed of three key services:

1. **Billing Service**: Emulate all financial transactions associated with the car reservations. It ensures that the corresponding charges are processed when a car is reserved. It also takes care of the necessary refunds in the event of a cancellation.

2. **Booking Service**: Manages the reservations themselves. It records the reservation details, such as the customer's name and the class of car reserved. It also ensures that the reservation state is maintained, whether the car is currently reserved or available for others.

3. **Inventory Service**: Keeps track of the cars available for reservation. It updates the inventory in real-time as cars are reserved and released, ensuring that the same car is not double-booked and that reservations are only made for available cars.

The system employs two Sagas:

- **Reservation Saga**: Orchestrates the steps in reserving a car. It begins with sending concurrent requests for the Booking Service to record the reservation and the Inventory Service to decrease the available inventory. Upon the success of those two services, it finally requested the Billing Service to process the payment.

- **Cancellation Saga**: Manages the cancellation of reservations. It reverses the actions of the Reservation Saga. It starts with the Booking Service to release the reservation and, in parallel, updates the Inventory Service to increment the available inventory. And lastly, on the success of the two service requests, it instructs the Billing Service to issue a refund.

Both Sagas are designed to handle failures at any point in the process. For instance, if the Billing Service cannot process a charge, the system will not finalize the reservation and will release any holds on the inventory. Similarly, suppose an error occurs during the cancellation process. In that case, the system will retry the necessary steps to ensure that as long there is a payment from the user, the car is registered to them. Of course, you can decide on any other compensation logic you like.

### Saga Compensation

In the demonstration of the car reservation system, participant services are crafted to emulate potential failures to showcase the compensatory mechanisms of the Sagaway framework. This illustrative setup is essential to understand how the system maintains consistency in adverse scenarios.

The **Billing Service** simulates failures using a random number generator to determine the outcome of billing actions, such as charging for a reservation or processing a refund. In real-world scenarios, failures could stem from network issues, processing errors, or validation failures. Here, the random determination of a billing status—charged, refunded, or not charged—mimics these unpredictable failures. When a charge fails, the Saga must compensate, meaning it should cancel the reservation and release any inventory holds.

The **Inventory Service** demonstrates a different failure scenario by enforcing a constraint where no more than two cars of a particular class can be reserved. If a reservation request exceeds this limit, the service denies the reservation and triggers a failure. It reflects a common real-world limitation where resources are finite and must be managed. Upon such a denial, the Saga's compensatory action would kick in to ensure that partial transactions are rolled back, maintaining the system's integrity. For instance, if a car cannot be reserved because of inventory limits, any previous successful booking transactions should be undone.

### Ensuring Transactional Integrity through Idempotency and Message Sequencing

The Sagaway framework's robust state management capabilities are essential for preserving transactional integrity. A key aspect of maintaining such integrity involves accounting for the potential challenges of idempotency and message sequencing, which are addressed by incorporating a message dispatch time via an HTTP header. This timestamp and a unique message identifier allow the system to disregard stale or duplicate messages, ensuring that only the latest and relevant actions are processed.

Handling Idempotancy when adding or updating a state is more straightforward than handling Idempotency for deleting a state. It might sound strange; deletion operations are inherently idempotent, and the same delete command can be applied multiple times without changing the outcome. The issue arises when messages are received out of order. Such scenarios could inadvertently lead to the recreation of a previously deleted state. This race condition could occur within the asynchronous and compensatory stages of a Saga, especially when multiple instances of a service are involved.

One strategy employed to mitigate a wrong outcome of out-of-order messages is the concept of suspended deletion. By flagging a record for eventual deletion and retaining it for a specified duration, the system ensures that outdated messages do not inadvertently recreate state entries that should no longer exist. This approach can be implemented through periodic cleanup processes purging marked records or setting a Time-To-Live (TTL) attribute on the state. The latter is particularly effective, as it automates the removal process while allowing sufficient time to ensure all aspects of the Saga have concluded.

The TTL duration is carefully calibrated to balance prompt resource liberation with the demands of ongoing Saga operations. For instance, in the car reservations system, once a cancellation is confirmed, the TTL ensures that the vehicle is retained in the "reserved" state only as long as necessary to prevent any out-of-order messages from affecting the availability. Once the TTL expires, the system can confidently release the car into the available inventory, optimizing the asset's utilization and ensuring customer satisfaction. 
You can free resources immediately by utilizing a separate database (or a distributed cache) for the historical messages, their identity, and dispatch time stamps. You need to house-keeping this database.

## Using Sagaway in a Dapr System

[Dapr]( https://dapr.io/) – a Distributed Application Runtime is a robust foundation for developing Microservices solutions. As such, Dapr provides various mechanisms to handle the MSA complexity. One of the new mechanisms is Dapr [Dapr Workflow]( https://docs.dapr.io/developing-applications/building-blocks/workflow/workflow-overview/)
Dapr Workflow is a new mechanism of Dapr that provides long-running, isolated workflows that can be used to implement a Saga. However, the Dapr Workflow is a new, [not production-ready]( https://docs.dapr.io/developing-applications/building-blocks/workflow/workflow-overview/#limitations), mechanism. [Dapr Workflow is based on the event sourcing pattern]( https://docs.dapr.io/developing-applications/building-blocks/workflow/workflow-architecture/), meaning that it records any outcome of activity operation, and when the Workflow resumes, it executes all previous events to get to the last Workflow state. While this mechanism is effective, it has many restrictions and pitfalls, such as using deterministic functions and only splitting the Workflow into orchestration, activities, and child workflows to prevent too many historical events. I developed the first version of Sagaway before Dapr had built-in support for workflow executions. However, even with Dapr Workflow, Sagaway provides an out-of-the-box method to implement asynchronous Saga. In contrast, with Dapr Workflow, you must manually craft the calls to validate an action outcome and implement waiting for callback events with `WaitForExternalEventAsync`.  
For good reasons, the Sagaway Host and the Dapr Workflow building block utilize the Dapr Actor model. Dapr Actor provides the means to execute code in isolation, calling the Actor via a proxy and enabling the Dapr runtime to deactivate and reactivate Actor instances. Dapr actor has built-in state management and a reminder mechanism – crucial for waking the Saga to verify the outcome and retry operations. The Actor model dictates that only a single request is executed on an instance at a time, ensuring thread safety.

## Using the Sagaway Framework in ASP.NET Minimal API and Dapr

Follow these steps to implement Saga with Sagaway, Dapr, and ASP.NET Minimal API:
For the service that hosts the Dapr Actor:

- Create a minimal API project
- Add Sagaway NuGet packages. For the service that host the Dapr Actors add:
  - Dapr.Client
  - Dapr.AspNetCore
  - Dapr.Actors
  - Sagaway.Hosts.DaprActorHost
  - If you want to have Open Telemetry, then add:
    - OpenTelemetry NuGet packages, according to the kinf of instrumentations and exporters, for example:
      - OpenTelemetry
      - OpenTelemetry.Api.ProviderBuilderExtensions
      - OpenTelemetry.Exporter.Console
      - OpenTelemetry.Extensions.Hosting
      - OpenTelemetry.Instrumentation.AspNetCore
      - OpenTelemetry.Instrumentation.Http
      - OpenTelemetry.Exporter.Zipkin
      - Sagaway.OpenTelemetry
- Register the Dapr client. You can also add Json options for the default serialization:

```csharp
builder.Services.AddControllers().AddDapr().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});
```

- Register the Dapr actors:

```csharp
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
```

- Add the Open Telemetry support:

```csharp
builder.Services.AddSagawayOpenTelemetry(configureTracerProvider =>
{
    configureTracerProvider
        tracing.AddAspNetCoreInstrumentation(options =>
        {
            options.Filter = (httpContext) => httpContext.Request.Path != "/healthz";
        })                              // Instruments incoming requests
        .AddHttpClientInstrumentation() // Instrument outgoing HTTP requests
        .AddConsoleExporter()
        .AddZipkinExporter(options =>
        {
            options.Endpoint = new Uri("http://zipkin:9411/api/v2/spans");
        })
        .SetSampler(new AlwaysOnSampler()); // Collect all samples. Adjust as necessary for production.
}, "ReservationManagerService");
```

- Dapr sidecar uses the ASP.NET built-in health API, so add also:

```csharp
builder.Services.AddHealthChecks();
```

- To support auto-routing of a callback message, add the following line:

```csharp
app.UseSagawayCallbackRouter("reservation-response-queue");
```

The parameter is a Dapr component binding name of the callback. The Sagaway currently supports bindings that call the POST HTTP method. A Pub/Sub Topic is not supported yet for auto-routing.

Example of such a binding:

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: reservation-response-queue
spec:
  type: bindings.rabbitmq
  version: v1
  metadata:
  - name: queueName
    value: reservation-response-queue
  - name: host
    value: "amqp://rabbitmq:5672"
  - name: durable
    value: true
  - name: deleteWhenUnused
    value: false
  - name: ttlInSeconds
    value: 60
  - name: prefetchCount
    value: 0
  - name: exclusive
    value: false
  - name: maxPriority
    value: 5
  - name: contentType
    value: "text/plain"
```

- Map your APIs:

```csharp
app.MapPost("/reserve", async (
        [FromQuery] Guid? reservationId, 
        [FromQuery] string customerName, 
        [FromQuery] string carClass, 
        [FromServices] IActorProxyFactory actorProxyFactory,
        [FromServices] ILogger <Program> logger) =>
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
```

Pay attention to the use of the Actor proxy. Use it as any Dapr-typed Actor with your defined methods in its interface. For example:

```csharp
using Dapr.Actors;

namespace Sagaway.ReservationDemo.ReservationManager.Actors.CarReservation;
public interface ICarReservationActor : IActor
{
    Task ReserveCarAsync(ReservationInfo reservationInfo);
}
```

- Don’t forget all ASP.NET Minimal API mapping, according to your service needs:

```csharp
app.MapHealthChecks("/healthz");
app.MapControllers();
app.MapSubscribeHandler();
app.UseRouting();
app.MapActorsHandlers();

app.Run();
```

- For a complete result, see [Program.cs]( https://github.com/alonf/Sagaway/blob/master/Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationManager/Program.cs)

### Implementing the Saga Within a Dapr Actor

- first, create the enum of all of the Saga operations:

```csharp
[Flags]
public enum CarReservationActorOperations
{
    CarBooking = 1,
    InventoryReserving = 2,
    Billing = 4
}
```

Make sure you decorate it as Flags since the enum also defines dependencies between operations.

- Define the interface with the function that initializes the Saga:

```csharp
public interface ICarReservationActor : IActor
{
    Task ReserveCarAsync(ReservationInfo reservationInfo);
}
```

- Create the Actor class:

```csharp
[Actor(TypeName = "CarReservationActor")]
public class CarReservationActor : DaprActorHost<CarReservationActorOperations>, ICarReservationActor
{
    private readonly ILogger<CarReservationActor> _logger;
    private readonly ActorHost _actorHost;
    private ReservationInfo? _reservationInfo;

    public CarReservationActor(ActorHost host,
        ILogger<CarReservationActor> logger)
        : base(host, logger)
    {
        _actorHost = host;
        _logger = logger;
    }
```

- Dapr typed Actor dictates that the class must be decorated with the `Actor` attributes. Dapr Actor also dictates that the actor class needs to be derived from the Actor base class; however, the Sagaway Dapr Actor host does this for us:

```csharp
public abstract class DaprActorHost<TEOperations> : Actor, IRemindable, ISagaSupport, ISagawayActor
    where TEOperations : Enum
{
...

}
```

For example, this is the CarReservationActor:

```csharp
[Actor(TypeName = "CarReservationActor")]
public class CarReservationActor : DaprActorHost<CarReservationActorOperations>, ICarReservationActor
{
    private readonly ILogger<CarReservationActor> _logger;
    private readonly ActorHost _actorHost;
    private ReservationInfo? _reservationInfo;

    public CarReservationActor(ActorHost host, ILogger<CarReservationActor> logger, IServiceProvider? serviceProvider)
        : base(host, logger, serviceProvider)
    {
        _actorHost = host;
        _logger = logger;
    }

    protected override string GetCallbackBindingName()
    {
        return "reservation-response-queue";
    }
```

The `GetCallbackBindingName` is needed to enable auto dispatching of callback calls using Dapr binding.

## The Saga Builder

Since Actor instances are subject to deactivation, it is crucial to restore the state of the Actor instance when reactivating. There are two parts of the state:

- The static state, i.e., the operations object graph, the parameters such as the number of retries, the wait time between a retry attempt per operation, and more
- The dynamic state, i.e., the progress of the Saga.

The static state is the Saga configuration. Sagaway uses the Builder pattern to set the parameters and tie the functions to the operations. The Builder assists with providing the mandatory and optional Saga parameters.

- Example of building a Saga:

```csharp
protected override ISaga<CarReservationActorOperations> ReBuildSaga()
 {
     var saga = Saga<CarReservationActorOperations>.Create(_actorHost.Id.ToString(), this, _logger)
        .WithOnSuccessCompletionCallback(OnSuccessCompletionCallbackAsync)
        .WithOnRevertedCallback(OnRevertedCallbackAsync)
        .WithOnFailedRevertedCallback(OnFailedRevertedCallbackAsync)
        .WithOnFailedCallback(OnFailedCallbackAsync)

        .WithOperation(CarReservationActorOperations.CarBooking)
        .WithDoOperation(BookCarReservationAsync)
        .WithMaxRetries(3)
        .WithRetryIntervalTime(TimeSpan.FromMinutes(2)) //an example of a fixed interval
        .WithValidateFunction(ValidateBookCarReservationAsync)
        .WithUndoOperation(RevertBookCarReservationAsync)
        .WithMaxRetries(3)
        .WithUndoRetryInterval(TimeSpan.FromMinutes(10))
        .WithValidateFunction(ValidateRevertBookCarReservationAsync)

        .WithOperation(CarReservationActorOperations.InventoryReserving)
        .WithDoOperation(ReserveInventoryAsync)
        .WithMaxRetries(3)
        .WithRetryIntervalTime(ExponentialBackoff.InMinutes()) //An example of an exponential backoff in minutes
        .WithValidateFunction(ValidateReserveInventoryAsync)
        .WithUndoOperation(RevertReserveInventoryAsync)
        .WithMaxRetries(3)
        .WithUndoRetryInterval(ExponentialBackoff.InMinutes())
        .WithValidateFunction(ValidateRevertReserveInventoryAsync)

        .WithOperation(CarReservationActorOperations.Billing)
        .WithDoOperation(BillReservationAsync)
        .WithMaxRetries(3)
        .WithRetryIntervalTime(ExponentialBackoff.InSeconds()) //An example of an exponential backoff in seconds
        .WithValidateFunction(ValidateBillReservationAsync)
        .WithPreconditions(CarReservationActorOperations.CarBooking | CarReservationActorOperations.InventoryReserving)
        .WithUndoOperation(RevertBillReservationAsync)
        .WithMaxRetries(3)
        .WithUndoRetryInterval(ExponentialBackoff.InSeconds())
        .WithValidateFunction(ValidateRevertBillReservationAsync)

        .Build();

    saga.OnSagaCompleted += async (s, e) => await OnSagaCompletedAsync(s, e);

    return saga;
 }
```

- Let's understand each part:

```csharp
var saga = Saga<CarReservationActorOperations>.Create(_actorHost.Id.ToString(), this, _logger)
```

Tyes the Saga to the host and provide the Saga identity and logger.

The `Create` method returns a [SagaBuilder]( https://github.com/alonf/Sagaway/blob/master/Sagaway/Sagaway.Builder/SagaBuilder.cs) instance.

Like most builders, the SagaBuilder uses a fluent interface. The first four methods set the Saga output report functions:

```csharp
var saga = Saga<CarReservationActorOperations>.Create(_actorHost.Id.ToString(), this, _logger)
    .WithOnSuccessCompletionCallback(OnSuccessCompletionCallbackAsync)
    .WithOnRevertedCallback(OnRevertedCallbackAsync)
    .WithOnFailedRevertedCallback(OnFailedRevertedCallbackAsync)
    .WithOnFailedCallback(OnFailedCallbackAsync)
```

The `OnSuccessCompletionCallbackAsync`, `OnRevertedCallbackAsync`, and `OnFailedRevertedCallbackAsync` mark the end of the Saga. `OnFailedCallbackAsync` marks the beginning of the compensation phase if at least one of the operations has an Undo function.

The code in these functions usually issues an HTTP request or a Dapr binding call to inform other services about the Saga outcome.

Now, we provide the list of operations and their corresponding configuration and callback functions. The order of the operation in the Builder is not essential. To set the order of execution, use the dependency option.

```csharp
.WithOperation(CarReservationActorOperations.CarBooking)
.WithDoOperation(BookCarReservationAsync)
.WithMaxRetries(3)
.WithRetryIntervalTime(TimeSpan.FromMinutes(2))
.WithValidateFunction(ValidateBookCarReservationAsync)
.WithUndoOperation(RevertBookCarReservationAsync)
.WithMaxRetries(3)
.WithUndoRetryInterval(TimeSpan.FromMinutes(10))
.WithValidateFunction(ValidateRevertBookCarReservationAsync)
```

The `WithOperation` takes the operation enum value; it returns a `SagaDoOperationBuilder` instance that enables configuring the success path action.

`WithDoOperation` takes the function that issues an async call to the micro-service:

```csharp
private async Task BookCarReservationAsync()
{
    _logger.LogInformation("Booking car reservation for reservation id: {reservationId}", _reservationInfo!.ReservationId);

    var carReservationRequest = new CarReservationRequest
    {
        ActionType = CarReservationRequestActionType.Reserve,
        CarClass = _reservationInfo!.CarClass,
        CustomerName = _reservationInfo.CustomerName,
        ReservationId = _reservationInfo.ReservationId,
    };

    await DaprClient.InvokeBindingAsync("booking-queue", "create", carReservationRequest,
        GetCallbackMetadata(nameof(OnCarBookingResultAsync)));
}
```

- The last line in the code issues the call utilizing Dapr binding. The last parameter is crucial for enabling seamless callback. More detail about "behind the scenes" later. The Sagaway Dapr host provides the `GetCallbackMetadata()` that ties the specific operation to the callback that informs the action result:

```csharp
private async Task OnCarBookingResultAsync(ReservationOperationResult reservationOperationResult)
 {
     if (reservationOperationResult.ReservationId != _reservationInfo?.ReservationId)
     {
         _logger.LogError("The reservation id in the response does not match the current reservation id.");
         return;
     }

     await ReportCompleteOperationOutcomeAsync(CarReservationActorOperations.CarBooking, reservationOperationResult.IsSuccess);
 }
```

The `ReportCompleteOperationOutcomeAsync` informs the Saga about the operation outcome. The Saga uses this information to decide whether to proceed to the next operation or to switch to the compensation phase.
There are two optional arguments in the `ReportCompleteOperationOutcomeAsync` function, the `failFast` and the `fastSuccess`:

```csharp
/// <summary>
        /// Implementer should call this method to inform the outcome of an operation
        /// </summary>
        /// <param name="operation">The operation</param>
        /// <param name="success">Success or failure</param>
        /// <param name="failFast">If true, fail the Saga, stop retries and start revert</param>
        /// <param name="fastSuccess">Inform a success of the operation, complete the saga. Not started operations marked as successful</param>
        /// <returns>Async operation</returns>
        Task ReportOperationOutcomeAsync(TEOperations operation, bool success, bool failFast = false, bool fastSuccess = false);
```

Pass `true` value to the `failFast` argument to fail the saga and start the compensassion process.
Pass `true` value to the `fastSuccess` argument to inform the Saga that the operation succeeded and to complete the Saga. The Saga will mark all the operations that have not started as successful.

- The following optional functions configure the retry behavior:

```csharp
.WithMaxRetries(3)
.WithRetryIntervalTime(TimeSpan.FromMinutes(2))
```

The number of retries before the Saga switches to the compensation state and the time to set the reminder to wake the Saga to retry the call. The interval can be constant, or a function returning the interval according to the retry attempt. You can use it to create an exponential backoff wait. Look at the `ExponentialBackoff` utility class for predefined functions:

```csharp
WithRetryIntervalTime(ExponentialBackoff.InMinutes())
 //or
WithRetryIntervalTime(ExponentialBackoff.InSeconds())
```

- The optional: `WithValidateFunction(ValidateBookCarReservationAsync)` sets a function that the Saga can call to validate an outcome of an operation. If the operation has not informed a result, and the retry timeout has elapsed, the Saga issues a call to get the success or failure result. If the remote service does not provide a callback mechanism, use this function for polling for the outcome. 

```csharp
private async Task<bool> ValidateBookCarReservationAsync()
{
    var reservationId = _reservationInfo!.ReservationId;

    try
    {
        var reservationState =
            await DaprClient.InvokeMethodAsync<BookingDto.ReservationState>(HttpMethod.Get, "booking-management",
                $"/reservations/{reservationId}");

        return reservationState.IsReserved;
    }
    catch (Exception e)
    {
        _logger.LogError(e, "Error in ValidateBookCarReservationAsync for reservation id: {reservationId}", reservationId);
        return false;
    }
}
```

- The `WithValidateFunction` returns the `SagaDoOperationBuilder`
That allows us to call `WithUndoOperation` and provide parameters and callback functions similar to the Do operation:

```csharp
.WithUndoOperation(RevertBookCarReservationAsync)
.WithMaxRetries(3)
.WithUndoRetryInterval(TimeSpan.FromMinutes(10))
.WithValidateFunction(ValidateRevertBookCarReservationAsync)
```

- In many cases, the undo validate function is the opposite of the do validate:

```csharp
private async Task<bool> ValidateRevertBookCarReservationAsync()
 {
     _logger.LogInformation("Validating revert car reservation for reservation id: {reservationId}", _reservationInfo!.ReservationId);
     return !await ValidateBookCarReservationAsync();
 }
```

- If you need to dictate an order of operations, for example, in the example above, we decided to call the billing only after the first two operations succeeded; since refund as a compensation has its problems and costs, you can provide the dependent operations:

```csharp
WithOperation(CarReservationActorOperations.Billing)
            .WithDoOperation(BillReservationAsync)
            .WithMaxRetries(3)
            .WithRetryIntervalTime(TimeSpan.FromSeconds(10))
            .WithValidateFunction(ValidateBillReservationAsync)
            .WithPreconditions(CarReservationActorOperations.CarBooking | CarReservationActorOperations.InventoryReserving)
            .WithUndoOperation(RevertBillReservationAsync)
            .WithMaxRetries(3)
            .WithUndoRetryInterval(TimeSpan.FromSeconds(10))
            .WithValidateFunction(ValidateRevertBillReservationAsync)
```

- The `.WithPreconditions(CarReservationActorOperations.CarBooking | CarReservationActorOperations.InventoryReserving)` sets the execution order.

Look at the [complete Actor implementation]( https://github.com/alonf/Sagaway/blob/master/Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationManager/Actors/CarReservation/CarReservationActor.cs).

## Implementing a participant Service

The Saga can call any service, utilizing any means of communication. However, to enable auto-routing, the Sagaway framework provides a mechanism that propagates the actor identity and the callback function name so the callback message can be dispatched to the correct function. If you decide not to use the Sagaway auto-routing, you must inform the Saga of each call outcome or rely on the Saga polling mechanism. If you want a more straightforward implementation, follow these instructions:

- Create an ASP.NET Minimal API project
- Add NuGet packages for Dapr, for Open Telemetry and the `Sagaway.Callback.Propagator` to support auto callback dispatch
- Add the following services:

```csharp
// Register DaprClient that supports Sagaway context propagator
builder.Services.AddControllers().AddDaprWithSagawayContextPropagator().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});
```

Again, the json serialization is optional. The vital function is ` AddDaprWithSagawayContextPropagator ` that Add the dapper service and configures it to use a custom HttpClient that takes care of copying special Http Headers from the request headers to the callback headers. These headers contain the Actor and callback method identities. More about it later.
If you need to customize the DaprClient, use the `AddSagawayContextPropagator` function instead.

After mapping the APIs, add the: `app.UseSagawayContextPropagator();` 
This function injects a middleware that keeps the incoming special headers in an AsyncCall object, making them available for the callback mechanism.

Now, the API can leverage the framework:

```csharp
app.MapPost("/booking-queue", async (
        [FromBody] CarReservationRequest request,
        [FromHeader(Name = "x-sagaway-message-dispatch-time")] string messageDispatchTimeHeader,
        [FromServices] ILogger<Program> logger,
        [FromServices] ICallbackBindingNameProvider callbackBindingNameProvider,
        [FromServices] DaprClient daprClient) =>
    {
       ...
    }
```

The `x-sagaway-message-dispatch-time` provides the dispatch time of the message. You can use it for handling out-of-order messaging. The `ICallbackBindingNameProvider callbackBindingNameProvider` service provides the Dapr binding name of the callback component that the Actor host uses for accepting the outcome of a service:

```csharp
await daprClient.InvokeBindingAsync(callbackBindingNameProvider.CallbackBindingName, "create", reservationOperationResult);
```

That is all you need to do to use the Sagaway with Dapr services.

### Is the Sagaway Auto-Dispaching Framework an MSA Anti-Pattern?

A Micro Service should be autonomous; it should define its service contract. Other services need to call it, using the contract it dictated.

The Auto-Dispaching framework is based on several HTTP Custom headers that can be treated as the way the service defines a contract where it asks for information that enables it to call back the caller. It is an optional cross-cutting concern mechanism, and we can treat the header schema as part of the service contract. In the first version of Sagaway, I didn’t provide such a mechanism. I discovered that it is error-prone and requires much effort to manage asynchronous communication manually. By adding this middleware, I reduced the amount of boilerplate radically. If you don’t want to add the dependency of the Sagaway code to each service, you can use the incoming custom HTTP headers directly.

## Understanding the Sagaway Framework Design

The Sagaway framework is a specialized library for orchestrating distributed transactions using the Saga pattern in .NET applications. At the heart of Sagaway is a core library meticulously designed to be a pure C# implementation, which stands independent of any other specific technologies. The independence is critical as it allows the framework to be versatile and adaptable, capable of being integrated into various hosting environments while only necessitating that the host implements a specific interface.

The foundation of Sagaway is established upon two primary interfaces: `ISaga<TOperations>` and `ISagaSupport`. These interfaces are pivotal as they outline the essential operations and supports required by the Saga lifecycle.

### [`ISaga<TOperations>`]( https://github.com/alonf/Sagaway/blob/master/Sagaway/ISaga.cs)

This interface is generic, parameterized by an `Enum` representing the set of operations or steps within a Saga. It defines the life cycle of a Saga, including the initiation of execution through `RunAsync` and various properties that indicate the Saga's current state, such as `InProgress`, `Succeeded`, `Failed`, `Reverted`, and `RevertFailed`. It includes events like `OnSagaCompleted` to notify when the Saga has concluded its operations. The interface also prescribes methods to inform the Saga of specific events like activation, deactivation, operation outcomes, undo operation outcomes, and reminders, ensuring that the Saga can react accordingly.

### [`ISagaSupport`]( https://github.com/alonf/Sagaway/blob/master/Sagaway/ISagaSupport.cs)

ISagaSupport is an interface that the host must implement. It defines the support structure a Saga requires from its hosting environment to function correctly. It includes capabilities for reminders, state persistence, state retrieval, and an asynchronous locking mechanism if required for thread safety. In the Sagaway implementation, there are two implementations of the `ISagaSupport` interface, a simple [`SagaSupportOperations`]( https://github.com/alonf/Sagaway/blob/master/Sagaway.Tests/SagaSupportOperations.cs) class – a pure C# implementation that is used for the Sagaway testing framework, and a [`DaprActorHost<TOperations>`]( https://github.com/alonf/Sagaway/blob/master/Sagaway.Tests/SagaSupportOperations.cs) that hosts the Saga in a Dapr Actor.

The design of these interfaces ensures that the Sagaway framework remains decoupled from any hosting technology. By defining clear contracts for implementing Sagas and their supportive operations, Sagaway can be utilized in a variety of runtime environments, from serverless functions and containers to traditional server-based applications.

### The ISagaSupport interface

```csharp
using System.Text.Json.Nodes;

namespace Sagaway
{
    /// <summary>
    /// Provide the required methods for a Saga
    /// </summary>
    public interface ISagaSupport
    {
        /// <summary>
        /// A function to set reminder. The reminder should bring the saga back to life and call the OnReminder function
        /// With the reminder name.
        /// </summary>
        /// <param name="reminderName">A unique name for the reminder</param>
        /// <param name="dueTime">The time to re-activate the saga</param>
        /// <returns>Async operation</returns>
        Task SetReminderAsync(string reminderName, TimeSpan dueTime);

        /// <summary>
        /// A function to cancel a reminder
        /// </summary>
        /// <param name="reminderName">The reminder to cancel</param>
        /// <returns>Async operation</returns>
        Task CancelReminderAsync(string reminderName);

        /// <summary>
        /// Provide a mechanism to persist the saga state
        /// </summary>
        /// <param name="sagaId">The saga unique id</param>
        /// <param name="state">The saga serialized state</param>
        /// <returns>Async operation</returns>
        Task SaveSagaStateAsync(string sagaId, JsonObject state);

        /// <summary>
        /// Provide a mechanism to load the saga state
        /// </summary>
        /// <param name="sagaId">The saga unique id</param>
        /// <returns>The serialized saga state</returns>
        Task<JsonObject?> LoadSagaAsync(string sagaId);

        /// <summary>
        /// Provide the lock for a thread-safe saga if required
        /// Can utilize the <see cref="ReentrantAsyncLock"/> or <see cref="NonLockAsync"/>
        /// </summary>
        /// <returns>A lock implementor</returns>
        ILockWrapper CreateLock();

        private static readonly ITelemetryAdapter NullTelemetryAdapterInstance = new NullTelemetryAdapter();

        /// <summary>
        /// Provide the telemetry adapter for the saga
        /// </summary>
        ITelemetryAdapter TelemetryAdapter  => NullTelemetryAdapterInstance;
    }
}
```

The saga requirements from the host are straightforward. The `SetReminderAsync` and the `CancelReminderAsync` allow the saga to set or cancel a reminder from the host or cancel it. For a simple host, the remainder is just a callback function that must be triggered after a due period. For more complex hosts such as Dapr Actor, the reminder may also reactivate the Actor and the saga, bringing it back to life on one of the hosted services. When the saga wakes up, the `ReBuildSaga` method is called to create the Saga object graph, i.e., the Saga operations. After that, the saga is loaded from the state by calling the `LoadSagaState` methods of the `ISagaSupport`. The `LoadSagaState` returns a Json representing the last state of the saga, i.e., the result of the already executed operations. The saga uses the `SaveSagaStateAsync` to store the information when it is called by the host in case the host is deactivated.
The next method in the interface is the `CreateLock`, which should provide an `ILockWrapper` instance. The saga uses this interface to obtain an async lock mechanism. If the host is a single-threaded host that allows only a single call to run concurrently, as the Dapr Actor does, then the host uses the [`NonLockAsync`]( https://github.com/alonf/Sagaway/blob/master/Sagaway/NonLockAsync.cs) class, otherwise it uses the [`ReentrantAsyncLock`]( https://github.com/alonf/Sagaway/blob/master/Sagaway/ReentrantAsyncLock.cs) class. The last property in the interface is the `TelemetryAdapter`. The host can leave the defualt `NullTelemetryAdapterInstance` or provide an implementation for the [`ITelemetryAdapter`](https://github.com/alonf/Sagaway/blob/master/Sagaway/Telemetry/ITelemetryAdapter.cs). For OpenTelemetry, there is a predefined implementation [`OpenTelemetryAdapter`](https://github.com/alonf/Sagaway/blob/master/Sagaway.OpenTelemetry/OpenTelemetryAdapter.cs) that can be injected using the [`AddSagawayOpenTelemetry`](https://github.com/alonf/Sagaway/blob/master/Sagaway.OpenTelemetry/TelemetryExtensions.cs) extension method.

The host uses the [`ISaga`](https://github.com/alonf/Sagaway/blob/master/Sagaway/ISaga.cs) interface methods to inform that saga about deactivation ` InformDeactivatedAsync `, activation: ` InformActivatedAsync `  and about a reminder that goes off `ReportReminderAsync`. The host can also use the interface to know the Saga status: ` InProgress `,` Succeeded `,` Failed `,` Reverted `, and `RevertFailed` and can know that the saga is done with the `OnSagaCompleted` event. The host can execute the saga with `RunAsync` and inform Saga operation results with `ReportOperationOutcomeAsync` and `ReportUndoOperationOutcomeAsync`.

The [`DaprActorHost`](https://github.com/alonf/Sagaway/tree/master/Sagaway.Hosts.DaprActorHost)implements the required functionality and hides the complexity from the developer that uses it. If you need to create your host, see the `DaprActorHost` implementation.

### The [`DaprActorHost`](https://github.com/alonf/Sagaway/tree/master/Sagaway.Hosts.DaprActorHost) class

```csharp
public abstract class DaprActorHost<TEOperations> : Actor, IRemindable, ISagaSupport, ISagawayActor
    where TEOperations : Enum
{
   ...
   
}
```

The `DaprActorHost` class derived from the Dapr [`Actor`](https://github.com/dapr/dotnet-sdk/blob/master/src/Dapr.Actors/Runtime/Actor.cs) class and implements the Dapr Actor [`IRemindable`](https://github.com/dapr/dotnet-sdk/blob/master/src/Dapr.Actors/Runtime/IRemindable.cs) to set and invoke by a reminder callback. The next two interfaces are the `ISagaSupport` that we have discussed and the [`ISagawayActor](https://github.com/alonf/Sagaway/blob/master/Sagaway.Callback.Router/ISagawayActor.cs) which enables the auto-routing of callback asynchronous results from external services when needed.

Most of the code of the `DaprActorHost` is a delegation either to the underline Sagaway [`Saga`](https://github.com/alonf/Sagaway/blob/master/Sagaway/Saga.cs) class or for the Dapr [`Actor`](https://github.com/dapr/dotnet-sdk/blob/master/src/Dapr.Actors/Runtime/Actor.cs) class. The only exception is the `DispatchCallbackAsync` that has the implementation using reflection to call back to complete the asynchronous Saga step.



### The Saga Core

#### The Saga class

##### Thread Safety

#### The SagaOperation and SagaAction classes

#### The Saga Flow

### The Saga Core Testing Framework

### The Dapr Host

#### The Auto-Dispatcher Mechanism

using Dapr.Actors.Runtime;
using Sagaway.Hosts;
using Sagaway.ReservationDemo.ReservationManager.Actors.BillingDto;
using Sagaway.ReservationDemo.ReservationManager.Actors.BookingDto;
using Sagaway.ReservationDemo.ReservationManager.Actors.InventoryDto;

namespace Sagaway.ReservationDemo.ReservationManager.Actors.CarReservation;

[Actor(TypeName = "CarReservationActor")]
// ReSharper disable once UnusedType.Global
// ReSharper disable once ClassNeverInstantiated.Global
public class CarReservationActor : DaprActorHost<CarReservationActorOperations>, ICarReservationActor
{
    private readonly ILogger<CarReservationActor> _logger;
    private readonly ActorHost _actorHost;
    private ReservationInfo? _reservationInfo;

    // ReSharper disable once ConvertToPrimaryConstructor
    public CarReservationActor(ActorHost host, ILogger<CarReservationActor> logger, IServiceProvider? serviceProvider)
        : base(host, logger, serviceProvider)
    {
        _actorHost = host;
        _logger = logger;
    }

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


    #region Saga Activation methods

    protected override string GetCallbackBindingName()
    {
        return "reservation-response-queue";
    }

    protected override async Task OnActivateSagaAsync()
    {
        _reservationInfo = (await StateManager.TryGetStateAsync<ReservationInfo>("reservationInfo")).Value;
    }

    public async Task ReserveCarAsync(ReservationInfo reservationInfo)
    {
        try
        {
            _logger.LogInformation(
                "ReserveCarAsync called with carClass: {carClass}, reservationId: {reservationId}, customerName: {customerName}",
                reservationInfo.CarClass, reservationInfo.ReservationId, reservationInfo.CustomerName);

            await StateManager.SetStateAsync("reservationInfo", reservationInfo);

            _reservationInfo = reservationInfo;

            await SagaRunAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in ReserveCarAsync for reservation id: {reservationId}", reservationInfo.ReservationId);
            throw;
        }
    }

    #endregion


    #region Booking Car Reservation metods

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

    private async Task OnCarBookingResultAsync(ReservationOperationResult reservationOperationResult)
    {
        if (reservationOperationResult.ReservationId != _reservationInfo?.ReservationId)
        {
            _logger.LogError("The reservation id in the response does not match the current reservation id.");
            return;
        }

        await ReportCompleteOperationOutcomeAsync(CarReservationActorOperations.CarBooking, reservationOperationResult.IsSuccess);
    }

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

    private async Task RevertBookCarReservationAsync()
    {
        _logger.LogInformation("Reverting car reservation for reservation id: {reservationId}", _reservationInfo!.ReservationId);
        var carReservationCancellationRequest = new CarReservationRequest
        {
            ActionType = CarReservationRequestActionType.Cancel,
            CarClass = _reservationInfo!.CarClass,
            CustomerName = _reservationInfo.CustomerName,
            ReservationId = _reservationInfo.ReservationId,
        };
        await DaprClient.InvokeBindingAsync("booking-queue", "create", carReservationCancellationRequest,
            GetCallbackMetadata(nameof(OnRevertBookCarReservationAsync)));
    }

    private async Task OnRevertBookCarReservationAsync(ReservationOperationResult reservationOperationResult)
    {
        if (reservationOperationResult.ReservationId != _reservationInfo?.ReservationId)
        {
            _logger.LogError("The reservation id in the response does not match the current reservation id.");
            return;
        }

        await ReportUndoOperationOutcomeAsync(CarReservationActorOperations.CarBooking, reservationOperationResult.IsSuccess);
    }

    private async Task<bool> ValidateRevertBookCarReservationAsync()
    {
        _logger.LogInformation("Validating revert car reservation for reservation id: {reservationId}", _reservationInfo!.ReservationId);
        return !await ValidateBookCarReservationAsync();
    }

    #endregion

    #region Inventory Reserving methods

    private async Task ReserveInventoryAsync()
    {
        _logger.LogInformation("Reserving inventory for reservation id: {reservationId}",
            _reservationInfo!.ReservationId);
        var carInventoryRequest = new CarInventoryRequest()
        {
            ActionType = CarInventoryRequestActionType.Reserve,
            CarClass = _reservationInfo!.CarClass,
            OrderId = _reservationInfo.ReservationId,
        };

        await DaprClient.InvokeBindingAsync("inventory-queue", "create", carInventoryRequest,
            GetCallbackMetadata(nameof(OnReserveInventoryResultAsync)));
    }

    private async Task OnReserveInventoryResultAsync(ReservationOperationResult reservationOperationResult)
    {
        if (reservationOperationResult.ReservationId != _reservationInfo?.ReservationId)
        {
            _logger.LogError("The reservation id in the response does not match the current reservation id.");
            return;
        }

        await ReportCompleteOperationOutcomeAsync(CarReservationActorOperations.InventoryReserving, reservationOperationResult.IsSuccess);
    }

    private async Task<bool> ValidateReserveInventoryAsync()
    {
        _logger.LogInformation("Validating inventory reservation for reservation id: {reservationId}",
                       _reservationInfo!.ReservationId);

        var orderId = _reservationInfo!.ReservationId;

        try
        {
            var reservationState =
                await DaprClient.InvokeMethodAsync<InventoryDto.ReservationState>(HttpMethod.Get,
                    "inventory-management", $"/reservation-state/{orderId}");

            return reservationState.IsReserved;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in ValidateReserveInventoryAsync for reservation id: {reservationId}", orderId);
            return false;
        }
    }

    private async Task RevertReserveInventoryAsync()
    {
        _logger.LogInformation("Reverting inventory reservation for reservation id: {reservationId}",
                       _reservationInfo!.ReservationId);
        var carInventoryRequest = new CarInventoryRequest()
        {
            ActionType = CarInventoryRequestActionType.Cancel,
            CarClass = _reservationInfo!.CarClass,
            OrderId = _reservationInfo.ReservationId,
        };


        await DaprClient.InvokeBindingAsync("inventory-queue", "create", carInventoryRequest,
            GetCallbackMetadata(nameof(OnRevertReserveInventoryAsync)));
    }

    private async Task OnRevertReserveInventoryAsync(ReservationOperationResult reservationOperationResult)
    {
        if (reservationOperationResult.ReservationId != _reservationInfo?.ReservationId)
        {
            _logger.LogError("The reservation id in the response does not match the current reservation id.");
            return;
        }

        await ReportUndoOperationOutcomeAsync(CarReservationActorOperations.InventoryReserving, reservationOperationResult.IsSuccess);
    }

    private async Task<bool> ValidateRevertReserveInventoryAsync()
    {
        _logger.LogInformation("Validating revert inventory reservation for reservation id: {reservationId}",
                                  _reservationInfo!.ReservationId);

        return !await ValidateReserveInventoryAsync();
    }

    #endregion

    #region Billing methods
    private async Task BillReservationAsync()
    {
        _logger.LogInformation("Billing for reservation id: {reservationId}", _reservationInfo!.ReservationId);

        var billingRequest = new BillingRequest
        {
            ReservationId = _reservationInfo!.ReservationId,
            CustomerName = _reservationInfo.CustomerName,
            ActionType = BillingRequestActionType.Charge,
            CarClass = _reservationInfo.CarClass
        };

        await DaprClient.InvokeBindingAsync("billing-queue", "create", billingRequest);
    }

    private async Task<bool> ValidateBillReservationAsync()
    {
        _logger.LogInformation("Validating billing for reservation id: {reservationId}", _reservationInfo!.ReservationId);

        var reservationId = _reservationInfo!.ReservationId;
        try
        {
            var billingState =
                await DaprClient.InvokeMethodAsync<BillingState>(HttpMethod.Get, "billing-management",
                    $"/billing-status/{reservationId}");

            return billingState.Status == "Charged";
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in ValidateBillReservationAsync for reservation id: {reservationId}", reservationId);
            return false;
        }
    }

    private async Task RevertBillReservationAsync()
    {
        _logger.LogInformation("Reverting billing for reservation id: {reservationId}", _reservationInfo!.ReservationId);

        var refundRequest = new BillingRequest
        {
            ReservationId = _reservationInfo!.ReservationId,
            CustomerName = _reservationInfo.CustomerName,
            ActionType = BillingRequestActionType.Refund,
            CarClass = _reservationInfo.CarClass
        };

        await DaprClient.InvokeBindingAsync("billing-queue", "create", refundRequest);
    }

    private async Task<bool> ValidateRevertBillReservationAsync()
    {
        _logger.LogInformation("Validating revert billing for reservation id: {reservationId}", _reservationInfo!.ReservationId);

        return !await ValidateBillReservationAsync();
    }

    #endregion

    #region Saga Completion Methods

    private async void OnFailedRevertedCallbackAsync(string sagaLog)
    {
        _logger.LogError("The car reservation has failed and left some unused resources.");
        _logger.LogError("The car reservation log:" + Environment.NewLine + sagaLog);

        await Task.CompletedTask;
    }

    private async void OnRevertedCallbackAsync(string sagaLog)
    {
        _logger.LogError("The car reservation has failed and all resources are deleted.");
        _logger.LogError("The car reservation log:" + Environment.NewLine + sagaLog);

        await Task.CompletedTask;
    }

    private async void OnFailedCallbackAsync(string sagaLog)
    {
        _logger.LogError("The car reservation has failed starting reverting resources.");
        _logger.LogError("The car reservation log:" + Environment.NewLine + sagaLog);

        await Task.CompletedTask;
        //Option: Send a message to the customer
    }

    private async void OnSuccessCompletionCallbackAsync(string sagaLog)
    {
        _logger.LogInformation("The car reservation has succeeded.");
        _logger.LogInformation("The car reservation log:" + Environment.NewLine + sagaLog);

        await Task.CompletedTask;
        //Option: Send a message to the customer
    }

    private async Task OnSagaCompletedAsync(object? _, SagaCompletionEventArgs e)
    {
        _logger.LogInformation($"Saga {e.SagaId} completed with status {e.Status}");
        await Task.CompletedTask;
    }

    #endregion


}
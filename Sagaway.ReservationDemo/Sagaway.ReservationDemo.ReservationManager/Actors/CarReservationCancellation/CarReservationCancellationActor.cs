using Dapr.Actors.Runtime;
using Sagaway.Hosts;
using Sagaway.ReservationDemo.ReservationManager.Actors.BillingDto;
using Sagaway.ReservationDemo.ReservationManager.Actors.BookingDto;
using Sagaway.ReservationDemo.ReservationManager.Actors.InventoryDto;

namespace Sagaway.ReservationDemo.ReservationManager.Actors.CarReservationCancellation;

[Actor(TypeName = "CarReservationCancellationActor")]
// ReSharper disable once UnusedType.Global
// ReSharper disable once ClassNeverInstantiated.Global
public class CarReservationCancellationActor : DaprActorHost<CarCancelReservationActorOperations>, ICarReservationCancellationActor
{
    private readonly ILogger<CarReservationCancellationActor> _logger;
    private readonly ActorHost _actorHost;
    private ReservationInfo? _reservationInfo;

    // ReSharper disable once ConvertToPrimaryConstructor
    public CarReservationCancellationActor(ActorHost host, ILogger<CarReservationCancellationActor> logger
            ,IServiceProvider serviceProvider)
        : base(host, logger, serviceProvider)
    {
        _actorHost = host;
        _logger = logger;
    }

    protected override ISaga<CarCancelReservationActorOperations> ReBuildSaga()
    {
        var saga = Saga<CarCancelReservationActorOperations>.Create(_actorHost.Id.ToString(), this, _logger)
            .WithOnSuccessCompletionCallback(OnSuccessCompletionCallback)
            .WithOnRevertedCallback(OnRevertedCallback)
            .WithOnFailedRevertedCallback(OnFailedRevertedCallback)
            .WithOnFailedCallback(OnFailedCallback)

            .WithOperation(CarCancelReservationActorOperations.CancelBooking)
            .WithDoOperation(CancelCarBookingAsync)
            .WithMaxRetries(3)
            .WithRetryIntervalTime(TimeSpan.FromMinutes(2))
            .WithValidateFunction(ValidateCarBookingCanceledAsync)
            .WithUndoOperation(RevertCancelCarBookingAsync)
            .WithMaxRetries(3)
            .WithUndoRetryInterval(TimeSpan.FromMinutes(10))
            .WithValidateFunction(ValidateCarBookingCanceledRevertAsync)

            .WithOperation(CarCancelReservationActorOperations.CancelInventoryReserving)
            .WithDoOperation(CancelInventoryReservationAsync)
            .WithMaxRetries(3)
            .WithRetryIntervalTime(TimeSpan.FromMinutes(2))
            .WithValidateFunction(ValidateInventoryReservationCanceledAsync)
            .WithUndoOperation(RevertInventoryReservationCancellingAsync)
            .WithMaxRetries(3)
            .WithUndoRetryInterval(TimeSpan.FromMinutes(10))
            .WithValidateFunction(ValidateRevertInventoryReservationCancellingAsync)

            .WithOperation(CarCancelReservationActorOperations.Refund)
            .WithDoOperation(RefundReservationBillingAsync)
            .WithMaxRetries(3)
            .WithRetryIntervalTime(TimeSpan.FromSeconds(10))
            .WithValidateFunction(ValidateRefundReservationAsync)
            .WithPreconditions(CarCancelReservationActorOperations.CancelBooking | CarCancelReservationActorOperations.CancelInventoryReserving)
            .WithUndoOperation(ChargeReservationAsync)
            .WithMaxRetries(3)
            .WithUndoRetryInterval(TimeSpan.FromSeconds(10))
            .WithValidateFunction(ValidateChargingReservationAsync)

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
        if (_reservationInfo == null)
        {
            _logger.LogInformation("The reservation id is empty. Assuming actor activation.");
            _reservationInfo = (await StateManager.TryGetStateAsync<ReservationInfo>("reservationInfo")).Value;
        }
    }

    public async Task CancelCarReservationAsync(ReservationInfo reservationInfo)
    {
        try
        {
            _logger.LogInformation(
                "CancelCarReservationAsync called with carClass: {carClass}, reservationId: {reservationId}, customerName: {customerName}",
                reservationInfo.CarClass, reservationInfo.ReservationId, reservationInfo.CustomerName);

            await StateManager.SetStateAsync("reservationInfo", reservationInfo);

            _reservationInfo = reservationInfo;

            await SagaRunAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in CancelCarReservationAsync for reservation id: {reservationId}", reservationInfo.ReservationId);
            throw;
        }
    }

    #endregion


    #region Cancelling Car booking metods

    private async Task CancelCarBookingAsync()
    {
        _logger.LogInformation("Cancel car booking for reservation id: {reservationId}", _reservationInfo!.ReservationId);

        var carReservationCancelRequest = new CarReservationRequest
        {
            ActionType = CarReservationRequestActionType.Cancel,
            CarClass = _reservationInfo!.CarClass,
            CustomerName = _reservationInfo.CustomerName,
            ReservationId = _reservationInfo.ReservationId,
        };

        await DaprClient.InvokeBindingAsync("booking-queue", "create", carReservationCancelRequest,
            GetCallbackMetadata(nameof(OnCarBookingCancellationResultAsync)));
    }

    private async Task OnCarBookingCancellationResultAsync(ReservationOperationResult reservationOperationResult)
    {
        if (reservationOperationResult.ReservationId != _reservationInfo?.ReservationId)
        {
            _logger.LogError("The reservation id in the response does not match the current reservation id.");
            return;
        }

        await ReportCompleteOperationOutcomeAsync(CarCancelReservationActorOperations.CancelBooking, reservationOperationResult.IsSuccess);
    }

    private async Task<bool> ValidateCarBookingCanceledAsync()
    {
        var reservationId = _reservationInfo!.ReservationId;

        try
        {
            var reservationState =
                await DaprClient.InvokeMethodAsync<BookingDto.ReservationState>(HttpMethod.Get, "booking-management",
                    $"/reservations/{reservationId}");

            return !reservationState.IsReserved;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in ValidateCarBookingCanceledAsync for reservation id: {reservationId}", reservationId);
            return false;
        }
    }

    private async Task RevertCancelCarBookingAsync()
    {
        _logger.LogInformation("re-reserve car for reservation id: {reservationId}", _reservationInfo!.ReservationId);
        var carReservationRequest = new CarReservationRequest
        {
            ActionType = CarReservationRequestActionType.Reserve,
            CarClass = _reservationInfo!.CarClass,
            CustomerName = _reservationInfo.CustomerName,
            ReservationId = _reservationInfo.ReservationId,
        };
        await DaprClient.InvokeBindingAsync("booking-queue", "create", carReservationRequest,
            GetCallbackMetadata(nameof(OnBookingCarReservationAsync)));
    }

    private async Task OnBookingCarReservationAsync(ReservationOperationResult reservationOperationResult)
    {
        if (reservationOperationResult.ReservationId != _reservationInfo?.ReservationId)
        {
            _logger.LogError("The reservation id in the response does not match the current reservation id.");
            return;
        }

        await ReportUndoOperationOutcomeAsync(CarCancelReservationActorOperations.CancelBooking, reservationOperationResult.IsSuccess);
    }

    private async Task<bool> ValidateCarBookingCanceledRevertAsync()
    {
        _logger.LogInformation("Validating revert car reservation cancellation for reservation id: {reservationId}", _reservationInfo!.ReservationId);
        return !await ValidateCarBookingCanceledAsync();
    }

    #endregion

    #region Inventory Reserving methods

    private async Task CancelInventoryReservationAsync()
    {
        _logger.LogInformation("Cancelling inventory reservation for reservation id: {reservationId}",
            _reservationInfo!.ReservationId);
        var carInventoryRequest = new CarInventoryRequest()
        {
            ActionType = CarInventoryRequestActionType.Cancel,
            CarClass = _reservationInfo!.CarClass,
            OrderId = _reservationInfo.ReservationId,
        };

        await DaprClient.InvokeBindingAsync("inventory-queue", "create", carInventoryRequest,
            GetCallbackMetadata(nameof(OnCancelReserveInventoryResultAsync)));
    }

    private async Task OnCancelReserveInventoryResultAsync(ReservationOperationResult reservationOperationResult)
    {
        if (reservationOperationResult.ReservationId != _reservationInfo?.ReservationId)
        {
            _logger.LogError("The reservation id in the response does not match the current reservation id.");
            return;
        }

        await ReportCompleteOperationOutcomeAsync(CarCancelReservationActorOperations.CancelInventoryReserving, reservationOperationResult.IsSuccess);
    }

    private async Task<bool> ValidateInventoryReservationCanceledAsync()
    {
        _logger.LogInformation("Validating inventory reservation canceled for reservation id: {reservationId}",
                       _reservationInfo!.ReservationId);

        var orderId = _reservationInfo!.ReservationId;

        try
        {
            var reservationState =
                await DaprClient.InvokeMethodAsync<InventoryDto.ReservationState>(HttpMethod.Get,
                    "inventory-management", $"/reservation-state/{orderId}");

            return !reservationState.IsReserved;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in ValidateInventoryReservationCanceledAsync for reservation id: {reservationId}", orderId);
            return false;
        }
    }

    private async Task RevertInventoryReservationCancellingAsync()
    {
        _logger.LogInformation("re-reserve reservation id: {reservationId}",
                       _reservationInfo!.ReservationId);
        var carInventoryRequest = new CarInventoryRequest()
        {
            ActionType = CarInventoryRequestActionType.Reserve,
            CarClass = _reservationInfo!.CarClass,
            OrderId = _reservationInfo.ReservationId,
        };


        await DaprClient.InvokeBindingAsync("inventory-queue", "create", carInventoryRequest,
            GetCallbackMetadata(nameof(OnReserveInventoryAsync)));
    }

    private async Task OnReserveInventoryAsync(ReservationOperationResult reservationOperationResult)
    {
        if (reservationOperationResult.ReservationId != _reservationInfo?.ReservationId)
        {
            _logger.LogError("The reservation id in the response does not match the current reservation id.");
            return;
        }

        await ReportUndoOperationOutcomeAsync(CarCancelReservationActorOperations.CancelInventoryReserving, reservationOperationResult.IsSuccess);
    }

    private async Task<bool> ValidateRevertInventoryReservationCancellingAsync()
    {
        _logger.LogInformation("Validating revert inventory cancellation for reservation id: {reservationId}",
                                  _reservationInfo!.ReservationId);

        return !await ValidateInventoryReservationCanceledAsync();
    }

    #endregion

    #region Billing methods
    private async Task RefundReservationBillingAsync()
    {
        _logger.LogInformation("Refund for reservation id: {reservationId}", _reservationInfo!.ReservationId);

        var billingRequest = new BillingRequest
        {
            ReservationId = _reservationInfo!.ReservationId,
            CustomerName = _reservationInfo.CustomerName,
            ActionType = BillingRequestActionType.Refund,
            CarClass = _reservationInfo.CarClass
        };

        await DaprClient.InvokeBindingAsync("billing-queue", "create", billingRequest);
    }

    private async Task<bool> ValidateRefundReservationAsync()
    {
        _logger.LogInformation("Validating refund for reservation id: {reservationId}", _reservationInfo!.ReservationId);

        var reservationId = _reservationInfo!.ReservationId;
        try
        {
            var billingState =
                await DaprClient.InvokeMethodAsync<BillingState>(HttpMethod.Get, "billing-management",
                    $"/billing-status/{reservationId}");

            return billingState.Status == "Refund";
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in ValidateRefundReservationAsync for reservation id: {reservationId}",
                reservationId);
            return false;
        }
    }

    private async Task ChargeReservationAsync()
    {
        _logger.LogInformation("Re-charge billing for reservation id: {reservationId}", _reservationInfo!.ReservationId);

        var chargeRequest = new BillingRequest
        {
            ReservationId = _reservationInfo!.ReservationId,
            CustomerName = _reservationInfo.CustomerName,
            ActionType = BillingRequestActionType.Charge,
            CarClass = _reservationInfo.CarClass
        };

        await DaprClient.InvokeBindingAsync("billing-queue", "create", chargeRequest);
    }

    private async Task<bool> ValidateChargingReservationAsync()
    {
        _logger.LogInformation("Validating charging for reservation id: {reservationId}", _reservationInfo!.ReservationId);

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
            _logger.LogError(e, "Error in ValidateChargingReservationAsync for reservation id: {reservationId}",
                reservationId);
            return false;
        }
    }

    #endregion

    #region Saga Completion Methods

    private void OnFailedRevertedCallback(string sagaLog)
    {
        _logger.LogError("The car reservation cancelling has failed and left some unused resources.");
        _logger.LogError("The car reservation cancelling log:" + Environment.NewLine + sagaLog);
    }

    private void OnRevertedCallback(string sagaLog)
    {
        _logger.LogError("The car reservation cancelling has failed and all resources are deleted.");
        _logger.LogError("The car reservation cancelling log:" + Environment.NewLine + sagaLog);
    }

    private void OnFailedCallback(string sagaLog)
    {
        _logger.LogError("The car reservation cancelling has failed starting reverting resources.");
        _logger.LogError("The car reservation cancelling log:" + Environment.NewLine + sagaLog);
        //Option: Send a message to the customer
    }

    private void OnSuccessCompletionCallback(string sagaLog)
    {
        _logger.LogInformation("The car reservation cancelling has succeeded.");
        _logger.LogInformation("The car reservation cancelling log:" + Environment.NewLine + sagaLog);
        //Option: Send a message to the customer
    }

    private async Task OnSagaCompletedAsync(object? _, SagaCompletionEventArgs e)
    {
        _logger.LogInformation($"Saga {e.SagaId} completed with status {e.Status}");

        var metadata = new Dictionary<string, string>
        {
            { "ttlInSeconds", "900" } // 15 minutes TTL
        };

        if (_reservationInfo == null)
        {
            _logger.LogWarning("Cannot save saga log: reservation info is null");
            return;
        }

        var key = _reservationInfo.ReservationId.ToString();

        await DaprClient.SaveStateAsync(
            "statestore",
            $"saga-log-{key}",
            e.Log,
            metadata: metadata);
    }
    #endregion
}
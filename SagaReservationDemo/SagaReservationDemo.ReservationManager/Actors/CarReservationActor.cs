using Dapr.Actors.Runtime;
using Dapr.Client;
using SagaReservationDemo.ReservationManager.Dto.BillingDto;
using SagaReservationDemo.ReservationManager.Dto.BookingDto;
using SagaReservationDemo.ReservationManager.Dto.InventoryDto;
using SagaReservationDemo.ReservationManager.Dto.ReservationDto;
using Sagaway;
using Sagaway.Hosts;

namespace SagaReservationDemo.ReservationManager.Actors;

[Actor(TypeName = "CarReservationActor")]
// ReSharper disable once UnusedType.Global
// ReSharper disable once ClassNeverInstantiated.Global
public class CarReservationActor : DaprActorHost<CarReservationActorOperations>, ICarReservationActor
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<CarReservationActor> _logger;
    private readonly ActorHost _actorHost;
    private ReservationInfo? _reservationInfo;

    // ReSharper disable once ConvertToPrimaryConstructor
    public CarReservationActor(ActorHost host, DaprClient daprClient,
        ILogger<CarReservationActor> logger)
        : base(host, logger)
    {
        _actorHost = host;
        _daprClient = daprClient;
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
            .WithRetryIntervalTime(TimeSpan.FromMinutes(2))
            .WithValidateFunction(ValidateBookCarReservationAsync)
            .WithUndoOperation(RevertBookCarReservationAsync)
            .WithMaxRetries(3)
            .WithUndoRetryInterval(TimeSpan.FromMinutes(10))
            .WithValidateFunction(ValidateRevertBookCarReservationAsync)

            .WithOperation(CarReservationActorOperations.InventoryReserving)
            .WithDoOperation(ReserveInventoryAsync)
            .WithMaxRetries(3)
            .WithRetryIntervalTime(TimeSpan.FromMinutes(2))
            .WithValidateFunction(ValidateReserveInventoryAsync)
            .WithUndoOperation(RevertReserveInventoryAsync)
            .WithMaxRetries(3)
            .WithUndoRetryInterval(TimeSpan.FromMinutes(10))
            .WithValidateFunction(ValidateRevertReserveInventoryAsync)

            .WithOperation(CarReservationActorOperations.Billing)
            .WithDoOperation(BillReservationAsync)
            .WithMaxRetries(3)
            .WithRetryIntervalTime(TimeSpan.FromSeconds(10))
            .WithValidateFunction(ValidateBillReservationAsync)
            .WithPreconditions(CarReservationActorOperations.CarBooking | CarReservationActorOperations.InventoryReserving)
            .WithUndoOperation(RevertBillReservationAsync)
            .WithMaxRetries(3)
            .WithUndoRetryInterval(TimeSpan.FromSeconds(10))
            .WithValidateFunction(ValidateRevertBillReservationAsync)


            .Build();

        saga.OnSagaCompleted += async (s, e) => await OnSagaCompletedAsync(s, e);

        return saga;
    }


    #region Saga Activation methods

    protected override async Task OnActivateSagaAsync()
    {
        if (_reservationInfo == null)
        {
            _logger.LogInformation("The reservation id is empty. Assuming actor activation.");
            _reservationInfo = (await StateManager.TryGetStateAsync<ReservationInfo>("reservationInfo")).Value;
        }
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

    public async Task<bool> HandleReservationActionResultAsync(ReservationOperationResult reservationOperationResult)
    {
        if (reservationOperationResult.ReservationId != _reservationInfo!.ReservationId)
        {
            _logger.LogError("The reservation id is not matching the current reservation id.");
            return false;
        }

        bool isSuccess = reservationOperationResult.IsSuccess;

        switch (reservationOperationResult.Activity)
        {
            case CarReservationActivity.CarBooking:
                await ReportCompleteOperationOutcomeAsync(CarReservationActorOperations.CarBooking, isSuccess);
                break;

            case CarReservationActivity.CancellingCarBooking:
                await ReportUndoOperationOutcomeAsync(CarReservationActorOperations.CarBooking, isSuccess);
                break;

            case CarReservationActivity.InventoryReserving:
                await ReportCompleteOperationOutcomeAsync(CarReservationActorOperations.InventoryReserving, isSuccess);
                break;

            case CarReservationActivity.InventoryCancelling:
                await ReportUndoOperationOutcomeAsync(CarReservationActorOperations.InventoryReserving, isSuccess);
                break;

            //billing doesn't have a callback capabilities. It is a fire and forget operation. Result is polled.
            //so this will never be called.
            case CarReservationActivity.Billing:
                await ReportCompleteOperationOutcomeAsync(CarReservationActorOperations.Billing, isSuccess);
                break;

            //billing doesn't have a callback capabilities. It is a fire and forget operation. Result is polled.
            //so this will never be called.
            case CarReservationActivity.Refund:
                await ReportUndoOperationOutcomeAsync(CarReservationActorOperations.Billing, isSuccess);
                break;


            default:
                _logger.LogError("The activity is not supported.");
                return false;
        }
        return true;
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
            ResponseQueueName = "reservation-response-queue"
        };
        await _daprClient.InvokeBindingAsync("booking-queue", "create", carReservationRequest);
    }

    private async Task<bool> ValidateBookCarReservationAsync()
    {
        var reservationId = _reservationInfo!.ReservationId;

        try
        {
            var reservationState =
                await _daprClient.InvokeMethodAsync<Dto.BookingDto.ReservationState>(HttpMethod.Get, "booking-management",
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
            ResponseQueueName = "reservation-response-queue"
        };
        await _daprClient.InvokeBindingAsync("booking-queue", "create", carReservationCancellationRequest);
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
            ResponseQueueName = "reservation-response-queue"
        };

        await _daprClient.InvokeBindingAsync("inventory-queue", "create", carInventoryRequest);
    }
    
    private async Task<bool> ValidateReserveInventoryAsync()
    {
        _logger.LogInformation("Validating inventory reservation for reservation id: {reservationId}",
                       _reservationInfo!.ReservationId);

        var orderId = _reservationInfo!.ReservationId;

        try
        {
            var reservationState =
                await _daprClient.InvokeMethodAsync<Dto.InventoryDto.ReservationState>(HttpMethod.Get,
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
            ResponseQueueName = "reservation-response-queue"
        };

        await _daprClient.InvokeBindingAsync("inventory-queue", "create", carInventoryRequest);
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

        await _daprClient.InvokeBindingAsync("billing-queue", "create", billingRequest);
    }

    private async Task<bool> ValidateBillReservationAsync()
    {
        _logger.LogInformation("Validating billing for reservation id: {reservationId}", _reservationInfo!.ReservationId);

        var reservationId = _reservationInfo!.ReservationId;
        try
        {
            var billingState =
                await _daprClient.InvokeMethodAsync<BillingState>(HttpMethod.Get, "billing-management",
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

        await _daprClient.InvokeBindingAsync("billing-queue", "create", refundRequest);
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
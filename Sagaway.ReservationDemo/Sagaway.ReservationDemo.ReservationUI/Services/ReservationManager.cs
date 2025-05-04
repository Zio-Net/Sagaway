using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Collections.Concurrent;

namespace Sagaway.ReservationDemo.ReservationUI.Services;

// Type aliases for complex dictionary types
using UserReservationsMap = Dictionary<Guid, ReservationState>;
using UserReservationsConcurrentMap = ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, ReservationState>>;
using ReservationIdToCustomerMap = ConcurrentDictionary<Guid, Guid>;
using UserSubjectMap = ConcurrentDictionary<Guid, BehaviorSubject<Dictionary<Guid, ReservationState>>>;

using DTOs;
using System.Net;

/// <summary>
/// Client-side manager that handles reservation operations and state for the Blazor UI
/// </summary>
public class ReservationManager : IReservationManager, IAsyncDisposable
{
    private readonly IReservationApiClient _apiClient;
    private readonly ISignalRService _signalRService;
    private readonly ILogger<ReservationManager> _logger;

    // State storage
    private readonly UserReservationsConcurrentMap _userReservations = new();
    private readonly UserSubjectMap _userSubjects = new();
    private readonly ReservationIdToCustomerMap _reservationToCustomerId = new();

    // Simplified customer management - default predefined users
    private readonly Dictionary<Guid, string> _knownCustomers = new()
    {
        { Guid.Parse("12345678-1234-1234-1234-123456789abc"), "John Doe" },
        { Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890"), "Jane Smith" },
        { Guid.Parse("98765432-1098-7654-3210-987654321fed"), "Guest User" }
    };

    public event Action? StateChanged;

    public ReservationManager(
        IReservationApiClient apiClient,
        ISignalRService signalRService,
        ILogger<ReservationManager> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _signalRService = signalRService ?? throw new ArgumentNullException(nameof(signalRService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to SignalR events immediately
        _signalRService.OnSagaCompleted += HandleSagaCompleted;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _signalRService.InitializeAsync();
            _logger.LogInformation("SignalR initialized successfully");

            // Preload all known customers' data
            foreach (var customer in _knownCustomers)
            {
                // We don't await this to avoid blocking the initialization
                // These will be loaded in the background
                await LoadReservationsForUserAsync(customer.Key).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize SignalR");
            throw; // Rethrow so the UI knows initialization failed
        }
    }

    /// <summary>
    /// Gets all known customers with their IDs
    /// </summary>
    public Dictionary<Guid, string> GetAllUsers()
    {
        return _knownCustomers;
    }

    public IObservable<Dictionary<Guid, ReservationState>> GetReservationsForUser(Guid userId)
    {
        // Check if this is a known customer
        if (!_knownCustomers.ContainsKey(userId))
        {
            _logger.LogWarning("Attempted to get reservations for unknown customer {CustomerId}", userId);
            return Observable.Return(new Dictionary<Guid, ReservationState>());
        }

        var subject = _userSubjects.GetOrAdd(userId, _ =>
            new BehaviorSubject<UserReservationsMap>(GetUserReservationsSnapshot(userId)));

        return subject.AsObservable();
    }

    public async Task<Guid> CreateReservationAsync(Guid customerId, string customerName, string carClass)
    {
        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(carClass))
        {
            throw new ArgumentException("Customer name and car class are required");
        }

        // Verify this is a known customer
        if (!_knownCustomers.ContainsKey(customerId))
        {
            _logger.LogWarning("Attempted to create reservation for unknown customer {CustomerId}", customerId);
            throw new ArgumentException("Unknown customer ID", nameof(customerId));
        }

        _logger.LogInformation("Creating reservation for {CustomerName} ({CustomerId}), car class: {CarClass}",
            customerName, customerId, carClass);

        // Call API to create reservation
        var result = await _apiClient.ReserveCarAsync(customerName, carClass);

        if (result == null || result.ReservationId == Guid.Empty)
        {
            _logger.LogWarning("API call failed or returned invalid reservation ID");
            throw new InvalidOperationException("Reservation creation failed");
        }

        // Add to local state
        AddReservationToState(result.ReservationId, customerId, customerName, carClass);

        return result.ReservationId;
    }

    public async Task LoadReservationsForUserAsync(Guid customerId)
    {
        // Verify this is a known customer
        if (!_knownCustomers.TryGetValue(customerId, out var customerName))
        {
            _logger.LogWarning("Attempted to load reservations for unknown customer {CustomerId}", customerId);
            return;
        }

        _logger.LogInformation("Loading reservations for {CustomerName}", customerName);

        var reservations = await _apiClient.GetReservationsAsync(customerName);
        if (reservations == null)
        {
            _logger.LogWarning("API returned null for customer reservations");
            return;
        }

        UpdateReservationsFromApi(customerId, customerName, reservations);
        await LoadSagaLogsForUserReservations(customerId);
    }

    private void AddReservationToState(Guid reservationId, Guid customerId, string customerName, string carClass)
    {
        // Create reservation state with "Pending" status
        var reservation = new ReservationState
        {
            ReservationId = reservationId,
            CustomerId = customerId,
            CustomerName = customerName,
            CarClassCode = carClass,
            Status = ReservationStatusType.Pending,  // Initial status is always Reservation Pending
            IsProcessing = true, // New reservations are always processing
            CreatedAt = DateTime.UtcNow,
            SagaLog = string.Empty
        };

        // Add to customer's reservations dictionary
        var userReservations = _userReservations.GetOrAdd(customerId, _ =>
            new ConcurrentDictionary<Guid, ReservationState>());

        // Update existing or add new
        userReservations[reservationId] = reservation;

        // Add to lookup index
        _reservationToCustomerId[reservationId] = customerId;

        // Notify subscribers immediately
        NotifyStateChanged(customerId);

        _logger.LogInformation("Added pending reservation {ReservationId} to state for {CustomerName}, car class {CarClass}",
            reservationId, customerName, carClass);
    }


    private void UpdateReservationsFromApi(Guid customerId, string customerName, IEnumerable<ReservationStatus> apiReservations)
    {
        var userReservations = _userReservations.GetOrAdd(customerId, _ =>
            new ConcurrentDictionary<Guid, ReservationState>());

        // Track reservation IDs received from the API
        var reservationStatusEnumerable = apiReservations as ReservationStatus[] ?? apiReservations.ToArray();
        var apiReservationIds = new HashSet<Guid>(reservationStatusEnumerable.Select(r => r.ReservationId));

        // Log the received reservation counts
        int totalReceived = reservationStatusEnumerable.Length;
        int reservedCount = reservationStatusEnumerable.Count(r => r.IsReserved);
        _logger.LogInformation("Received {TotalCount} reservations for {CustomerName} from API. {ReservedCount} are marked as reserved.",
            totalReceived, customerName, reservedCount);

        // Remove reservations that are no longer in the API response
        foreach (var reservationId in userReservations.Keys.ToList().Where(reservationId => !apiReservationIds.Contains(reservationId)))
        {
            userReservations.TryRemove(reservationId, out _);
            _reservationToCustomerId.TryRemove(reservationId, out _);

            _logger.LogInformation("Removed reservation {ReservationId} for customer {CustomerName} as it no longer exists in the system.",
                reservationId, customerName);
        }

        // Update existing reservations or add new ones
        foreach (var apiRes in reservationStatusEnumerable)
        {
            if (apiRes.ReservationId == Guid.Empty)
                continue;

            UpdateCustomerReservation(customerId, customerName, apiRes);
        }

        NotifyStateChanged(customerId);
    }

    private void UpdateCustomerReservation(Guid customerId, string customerName, ReservationStatus newReservationStatus)
    {
        var previousReservationStatus = ReservationStatusType.Pending;
        ReservationState? existingReservation = null;

        //try to get the user reservation by the customerId and the reservation id from the newReservationStatus 
        if (!_userReservations.TryGetValue(customerId, out var userReservations) ||
            !userReservations.TryGetValue(newReservationStatus.ReservationId, out existingReservation))
        {
            // If not found, create a new dictionary for the user
            userReservations = _userReservations.GetOrAdd(customerId, _ =>
                new ConcurrentDictionary<Guid, ReservationState>());
        }
        else
        {
            previousReservationStatus = existingReservation.Status;
        }

        // For initial loading, we need to handle transient states properly
        bool isInitialLoad = previousReservationStatus == ReservationStatusType.Pending && existingReservation == null;

        ReservationStatusType status = (newReservationStatus.IsReserved, previousReservationStatus, isInitialLoad) switch
        {
            // If it's reserved and was pending, it's now confirmed/reserved
            (true, ReservationStatusType.Pending, _) => ReservationStatusType.Reserved,

            // If it's reserved and was in CancelPending, cancellation failed (still reserved)
            (true, ReservationStatusType.CancelPending, _) => ReservationStatusType.CancelFailed,

            // If it's not reserved and was pending, and this is initial load, default to Cancelled
            // This assumes non-reserved cars in API were successfully cancelled rather than failed
            (false, ReservationStatusType.Pending, true) => ReservationStatusType.Cancelled,

            // If it's not reserved and was pending, but we're updating an existing record, it's a failure
            (false, ReservationStatusType.Pending, false) => ReservationStatusType.Failed,

            // If it's not reserved and was in CancelPending, cancellation succeeded
            (false, ReservationStatusType.CancelPending, _) => ReservationStatusType.Cancelled,

            // If it was Reserved and is now not reserved, it was cancelled
            (false, ReservationStatusType.Reserved, _) => ReservationStatusType.Cancelled,

            // Default: maintain current status
            _ => previousReservationStatus
        };

        if (existingReservation != null)
        {
            // Update existing reservation while preserving saga log if it exists
            var updatedReservation = existingReservation with
            {
                Status = status,
                IsProcessing = false,
                // Keep the existing saga log
            };
            userReservations[newReservationStatus.ReservationId] = updatedReservation;
            return;
        }

        // Create new reservation
        userReservations[newReservationStatus.ReservationId] = new ReservationState
        {
            ReservationId = newReservationStatus.ReservationId,
            CustomerId = customerId,
            CustomerName = customerName,
            CarClassCode = newReservationStatus.CarClass,
            Status = status,
            IsProcessing = false,
            CreatedAt = DateTime.UtcNow,
            SagaLog = string.Empty
        };

        // Add to lookup index
        _reservationToCustomerId[newReservationStatus.ReservationId] = customerId;
    }

    private async Task LoadSagaLogsForUserReservations(Guid customerId)
    {
        if (!_userReservations.TryGetValue(customerId, out var userReservations))
        {
            return;
        }

        // Process saga logs in parallel for efficiency
        var loadTasks = new List<Task>();

        foreach (var reservationId in userReservations.Keys)
        {
            loadTasks.Add(LoadSagaLogForReservationAsync(reservationId));
        }

        await Task.WhenAll(loadTasks);
    }

    private async Task LoadSagaLogForReservationAsync(Guid reservationId)
    {
        if (!_reservationToCustomerId.TryGetValue(reservationId, out var customerId))
            return;

        try
        {
            var sagaLog = await _apiClient.GetSagaLogAsync(reservationId);

            // If no saga log was returned, don't update
            if (string.IsNullOrEmpty(sagaLog))
                return;

            // Update the reservation if it exists
            if (_userReservations.TryGetValue(customerId, out var userReservations) &&
                userReservations.TryGetValue(reservationId, out var reservation))
            {
                // Only update the saga log, preserve other properties
                var updatedReservation = reservation with { SagaLog = sagaLog };
                userReservations[reservationId] = updatedReservation;

                _logger.LogInformation("Updated saga log for reservation {ReservationId}", reservationId);

                // Notify subscribers of the saga log update
                NotifyStateChanged(customerId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load saga log for reservation {ReservationId}", reservationId);
        }
    }

    private UserReservationsMap GetUserReservationsSnapshot(Guid userId)
    {
        if (!_userReservations.TryGetValue(userId, out var reservations))
            return new UserReservationsMap();

        // Create a safe copy of all the reservation state objects
        var snapshot = new UserReservationsMap();
        foreach (var (reservationId, reservation) in reservations)
        {
            snapshot[reservationId] = reservation;  // Records are immutable so no need for deep copy
        }

        return snapshot;
    }

    private void NotifyStateChanged(Guid customerId)
    {
        if (_userSubjects.TryGetValue(customerId, out var subject))
        {
            subject.OnNext(GetUserReservationsSnapshot(customerId));
        }

        StateChanged?.Invoke();
    }

    private void HandleSagaCompleted(SagaUpdate update)
    {
        _logger.LogInformation("Received saga update for reservation {ReservationId}, outcome: {Outcome}",
            update.ReservationId, update.Outcome);

        // First try to find customer ID using O(1) lookup
        if (_reservationToCustomerId.TryGetValue(update.ReservationId, out var customerId))
        {
            // Found customer by reservation ID
            UpdateReservationFromSaga(customerId, update);
            return;
        }

        // Try to find by customer name among known customers
        customerId = FindCustomerIdByName(update.CustomerName);
        if (customerId != Guid.Empty)
        {
            // Found customer, store for future lookups
            _reservationToCustomerId[update.ReservationId] = customerId;
            UpdateReservationFromSaga(customerId, update);
            return;
        }

        _logger.LogInformation("Ignoring update for unknown reservation {ReservationId} with customer {CustomerName}",
            update.ReservationId, update.CustomerName);
    }

    private Guid FindCustomerIdByName(string customerName)
    {
        // Find the customer ID by name in our known customers dictionary
        foreach (var kvp in _knownCustomers)
        {
            if (kvp.Value.Equals(customerName, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Key;
            }
        }

        return Guid.Empty;
    }

    private void UpdateReservationFromSaga(Guid customerId, SagaUpdate update)
    {
        // Get user reservations dictionary
        var userReservations = _userReservations.GetOrAdd(customerId, _ =>
            new ConcurrentDictionary<Guid, ReservationState>());

        // Determine the status based on the saga outcome
        ReservationStatusType status;
        bool isProcessing = false;

        if (update.Outcome.StartsWith("Reservation", StringComparison.OrdinalIgnoreCase))
        {
            // For reservation creation saga
            status = update.Outcome.Equals("Reservation Succeeded", StringComparison.OrdinalIgnoreCase)
                ? ReservationStatusType.Reserved : ReservationStatusType.Failed;
        }
        else if (update.Outcome.StartsWith("Cancellation", StringComparison.OrdinalIgnoreCase))
        {
            // For cancellation saga
            status = update.Outcome.Equals("Cancellation Succeeded", StringComparison.OrdinalIgnoreCase)
                ? ReservationStatusType.Cancelled
                : ReservationStatusType.CancelFailed;
        }
        else
        {
            // Generic outcome handling
            status = update.Outcome.Contains("Success", StringComparison.OrdinalIgnoreCase)
                ? ReservationStatusType.Reserved
                : ReservationStatusType.Failed;
        }

        // Check if we already have this reservation
        ReservationState updatedReservation;
        if (userReservations.TryGetValue(update.ReservationId, out var existingReservation))
        {
            // Update existing reservation with new data
            updatedReservation = existingReservation with
            {
                Status = status,
                IsProcessing = isProcessing,
                SagaLog = update.Log
            };
        }
        else
        {
            // Find the customer name
            _knownCustomers.TryGetValue(customerId, out var customerName);

            // Create new reservation with all data in one go
            updatedReservation = new ReservationState
            {
                ReservationId = update.ReservationId,
                CustomerId = customerId,
                CustomerName = customerName ?? update.CustomerName, // Prefer our known name, fall back to update
                CarClassCode = update.CarClass,
                CreatedAt = DateTime.UtcNow,
                Status = status,
                IsProcessing = isProcessing,
                SagaLog = update.Log
            };
        }

        // Store the reservation
        userReservations[update.ReservationId] = updatedReservation;

        _logger.LogInformation("Updated reservation {ReservationId} status to {Status} based on outcome: {Outcome}",
            update.ReservationId, status, update.Outcome);

        // Notify subscribers
        NotifyStateChanged(customerId);
    }

    public async Task<bool> CancelReservationAsync(Guid reservationId)
    {
        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException("Reservation ID cannot be empty", nameof(reservationId));
        }

        // Find the customer ID for this reservation
        if (!_reservationToCustomerId.TryGetValue(reservationId, out var customerId))
        {
            _logger.LogWarning("Cannot cancel reservation {ReservationId} - not found in local state", reservationId);
            return false;
        }

        // Get current reservation state
        if (!_userReservations.TryGetValue(customerId, out var userReservations) ||
            !userReservations.TryGetValue(reservationId, out var reservation))
        {
            _logger.LogWarning("Cannot cancel reservation {ReservationId} - not found in user reservations", reservationId);
            return false;
        }

        // If already cancelled, treat as success
        if (reservation.Status == ReservationStatusType.Cancelled)
        {
            _logger.LogInformation("Reservation {ReservationId} is already cancelled", reservationId);
            return true; // Return success for already cancelled reservations
        }

        // Reject cancellation if not in a cancellable state
        if (reservation.Status is not (ReservationStatusType.Reserved or ReservationStatusType.CancelFailed) ||
            reservation.IsProcessing)
        {
            _logger.LogWarning("Cannot cancel reservation {ReservationId} - status is {Status}, IsProcessing: {IsProcessing}",
                reservationId, reservation.Status, reservation.IsProcessing);
            return false;
        }

        _logger.LogInformation("Initiating cancellation for reservation {ReservationId} for customer {CustomerName}",
            reservationId, reservation.CustomerName);

        try
        {
            // Call the updated API method that returns a tuple
            var (cancellationAccepted, statusCode) = await _apiClient.CancelReservationAsync(reservationId);

            if (cancellationAccepted)
            {
                // Update local state to show reservation is being processed (cancellation pending)
                var updatedReservation = reservation with
                {
                    IsProcessing = true,
                    Status = ReservationStatusType.CancelPending
                };

                // Update in the dictionary
                userReservations[reservationId] = updatedReservation;

                // Notify subscribers that the state has changed
                NotifyStateChanged(customerId);

                _logger.LogInformation("Cancellation request accepted for reservation {ReservationId}", reservationId);
                return true;
            }

            // Handle "already cancelled" case (404 Not Found)
            if (statusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Reservation {ReservationId} not found on server - likely already cancelled", reservationId);

                // Update local state to show as cancelled
                var updatedReservation = reservation with
                {
                    IsProcessing = false,
                    Status = ReservationStatusType.Cancelled
                };

                // Update in the dictionary
                userReservations[reservationId] = updatedReservation;

                // Notify subscribers that the state has changed
                NotifyStateChanged(customerId);

                return true; // Consider this a successful cancellation
            }

            _logger.LogWarning("Cancellation request was rejected by API for reservation {ReservationId} with status code {StatusCode}",
                reservationId, statusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while trying to cancel reservation {ReservationId}", reservationId);
            return false;
        }
    }

    public async Task<List<CarClassInfo>> GetCarInventoryAsync()
    {
        try
        {
            var inventoryResponse = await _apiClient.GetCarInventoryAsync();

            return inventoryResponse.CarClasses;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while retrieving car inventory.");
            throw;
        }
    }

    public async Task<CarClassInfo> UpdateCarClassAllocationAsync(string carClass, int maxAllocation)
    {
        if (string.IsNullOrWhiteSpace(carClass))
        {
            throw new ArgumentException("Car class code is required.", nameof(carClass));
        }

        if (maxAllocation < 0)
        {
            throw new ArgumentException("Maximum allocation must be non-negative.", nameof(maxAllocation));
        }

        try
        {
            var allocationRequest = new CarClassAllocationRequest
            {
                CarClass = carClass,
                MaxAllocation = maxAllocation
            };

            var updatedCarClassInfo = await _apiClient.UpdateCarClassAllocationAsync(allocationRequest);
            if (updatedCarClassInfo == null)
            {
                _logger.LogWarning("Failed to update car class allocation for {CarClass}.", carClass);
                throw new InvalidOperationException("Failed to update car class allocation.");
            }

            return updatedCarClassInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while updating car class allocation for {CarClass}.", carClass);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Unsubscribe from SignalR
        _signalRService.OnSagaCompleted -= HandleSagaCompleted;

        // Clean up subjects
        foreach (var subject in _userSubjects.Values)
        {
            subject.OnCompleted();
            subject.Dispose();
        }

        // Clear collections
        _userSubjects.Clear();
        _userReservations.Clear();
        _reservationToCustomerId.Clear();

        await ValueTask.CompletedTask;
    }
}

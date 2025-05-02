using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Collections.Concurrent;

namespace Sagaway.ReservationDemo.ReservationUI.Services;

// Type aliases for complex dictionary types
using UserReservationsMap = Dictionary<Guid, ReservationState>;
using UserReservationsConcurrentMap = ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, ReservationState>>;
using ReservationIdToCustomerMap = ConcurrentDictionary<Guid, Guid>;
using CustomerIdToNameMap = ConcurrentDictionary<Guid, string>;
using CustomerNameToIdMap = ConcurrentDictionary<string, Guid>;
using UserSubjectMap = ConcurrentDictionary<Guid, BehaviorSubject<Dictionary<Guid, ReservationState>>>;


using DTOs;
using System.Collections.Concurrent;


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
    private readonly CustomerIdToNameMap _customerIdToName = new();
    private readonly CustomerNameToIdMap _customerNameToId = new();

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize SignalR");
            throw; // Rethrow so the UI knows initialization failed
        }
    }
    
    public IObservable<Dictionary<Guid, ReservationState>> GetReservationsForUser(Guid userId)
    {
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

        // Store customer name for future reference
        _customerIdToName[customerId] = customerName;
        _customerNameToId[customerName] = customerId;

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
        if (!_customerIdToName.TryGetValue(customerId, out var customerName))
        {
            _logger.LogWarning("Unable to load reservations - customer name not found for ID {CustomerId}", customerId);
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
            Status = "Pending",  // Initial status is always Pending
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

            // Determine status based on the IsReserved property
            string status = apiRes.IsReserved ? "Confirmed" : "Failed";
            bool isProcessing = false;

            // Check if we already have this reservation
            if (userReservations.TryGetValue(apiRes.ReservationId, out var existingReservation))
            {
                // Update existing reservation while preserving saga log if it exists
                var updatedReservation = existingReservation with
                {
                    Status = status,
                    IsProcessing = isProcessing,
                    // Keep the existing saga log
                };
                userReservations[apiRes.ReservationId] = updatedReservation;
            }
            else
            {
                // Create new reservation
                userReservations[apiRes.ReservationId] = new ReservationState
                {
                    ReservationId = apiRes.ReservationId,
                    CustomerId = customerId,
                    CustomerName = customerName,
                    CarClassCode = apiRes.CarClass ?? string.Empty,
                    Status = status,
                    IsProcessing = isProcessing,
                    CreatedAt = DateTime.UtcNow,
                    SagaLog = string.Empty
                };

                // Add to lookup index
                _reservationToCustomerId[apiRes.ReservationId] = customerId;
            }
        }

        NotifyStateChanged(customerId);
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

        // Otherwise try to find by customer name
        customerId = FindCustomerIdByName(update.CustomerName);
        if (customerId != Guid.Empty)
        {
            // Found customer by name, store for future lookups
            _reservationToCustomerId[update.ReservationId] = customerId;
            UpdateReservationFromSaga(customerId, update);
            return;
        }

        // If still not found, this might be a totally new customer or reservation
        _logger.LogInformation("Ignoring update for unknown reservation {ReservationId} with customer {CustomerName}",
            update.ReservationId, update.CustomerName);
    }


    private Guid FindCustomerIdByName(string customerName) =>
        _customerNameToId.TryGetValue(customerName, out var customerId)
            ? customerId
            : Guid.Empty;

    private void UpdateReservationFromSaga(Guid customerId, SagaUpdate update)
    {
        // Get user reservations dictionary
        var userReservations = _userReservations.GetOrAdd(customerId, _ =>
            new ConcurrentDictionary<Guid, ReservationState>());

        // Determine the status based on the saga outcome
        string status;
        bool isProcessing = false;

        if (update.Outcome.StartsWith("Reservation", StringComparison.OrdinalIgnoreCase))
        {
            // For reservation creation saga
            status = update.Outcome.Equals("Reservation Succeeded", StringComparison.OrdinalIgnoreCase)
                ? "Confirmed" : "Failed";
        }
        else if (update.Outcome.StartsWith("Cancellation", StringComparison.OrdinalIgnoreCase))
        {
            // For cancellation saga
            status = update.Outcome.Equals("Cancellation Succeeded", StringComparison.OrdinalIgnoreCase)
                ? "Cancelled" : "Confirmed"; // If cancellation fails, reservation remains confirmed
        }
        else
        {
            // Generic outcome handling
            status = update.Outcome.Contains("Success", StringComparison.OrdinalIgnoreCase)
                ? "Confirmed" : "Failed";
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
            // Create new reservation with all data in one go
            updatedReservation = new ReservationState
            {
                ReservationId = update.ReservationId,
                CustomerId = customerId,
                CustomerName = update.CustomerName,
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

    /// <summary>
    /// Initiates the cancellation process for a specific reservation.
    /// </summary>
    /// <param name="reservationId">The ID of the reservation to cancel.</param>
    /// <returns>True if the cancellation request was accepted, false otherwise.</returns>
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

        // Only allow cancelling confirmed reservations that are not already being processed
        if (reservation.Status != "Confirmed" || reservation.IsProcessing)
        {
            _logger.LogWarning("Cannot cancel reservation {ReservationId} - status is {Status}, IsProcessing: {IsProcessing}",
                reservationId, reservation.Status, reservation.IsProcessing);
            return false;
        }

        _logger.LogInformation("Initiating cancellation for reservation {ReservationId} for customer {CustomerName}",
            reservationId, reservation.CustomerName);

        try
        {
            // Call the API to initiate cancellation
            bool cancellationAccepted = await _apiClient.CancelReservationAsync(reservationId);

            if (cancellationAccepted)
            {
                // Update local state to show reservation is being processed (cancellation pending)
                // Note: Status remains "Confirmed" until the saga completes
                var updatedReservation = reservation with
                {
                    IsProcessing = true,
                    Status = "Confirmed" // Status doesn't change yet
                };

                // Update in the dictionary
                userReservations[reservationId] = updatedReservation;

                // Notify subscribers that the state has changed
                NotifyStateChanged(customerId);

                _logger.LogInformation("Cancellation request accepted for reservation {ReservationId}", reservationId);
                return true;
            }

            _logger.LogWarning("Cancellation request was rejected by API for reservation {ReservationId}", reservationId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while trying to cancel reservation {ReservationId}", reservationId);
            return false;
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
        _customerIdToName.Clear();

        await ValueTask.CompletedTask;
    }
}
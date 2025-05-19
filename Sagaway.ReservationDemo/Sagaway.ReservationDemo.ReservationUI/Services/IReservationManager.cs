namespace Sagaway.ReservationDemo.ReservationUI.Services;

// Type aliases for complex dictionary types
using ReservationStateObservable = IObservable<Dictionary<Guid,
    ReservationState>>;

public interface IReservationManager
{
    /// <summary>
    /// Initializes the reservation manager and prepares it for use.
    /// </summary>
    /// <returns>A task that represents the asynchronous initialization operation.</returns>
    Task InitializeAsync();

    /// <summary>
    /// Gets all known customers with their IDs
    /// </summary>
    Dictionary<Guid, string> GetAllUsers();

    /// <summary>
    /// Gets an observable stream of reservation states for a specific user.
    /// </summary>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <returns>An observable that provides updates to the reservation states for the user.</returns>
    ReservationStateObservable GetReservationsForUser(Guid userId);

    /// <summary>
    /// Creates a new reservation for a customer.
    /// </summary>
    /// <param name="customerId">The unique identifier of the customer.</param>
    /// <param name="customerName">The name of the customer.</param>
    /// <param name="carClass">The class of the car being reserved.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the unique identifier of the created reservation.</returns>
    Task<Guid> CreateReservationAsync(Guid customerId, string customerName, string carClass);

    /// <summary>
    /// Loads all reservations for a specific user.
    /// </summary>
    /// <param name="customerId">The unique identifier of the customer.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task LoadReservationsForUserAsync(Guid customerId);

    /// <summary>
    /// Initiates the cancellation process for a specific reservation.
    /// </summary>
    /// <param name="reservationId">The ID of the reservation to cancel.</param>
    /// <returns>True if the cancellation request was accepted, false otherwise.</returns>
    Task<bool> CancelReservationAsync(Guid reservationId);

	/// <summary>
	/// Initiates the cleanup process for the db.
	/// </summary>
	/// <returns>True if the request was accepted, false otherwise.</returns>
	Task<bool> CleanTheDatabaseAsync();

	/// <summary>
	/// Event triggered when the state of reservations changes.
	/// </summary>
	event Action StateChanged;

    ValueTask DisposeAsync();

}

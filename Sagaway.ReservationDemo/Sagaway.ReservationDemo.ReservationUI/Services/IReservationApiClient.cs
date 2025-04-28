namespace Sagaway.ReservationDemo.ReservationUI.Services;
public interface IReservationApiClient
{
    /// <summary>
    /// Initiates a car reservation request.
    /// </summary>
    /// <param name="customerName">The name of the customer.</param>
    /// <param name="carClass">The desired car class code (e.g., "ECON", "STD", "LUX").</param>
    /// <param name="reservationId">An optional existing reservation ID to use.</param>
    /// <returns>A ReservationResult containing details of the initiated reservation, or null if failed before getting a result.</returns>
    Task<ReservationResult?> ReserveCarAsync(string customerName, string carClass, Guid? reservationId = null);

    /// <summary>
    /// Retrieves the current status of reservations for a specific customer.
    /// </summary>
    /// <param name="customerName">The name of the customer whose reservations to fetch.</param>
    /// <returns>A list of ReservationStatus objects (mapped from BookingInfo), or null/empty list if none found or an error occurred.</returns>
    Task<List<ReservationStatus>?> GetReservationsAsync(string customerName);

    /// <summary>
    /// Retrieves the details of a specific reservation.
    /// </summary>
    /// <param name="reservationId">The ID of the reservation to fetch.</param>
    /// <returns>A ReservationStatus object (mapped from BookingInfo), or null if not found or an error occurred.</returns>
    Task<ReservationStatus?> GetReservationAsync(Guid reservationId);

    /// <summary>
    /// Initiates the cancellation process for a specific reservation.
    /// </summary>
    /// <param name="reservationId">The ID of the reservation to cancel.</param>
    /// <returns>True if the cancellation request was accepted, false otherwise.</returns>
    Task<bool> CancelReservationAsync(Guid reservationId);
}

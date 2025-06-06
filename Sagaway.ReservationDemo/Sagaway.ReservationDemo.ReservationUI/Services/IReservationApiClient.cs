using System.Net;
using Sagaway.ReservationDemo.ReservationUI.Services.DTOs;

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
    /// <returns>A tuple containing: success status and HTTP status code if applicable</returns>
    Task<(bool Success, HttpStatusCode StatusCode)> CancelReservationAsync(Guid reservationId);

    /// <summary>
    /// Retrieves the saga log for a specific reservation.
    /// </summary>
    /// <param name="reservationId">The ID of the reservation whose saga log to fetch.</param>
    /// <returns>The saga log as a string, or null if not found or an error occurred.</returns>
    Task<string?> GetSagaLogAsync(Guid reservationId);

    /// <summary>
    /// Updates the car class allocation with the provided request details.
    /// </summary>
    /// <param name="allocationRequest">The request containing car class and allocation details.</param>
    /// <returns>A CarClassInfo object containing updated allocation details.</returns>
    Task<CarClassInfo> UpdateCarClassAllocationAsync(CarClassAllocationRequest allocationRequest);

    /// <summary>
    /// Retrieves the current car inventory details.
    /// </summary>
    /// <returns>A CarInventoryResponse object containing the list of car classes and their allocation details.</returns>
    Task<CarInventoryResponse> GetCarInventoryAsync();
}

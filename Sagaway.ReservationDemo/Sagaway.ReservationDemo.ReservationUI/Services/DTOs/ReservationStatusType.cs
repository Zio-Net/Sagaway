using System.Text.Json.Serialization;

namespace Sagaway.ReservationDemo.ReservationUI.Services.DTOs;

/// <summary>
/// Enum representing the possible states of a reservation
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReservationStatusType
{
    Pending,           // Initial transient state when reservation request is sent
    Reserved,          // Final state, Successfully reserved
    NotReserved,       // Final state, after cancelled or failed
    Failed,            // Transient state, Reservation attempt failed
    CancelPending,     // Transient state, Cancellation has been requested but not completed
    Cancelled,         // Transient state, Successfully cancelled
    CancelFailed       // Transient state, it will be Reserved
}
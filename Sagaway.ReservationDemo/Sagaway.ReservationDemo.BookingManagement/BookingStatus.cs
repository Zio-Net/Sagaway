using System.Text.Json.Serialization;

namespace Sagaway.ReservationDemo.BookingManagement;

/// <summary>
/// Enum representing the possible states of a reservation
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BookingStatus
{
    Unknown,          // Unknown status
    Reserved,          // Successfully reserved
    Cancelled          // Successfully cancelled
}
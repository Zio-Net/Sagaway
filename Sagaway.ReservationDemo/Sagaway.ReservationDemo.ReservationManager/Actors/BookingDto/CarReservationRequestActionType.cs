using System.Text.Json.Serialization;

namespace Sagaway.ReservationDemo.ReservationManager.Actors.BookingDto;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CarReservationRequestActionType
{
    Reserve,
    Cancel
}
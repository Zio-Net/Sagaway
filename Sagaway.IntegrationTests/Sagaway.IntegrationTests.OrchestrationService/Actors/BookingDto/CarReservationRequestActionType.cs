using System.Text.Json.Serialization;

namespace SagaReservationDemo.ReservationManager.Actors.BookingDto;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CarReservationRequestActionType
{
    Reserve,
    Cancel
}
using System.Text.Json.Serialization;

namespace SagaReservationDemo.ReservationManager.Dto.BookingDto;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CarReservationRequestActionType
{
    Reserve,
    Cancel
}
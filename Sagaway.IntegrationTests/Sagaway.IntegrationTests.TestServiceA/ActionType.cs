using System.Text.Json.Serialization;

namespace SagaReservationDemo.BookingManagement;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionType
{
    Reserve,
    Cancel
}
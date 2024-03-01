using System.Text.Json.Serialization;

namespace Sagaway.ReservationDemo.BookingManagement;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionType
{
    Reserve,
    Cancel
}
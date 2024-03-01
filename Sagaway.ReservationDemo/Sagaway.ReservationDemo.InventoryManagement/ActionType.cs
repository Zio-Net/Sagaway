using System.Text.Json.Serialization;

namespace Sagaway.ReservationDemo.InventoryManagement;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionType
{
    Reserve,
    Cancel
}
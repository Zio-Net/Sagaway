using System.Text.Json.Serialization;

namespace SagaReservationDemo.InventoryManagement;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionType
{
    Reserve,
    Cancel
}
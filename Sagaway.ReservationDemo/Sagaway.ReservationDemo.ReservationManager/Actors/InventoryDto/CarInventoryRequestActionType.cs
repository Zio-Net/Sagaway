using System.Text.Json.Serialization;

namespace Sagaway.ReservationDemo.ReservationManager.Actors.InventoryDto;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CarInventoryRequestActionType
{
    Reserve,
    Cancel
}
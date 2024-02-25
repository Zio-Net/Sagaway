using System.Text.Json.Serialization;

namespace SagaReservationDemo.ReservationManager.Actors.InventoryDto;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CarInventoryRequestActionType
{
    Reserve,
    Cancel
}
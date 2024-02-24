using System.Text.Json.Serialization;

namespace SagaReservationDemo.ReservationManager.Dto.InventoryDto;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CarInventoryRequestActionType
{
    Reserve,
    Cancel
}
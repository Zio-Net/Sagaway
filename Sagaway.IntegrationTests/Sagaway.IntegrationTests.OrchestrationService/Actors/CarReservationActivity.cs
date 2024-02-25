using System.Text.Json.Serialization;

namespace SagaReservationDemo.ReservationManager.Actors;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CarReservationActivity
{
    CarBooking,
    CancellingCarBooking,
    InventoryReserving,
    InventoryCancelling,
    Billing,
    Refund,
}
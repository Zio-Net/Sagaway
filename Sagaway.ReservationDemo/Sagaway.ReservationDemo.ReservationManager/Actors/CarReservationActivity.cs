using System.Text.Json.Serialization;

namespace Sagaway.ReservationDemo.ReservationManager.Actors;

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
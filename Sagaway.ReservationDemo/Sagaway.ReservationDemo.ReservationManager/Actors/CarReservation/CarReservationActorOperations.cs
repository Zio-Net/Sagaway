namespace Sagaway.ReservationDemo.ReservationManager.Actors.CarReservation;

[Flags]
public enum CarReservationActorOperations
{
    CarBooking = 1,
    InventoryReserving = 2,
    Billing = 4
}
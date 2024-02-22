namespace SagaReservationDemo.ReservationManager.Actors;

[Flags]
public enum CarReservationActorOperations
{
    CarBooking = 1,
    InventoryReserving = 2,
    Billing = 4
}
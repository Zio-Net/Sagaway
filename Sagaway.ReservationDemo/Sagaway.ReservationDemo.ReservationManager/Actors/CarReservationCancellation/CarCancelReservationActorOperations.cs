namespace Sagaway.ReservationDemo.ReservationManager.Actors.CarReservationCancellation;

[Flags]
public enum CarCancelReservationActorOperations
{
    CancelBooking = 1,
    CancelInventoryReserving = 2,
    Refund = 4
}
namespace SagaReservationDemo.ReservationManager.Actors.BillingDto;

// ReSharper disable once ClassNeverInstantiated.Global
public record BillingState
{
    public string Status { get; set; } = string.Empty;
}
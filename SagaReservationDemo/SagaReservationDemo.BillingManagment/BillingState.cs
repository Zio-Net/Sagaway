namespace SagaReservationDemo.BillingManagement;

public record BillingState
{
    public string Status { get; set; } = "Not Charged";
}
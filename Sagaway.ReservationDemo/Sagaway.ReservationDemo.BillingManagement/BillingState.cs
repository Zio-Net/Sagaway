namespace Sagaway.ReservationDemo.BillingManagement;

public record BillingState
{
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public string Status { get; set; } = "Not Charged";
}
namespace Sagaway.ReservationDemo.ReservationManager.Actors;

// ReSharper disable once ClassNeverInstantiated.Global
public record ReservationInfo
{
    public string CarClass { get; set; } = string.Empty;
    public Guid ReservationId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
}
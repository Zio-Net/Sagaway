namespace Sagaway.ReservationDemo.ReservationManager.Actors;

// ReSharper disable once ClassNeverInstantiated.Global
public record ReservationInfo
{
    // ReSharper disable PropertyCanBeMadeInitOnly.Global
    public required string CarClass { get; set; }
    public Guid ReservationId { get; set; }
    public required string CustomerName { get; set; }
}
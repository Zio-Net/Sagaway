namespace Sagaway.ReservationDemo.ReservationManager.Actors;

// ReSharper disable once ClassNeverInstantiated.Global
public record BookingInfo
{
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public required string CustomerName { get; set; }
    public bool IsReserved { get; set; }
    public Guid Id { get; set; }
}
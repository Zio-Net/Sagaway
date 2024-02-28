namespace Sagaway.ReservationDemo.ReservationManager.Actors;

// ReSharper disable once ClassNeverInstantiated.Global
public record BookingInfo
{
    public string CustomerName { get; set; } = string.Empty;
    public bool IsReserved { get; set; }
    public Guid Id { get; set; }
}
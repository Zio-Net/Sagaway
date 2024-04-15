namespace Sagaway.ReservationDemo.ReservationManager.Actors;

// ReSharper disable once ClassNeverInstantiated.Global
public record InventoryInfo
{
    public Guid Id { get; set; }
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public required string CarClass { get; set; }
    public bool IsReserved { get; set; }
}
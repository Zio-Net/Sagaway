namespace Sagaway.ReservationDemo.ReservationManager.Actors;

public record InventoryInfo
{
    public Guid Id { get; set; }
    public string CarClass { get; set; } = string.Empty;
    public bool IsReserved { get; set; }
}
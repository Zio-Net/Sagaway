namespace Sagaway.ReservationDemo.ReservationManager.Actors.InventoryDto;

public record CarClassInfo
{
    public required string Code { get; set; }
    public int Reserved { get; set; }
    public int MaxAllocation { get; set; }
}
namespace Sagaway.ReservationDemo.ReservationManager.Actors.InventoryDto;

public record CarClassAllocationRequest
{
    public required string CarClass { get; set; }
    public int MaxAllocation { get; set; }
}
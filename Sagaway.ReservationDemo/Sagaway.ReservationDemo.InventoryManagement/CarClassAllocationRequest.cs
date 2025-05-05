namespace Sagaway.ReservationDemo.InventoryManagement;

public record CarClassAllocationRequest
{
    public required string CarClass { get; set; }
    public int MaxAllocation { get; set; }
}
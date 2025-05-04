namespace Sagaway.ReservationDemo.InventoryManagement;

public record CarClassInfo
{
    public required string Code { get; set; }
    public int Reserved { get; set; }
    public int MaxAllocation { get; set; }
}
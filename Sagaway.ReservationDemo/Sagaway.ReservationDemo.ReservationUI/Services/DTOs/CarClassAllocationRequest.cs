namespace Sagaway.ReservationDemo.ReservationUI.Services.DTOs;

public record CarClassAllocationRequest
{
    public required string CarClass { get; set; }
    public int MaxAllocation { get; set; }
}
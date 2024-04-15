namespace Sagaway.ReservationDemo.ReservationManager.Actors.InventoryDto;

// ReSharper disable once ClassNeverInstantiated.Global
public class ReservationState
{
    // ReSharper disable UnusedMember.Global
    public Guid Id { get; set; }
    public required string CarClass { get; set; }
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public bool IsReserved { get; set; }
}
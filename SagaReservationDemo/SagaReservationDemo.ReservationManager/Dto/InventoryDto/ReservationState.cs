namespace SagaReservationDemo.ReservationManager.Dto.InventoryDto;

// ReSharper disable once ClassNeverInstantiated.Global
public class ReservationState
{
    public Guid Id { get; set; }
    public string CarClass { get; set; } = string.Empty;
    public bool IsReserved { get; set; }
}
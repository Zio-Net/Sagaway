namespace Sagaway.ReservationDemo.ReservationManager.Actors.InventoryDto;

public record CarInventoryResponse
{
    public List<CarClassInfo> CarClasses { get; set; } = [];
}
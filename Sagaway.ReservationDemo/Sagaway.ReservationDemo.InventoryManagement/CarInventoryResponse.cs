namespace Sagaway.ReservationDemo.InventoryManagement;

public record CarInventoryResponse
{
    public List<CarClassInfo> CarClasses { get; set; } = [];
}
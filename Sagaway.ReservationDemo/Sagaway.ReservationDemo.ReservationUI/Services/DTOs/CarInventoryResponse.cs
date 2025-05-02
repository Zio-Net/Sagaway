namespace Sagaway.ReservationDemo.ReservationUI.Services.DTOs;

public record CarInventoryResponse
{
    public List<CarClassInfo> CarClasses { get; set; } = [];
}
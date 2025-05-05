using Sagaway.ReservationDemo.ReservationUI.Services.DTOs;

namespace Sagaway.ReservationDemo.ReservationUI.Services;

public record ReservationState
{
    public Guid ReservationId { get; init; }
    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string CarClassCode { get; init; } = string.Empty;
    public ReservationStatusType Status { get; set; } = ReservationStatusType.Pending;
    public bool IsProcessing { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string SagaLog { get; init; } = string.Empty;
}
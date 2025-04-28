using System.Text.Json.Serialization;

namespace Sagaway.ReservationDemo.ReservationUI.Services;

public class ReservationResult
{
    [JsonPropertyName("reservationId")]
    public Guid ReservationId { get; set; }

    [JsonPropertyName("customerName")]
    public string? CustomerName { get; set; }

    [JsonPropertyName("carClass")]
    public string? CarClass { get; set; }
}
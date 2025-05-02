using System.Text.Json.Serialization;

namespace Sagaway.ReservationDemo.ReservationUI.Services.DTOs;

/// <summary>
/// Represents the status/details of a single reservation.
/// Mirrors the server's BookingInfo record.
/// </summary>
public class ReservationStatus // Renamed for clarity on client-side, but maps to BookingInfo
{
    [JsonPropertyName("id")] // Maps to BookingInfo.Id
    public Guid ReservationId { get; set; } // Renamed from 'Id' for client-side consistency

    [JsonPropertyName("customerName")]
    public string? CustomerName { get; set; }

    [JsonPropertyName("carClass")] 
    public string? CarClass { get; set; }

    [JsonPropertyName("isReserved")] // Maps to BookingInfo.IsReserved
    public bool IsReserved { get; set; }
}
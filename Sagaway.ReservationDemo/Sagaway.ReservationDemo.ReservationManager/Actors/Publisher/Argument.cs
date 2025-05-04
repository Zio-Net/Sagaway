using System.Text.Json;

namespace Sagaway.ReservationDemo.ReservationManager.Actors.Publisher;

public record Argument
{
    public string Sender { get; set; } = string.Empty;
    // ReSharper disable once PropertyCanBeMadeInitOnly.Global
    public JsonDocument? Text { get; set; }
}
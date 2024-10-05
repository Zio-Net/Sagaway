using System.Text.Json;

namespace Sagaway.Routing.Tracking;

public record Argument
{
    public string Sender { get; set; } = string.Empty;
    // ReSharper disable once PropertyCanBeMadeInitOnly.Global
    public string? Text { get; set; }
}
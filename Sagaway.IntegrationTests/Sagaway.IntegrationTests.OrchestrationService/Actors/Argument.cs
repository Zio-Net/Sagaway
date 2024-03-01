using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sagaway.IntegrationTests.OrchestrationService.Actors;

public record Argument
{
    public string Sender { get; set; } = string.Empty;
    // ReSharper disable once PropertyCanBeMadeInitOnly.Global
    public JsonDocument? Text { get; set; } 
}
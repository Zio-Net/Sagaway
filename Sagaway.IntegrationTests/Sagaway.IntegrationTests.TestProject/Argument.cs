using System.Text.Json.Nodes;

namespace Sagaway.IntegrationTests.TestProject;

// ReSharper disable once ClassNeverInstantiated.Global
public record Argument
{
    public string Sender { get; set; } = string.Empty;
    public JsonObject Text { get; set; } = new JsonObject();
}
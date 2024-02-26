namespace Sagaway.IntegrationTests.OrchestrationService.Actors;

public record Argument
{
    public string Sender { get; set; } = "dapr";
    public string Text { get; set; } = string.Empty;
}
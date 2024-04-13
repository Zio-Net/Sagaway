namespace Sagaway.IntegrationTests.TestProject;

// ReSharper disable once ClassNeverInstantiated.Global
public class Span
{
    public required string TraceId { get; set; }
    public string? ParentId { get; set; }
    public string? Id { get; set; }
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public long Timestamp { get; set; }
    public long Duration { get; set; }
    public Endpoint? LocalEndpoint { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}
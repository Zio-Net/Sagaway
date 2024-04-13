namespace Sagaway.IntegrationTests.TestProject;

// ReSharper disable once ClassNeverInstantiated.Global
public class Endpoint
{
    public required string ServiceName { get; set; }
    public required string Ipv4 { get; set; }
    public int? Port { get; set; }
}
namespace Sagaway.Telemetry;

public record SagaTelemetryContext(string SagaId, string SagaType, ITelemetryDataPersistence TelemetryDataPersistence)
{
    public string SagaId { get; set; } = SagaId;
    public string SagaType { get; set; } = SagaType;

    public ITelemetryDataPersistence TelemetryDataPersistence { get; set; } = TelemetryDataPersistence;
}
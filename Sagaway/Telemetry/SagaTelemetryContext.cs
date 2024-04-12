using Microsoft.Extensions.Logging;

namespace Sagaway.Telemetry;

public record SagaTelemetryContext(string SagaId, string SagaType, ILogger Logger, ITelemetryDataPersistence TelemetryDataPersistence)
{
    public string SagaId { get; set; } = SagaId;
    public string SagaType { get; set; } = SagaType;
    public ILogger Logger { get; set; } = Logger;

    public ITelemetryDataPersistence TelemetryDataPersistence { get; set; } = TelemetryDataPersistence;
}
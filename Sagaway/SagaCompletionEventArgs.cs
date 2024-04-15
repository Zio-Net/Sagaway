namespace Sagaway
{
    public class SagaCompletionEventArgs(string sagaId, SagaCompletionStatus status, string log) : EventArgs
    {
        public string SagaId { get; } = sagaId;
        public SagaCompletionStatus Status { get; } = status;
        public string Log { get; } = log;
    }
}
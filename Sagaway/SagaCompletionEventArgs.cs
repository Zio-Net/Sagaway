namespace Sagaway
{
    public class SagaCompletionEventArgs : EventArgs
    {
        public SagaCompletionEventArgs(string sagaId, SagaCompletionStatus status, string log)
        {
            SagaId = sagaId;
            Status = status;
            Log = log;
        }

        public string SagaId { get; }
        public SagaCompletionStatus Status { get; }
        public string Log { get; }
    }
}
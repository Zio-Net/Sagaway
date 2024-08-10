namespace Sagaway;

public class CorruptedSagaStateException : Exception
{
    public CorruptedSagaStateException(string message) : base(message)
    {
    }

    public CorruptedSagaStateException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
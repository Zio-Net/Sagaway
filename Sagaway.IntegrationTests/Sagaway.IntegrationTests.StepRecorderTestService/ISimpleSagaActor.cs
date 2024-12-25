using Dapr.Actors;

public interface ISimpleSagaActor : IActor
{
    Task RunSagaAsync(string stepRecorderType);
}
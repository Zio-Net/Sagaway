namespace Sagaway.Routing.Tracking;

public interface ISignalRPublisher
{
    Task PublishMessageToSignalRAsync(string testName, string? callChainResult);
}
namespace Sagaway.Callback.Router;

public interface ISagawayActor : Dapr.Actors.IActor
{
    Task DispatchCallbackAsync(string payloadJson, string methodName);
}
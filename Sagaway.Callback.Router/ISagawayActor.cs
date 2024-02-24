using System.Text.Json.Nodes;
using Dapr.Client.Autogen.Grpc.v1;

namespace Sagaway.Callback.Router;

public interface ISagawayActor : Dapr.Actors.IActor
{
    Task DispatchCallbackAsync(string payloadJson, string methodName);
}
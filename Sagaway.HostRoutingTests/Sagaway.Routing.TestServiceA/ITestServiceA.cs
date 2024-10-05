using Sagaway.Routing.Tracking;

namespace Sagaway.Routing.TestServiceA;

public interface ITestServiceA
{
    Task InvokeAsync(CallChainInfo request);
    Task OnResultAsync(CallChainInfo result);
}
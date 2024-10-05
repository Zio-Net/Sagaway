using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Sagaway.RoutingTests.Logging;

public class XUnitLoggerProvider(ITestOutputHelper testOutputHelper) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
        => new XUnitLogger(testOutputHelper, categoryName);

    public void Dispose()
    { }
}
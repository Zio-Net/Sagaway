using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Sagaway.IntegrationTests.TestProject.Logging;

internal class XUnitLogger(ITestOutputHelper testOutputHelper, string categoryName) : ILogger
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull
        => NoopDisposable.Instance;

    public bool IsEnabled(LogLevel logLevel)
        => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception, string> formatter)
    {
        try
        {
            testOutputHelper.WriteLine($"{categoryName} [{eventId}] {formatter(state, exception!)}");

            if (exception != null)
                testOutputHelper.WriteLine(exception.ToString());
        }
        catch (InvalidOperationException)
        {
            //no active test, ignore!
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new NoopDisposable();
        public void Dispose()
        { }
    }
}
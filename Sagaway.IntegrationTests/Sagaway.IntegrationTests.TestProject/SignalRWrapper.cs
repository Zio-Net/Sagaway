using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Sagaway.IntegrationTests.TestProject.Logging;
using Xunit.Abstractions;

namespace Sagaway.IntegrationTests.TestProject;

public class SignalRWrapper : ISignalRWrapper
{
    private HubConnection _signalRHubConnection;
    private readonly List<JsonObject> _signalRMessagesReceived = new();
    private readonly SemaphoreSlim _signalRMessageReceived = new(0);
    private readonly object _lock = new();
    private readonly string _token;

    public SignalRWrapper(ITestOutputHelper testOutputHelper, string token = "")
    {
        _token = token;
        var signalRUrl = Environment.GetEnvironmentVariable("SIGNALR_URL");
        if (string.IsNullOrEmpty(signalRUrl))
        {
            signalRUrl = "http://localhost:6969/";
        }

        _signalRHubConnection = new HubConnectionBuilder()
            .WithUrl(signalRUrl, c =>
            {
                c.Headers.Add("authorization", token);
            })
            .WithAutomaticReconnect().ConfigureLogging(lb =>
            {
                lb.AddProvider(new XUnitLoggerProvider(testOutputHelper));
                lb.SetMinimumLevel(LogLevel.Debug);
            })
            .Build();
        TestOutputHelper = testOutputHelper;
    }

    async Task ISignalRWrapper.SwitchUserAsync(string alternativeUser, string eventName)
    {
        // Stop the current connection
        await _signalRHubConnection.StopAsync();

        // Create a new connection with the new user token
        var signalRUrl = Environment.GetEnvironmentVariable("SIGNALR_URL") ?? "http://localhost/notificationmanager/";
        _signalRHubConnection = new HubConnectionBuilder()
            .WithUrl(signalRUrl, options =>
            {
                options.Headers.Add("authorization", _token);
                options.Headers.Add("testUser", alternativeUser);
            })
            .WithAutomaticReconnect()
            .ConfigureLogging(lb =>
            {
                lb.AddProvider(new XUnitLoggerProvider(TestOutputHelper));
                lb.SetMinimumLevel(LogLevel.Debug);
            })
            .Build();

        // Start the new connection
        await ((ISignalRWrapper)this).StartSignalRAsync(eventName);
    }

    async Task ISignalRWrapper.StartSignalRAsync(params string[] eventNames)
    {
        try
        {
            ClearMessages();

            if (_signalRHubConnection.State == HubConnectionState.Connected)
            {
                ((ISignalRWrapper)this).ListenToSignalR(eventNames);
                return;
            }


            await _signalRHubConnection.StartAsync();
            ((ISignalRWrapper)this).ListenToSignalR(eventNames);
        }
        catch (Exception e)
        {
            try
            {
                TestOutputHelper.WriteLine(e.Message);
            }
            catch
            {
                Console.WriteLine(e.Message);
            }
            throw;
        }
    }

    void ISignalRWrapper.ListenToSignalR(params string[] eventNames)
    {
        try
        {
            if (_signalRHubConnection.State != HubConnectionState.Connected)
                throw new Exception("SignalR is not connected");

            foreach (var eventName in eventNames)
            {
                _signalRHubConnection.On<Argument>(eventName, message =>
                {
                    lock (_lock)
                    {
                        _signalRMessagesReceived.Add(message.Text);
                        _signalRMessageReceived.Release();
                    }
                });
            }
        }
        catch (Exception e)
        {
            try
            {
                TestOutputHelper.WriteLine(e.Message);
            }
            catch
            {
                Console.WriteLine(e.Message);
            }
            throw;
        }
    }

    public void ClearMessages()
    {
        lock (_lock)
        {
            _signalRMessagesReceived.Clear();
        }
    }

    async Task<bool> ISignalRWrapper.WaitForSignalREventAsync(int timeoutInSeconds)
    {
        var isSucceeded = await _signalRMessageReceived.WaitAsync(timeoutInSeconds * 1000);
        await Task.Delay(1000);
        return isSucceeded;
    }

    async Task<bool> ISignalRWrapper.WaitForSignalREventWithConditionAsync(int timeoutInSeconds, Func<IReadOnlyList<JsonObject>, bool> condition)
    {
        var startTime = DateTimeOffset.UtcNow;
        bool result;
        do
        {
            var timeToWait = (int)(timeoutInSeconds - (DateTimeOffset.UtcNow - startTime).TotalSeconds);
            if (timeToWait <= 0)
            {
                result = condition(((ISignalRWrapper)this).Messages);
                break;
            }

            if (!condition(((ISignalRWrapper)this).Messages))
                await _signalRMessageReceived.WaitAsync(timeToWait * 1000);

        } while (!(result = condition(((ISignalRWrapper)this).Messages)));

        return result;
    }

    IReadOnlyList<JsonObject> ISignalRWrapper.Messages
    {
        get
        {
            lock (_lock)
            {
                //copy the messages
                return _signalRMessagesReceived.ToList().AsReadOnly();
            }
        }
    }

    public ITestOutputHelper TestOutputHelper { get; }

    public void Dispose()
    {
        if (_signalRHubConnection != null! && _signalRHubConnection.State == HubConnectionState.Connected)
            Task.Run(async () =>
            {
                await _signalRHubConnection.StopAsync();
                await _signalRHubConnection.DisposeAsync();
            }).Wait();
    }
}
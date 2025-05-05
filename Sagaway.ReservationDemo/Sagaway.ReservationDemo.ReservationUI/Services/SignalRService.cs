using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Sagaway.ReservationDemo.ReservationUI.Services.DTOs;

namespace Sagaway.ReservationDemo.ReservationUI.Services;

public class SignalRService : ISignalRService, IAsyncDisposable
{
    private readonly NavigationManager _navigationManager;
    private readonly ILogger<SignalRService> _logger;
    private HubConnection? _hubConnection;
        
    public event Action<SagaUpdate>? OnSagaCompleted;

    // ReSharper disable once ConvertToPrimaryConstructor
    public SignalRService(NavigationManager navigationManager, ILogger<SignalRService> logger)
    {
        _navigationManager = navigationManager;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        if (_hubConnection != null)
        {
            return;
        }
            
        // Construct the SignalR hub URL - this will be proxied through Nginx
        var hubUrl = _navigationManager.ToAbsoluteUri("/reservationcallback");

        // Get negotiate endpoint to establish the connection
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                // First connect to negotiate endpoint to get connection info
                options.SkipNegotiation = false;
            })
            .WithAutomaticReconnect([TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)
            ])
            .Build();

        // Register handlers for SignalR messages
        // In SignalRService.cs
        _hubConnection.On<Argument>("SagaCompleted", argument =>
        {
            try
            {
                _logger.LogInformation("SignalR message received on SagaCompleted handler: {Args}",
                    argument.Text);

                // Create a SagaUpdate from the JSON properties in Text
                var update = new SagaUpdate
                {
                    // Extract properties directly from the JsonObject
                    ReservationId = argument.Text["reservationId"]?.GetValue<Guid>() ?? Guid.Empty,
                    Outcome = argument.Text["outcome"]?.GetValue<string>() ?? string.Empty,
                    Log = argument.Text["log"]?.GetValue<string>() ?? string.Empty,
                    CarClass = argument.Text["carClass"]?.GetValue<string>() ?? string.Empty,
                    CustomerName = argument.Text["customerName"]?.GetValue<string>() ?? string.Empty,
                };

                _logger.LogInformation($"SignalR: Saga completed for reservation {update.ReservationId} with outcome {update.Outcome}");
                OnSagaCompleted?.Invoke(update);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing SignalR message");
            }
        });




        try
        {
            await _hubConnection.StartAsync();
            _logger.LogInformation("SignalR connection started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting SignalR connection");
        }
    }

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}
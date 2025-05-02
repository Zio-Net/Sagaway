using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Sagaway.ReservationDemo.ReservationUI.Services.DTOs;

namespace Sagaway.ReservationDemo.ReservationUI.Services
{
    /// <summary>
    /// Interface for the client accessing the Reservation API.
    /// </summary>

    /// <summary>
    /// Implementation of the client accessing the Reservation API.
    /// </summary>

    public class ReservationApiClient : IReservationApiClient
    {
        private readonly HttpClient _http;
        private readonly NavigationManager _navigationManager; // Inject NavigationManager
        private readonly ILogger<ReservationApiClient> _logger;

        // Constructor injection for HttpClient, NavigationManager, and ILogger
        // ReSharper disable once ConvertToPrimaryConstructor
        public ReservationApiClient(HttpClient http, NavigationManager navigationManager,
            ILogger<ReservationApiClient> logger)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _navigationManager =
                navigationManager ??
                throw new ArgumentNullException(nameof(navigationManager)); // Store injected NavigationManager
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Initiates a car reservation request via a POST call to /reserve.
        /// </summary>
        public async Task<ReservationResult?> ReserveCarAsync(string customerName, string carClass,
            Guid? reservationId = null)
        {
            // Build the relative path and query string
            var relativePathAndQuery =
                $"reserve?customerName={Uri.EscapeDataString(customerName)}&carClass={Uri.EscapeDataString(carClass)}";
            if (reservationId.HasValue && reservationId != Guid.Empty)
            {
                relativePathAndQuery += $"&reservationId={reservationId.Value}";
            }

            // *** CHANGED: Construct Absolute URI using NavigationManager ***
            var absoluteUri = _navigationManager.ToAbsoluteUri(relativePathAndQuery);
            _logger.LogInformation("Sending POST request to absolute URI: {AbsoluteUri}", absoluteUri);

            // Use the absolute URI when creating the HttpRequestMessage
            using var request = new HttpRequestMessage(HttpMethod.Post, absoluteUri);

            try
            {
                // Send the request using SendAsync
                using var response = await _http.SendAsync(request);
                response.EnsureSuccessStatusCode(); // Throws HttpRequestException on non-success

                var result = await response.Content.ReadFromJsonAsync<ReservationResult>();
                _logger.LogInformation("Successfully initiated reservation via /reserve, ID: {ReservationId}",
                    result?.ReservationId);
                return result;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed when calling {AbsoluteUri}. Status Code: {StatusCode}",
                    absoluteUri, ex.StatusCode);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred during ReserveCarAsync for URI {AbsoluteUri}",
                    absoluteUri);
                throw;
            }
        }

        /// <summary>
        /// Retrieves reservation statuses for a customer via a GET call to /reservations/{customerName}.
        /// </summary>
        public async Task<List<ReservationStatus>?> GetReservationsAsync(string customerName)
        {
            var relativePath = $"reservations/{Uri.EscapeDataString(customerName)}";
            var absoluteUri = _navigationManager.ToAbsoluteUri(relativePath); // Construct absolute URI
            _logger.LogInformation("Sending GET request to absolute URI: {AbsoluteUri}", absoluteUri);

            using var request = new HttpRequestMessage(HttpMethod.Get, absoluteUri); // Use absolute URI

            try
            {
                using var response = await _http.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation(
                        "No reservations found for customer {CustomerName} via {AbsoluteUri} (404)", customerName,
                        absoluteUri);
                    return [];
                }

                response.EnsureSuccessStatusCode();

                var statuses = await response.Content.ReadFromJsonAsync<List<ReservationStatus>>();
                _logger.LogInformation(
                    "Successfully retrieved {Count} reservation statuses for {CustomerName} via {AbsoluteUri}",
                    statuses?.Count ?? 0, customerName, absoluteUri);
                return statuses;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed when calling {AbsoluteUri}. Status Code: {StatusCode}",
                    absoluteUri, ex.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "An unexpected error occurred during GetReservationsAsync for URI {AbsoluteUri}", absoluteUri);
                return null;
            }
        }

        /// <summary>
        /// Retrieves the details of a specific reservation via GET /reservation/{reservationId}.
        /// </summary>
        public async Task<ReservationStatus?> GetReservationAsync(Guid reservationId)
        {
            var relativePath = $"reservation/{reservationId:D}";
            var absoluteUri = _navigationManager.ToAbsoluteUri(relativePath); // Construct absolute URI
            _logger.LogInformation("Sending GET request to absolute URI: {AbsoluteUri}", absoluteUri);

            using var request = new HttpRequestMessage(HttpMethod.Get, absoluteUri); // Use absolute URI

            try
            {
                using var response = await _http.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("Reservation {ReservationId} not found via {AbsoluteUri} (404)",
                        reservationId, absoluteUri);
                    return null;
                }

                response.EnsureSuccessStatusCode();

                var status = await response.Content.ReadFromJsonAsync<ReservationStatus>();
                _logger.LogInformation(
                    "Successfully retrieved reservation status for {ReservationId} via {AbsoluteUri}",
                    reservationId, absoluteUri);
                return status;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed when calling {AbsoluteUri}. Status Code: {StatusCode}",
                    absoluteUri, ex.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "An unexpected error occurred during GetReservationAsync for URI {AbsoluteUri}", absoluteUri);
                return null;
            }
        }

        /// <summary>
        /// Retrieves the saga log for a specific reservation via GET /saga-log/{reservationId}.
        /// </summary>
        public async Task<string?> GetSagaLogAsync(Guid reservationId)
        {
            var relativePath = $"saga-log/{reservationId:D}";
            var absoluteUri = _navigationManager.ToAbsoluteUri(relativePath); // Construct absolute URI
            _logger.LogInformation("Sending GET request for saga log to absolute URI: {AbsoluteUri}", absoluteUri);

            using var request = new HttpRequestMessage(HttpMethod.Get, absoluteUri); // Use absolute URI

            try
            {
                using var response = await _http.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("Saga log for reservation {ReservationId} not found via {AbsoluteUri} (404)",
                        reservationId, absoluteUri);
                    return null;
                }

                response.EnsureSuccessStatusCode();

                var sagaLog = await response.Content.ReadAsStringAsync();
                _logger.LogInformation(
                    "Successfully retrieved saga log for reservation {ReservationId} via {AbsoluteUri}",
                    reservationId, absoluteUri);
                return sagaLog;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed when calling {AbsoluteUri} for saga log. Status Code: {StatusCode}",
                    absoluteUri, ex.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "An unexpected error occurred during GetSagaLogAsync for URI {AbsoluteUri}", absoluteUri);
                return null;
            }
        }

        /// <summary>
        /// Initiates the cancellation process for a specific reservation via POST /cancel.
        /// </summary>
        public async Task<bool> CancelReservationAsync(Guid reservationId)
        {
            var relativePathAndQuery = $"cancel?reservationId={reservationId:D}";
            var absoluteUri = _navigationManager.ToAbsoluteUri(relativePathAndQuery); // Construct absolute URI
            _logger.LogInformation("Sending POST request to absolute URI: {AbsoluteUri}", absoluteUri);

            using var request = new HttpRequestMessage(HttpMethod.Post, absoluteUri); // Use absolute URI

            try
            {
                using var response = await _http.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "Successfully requested cancellation for reservation {ReservationId} via {AbsoluteUri}",
                        reservationId, absoluteUri);
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning(
                        "Cancellation request failed for reservation {ReservationId} via {AbsoluteUri}. Status Code: {StatusCode}. Response: {Response}",
                        reservationId, absoluteUri, response.StatusCode, errorContent);
                    return false;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed when calling {AbsoluteUri}. Status Code: {StatusCode}",
                    absoluteUri, ex.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "An unexpected error occurred during CancelReservationAsync for URI {AbsoluteUri}",
                    absoluteUri);
                return false;
            }
        }
    }
}
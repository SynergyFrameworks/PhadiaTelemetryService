using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using PhadiaBackgroundService.Abstracts;
using PhadiaBackgroundService.Model;
using Polly;
using System.Text;
using System.Text.Json;

namespace PhadiaBackgroundService.Infrastructure;

public class TelemetryServiceClient : ITelemetryServiceClient
{
    private readonly EventHubProducerClient _eventHubClient;
    private readonly ILogger<TelemetryServiceClient> _logger;
    private readonly IAsyncPolicy<TelemetryResponse> _retryPolicy;
    public TelemetryServiceClient(string eventHubConnectionString, string eventHubName, ILogger<TelemetryServiceClient> logger)
    {
        if (string.IsNullOrEmpty(eventHubConnectionString))
            throw new ArgumentNullException(nameof(eventHubConnectionString));
        if (string.IsNullOrEmpty(eventHubName))
            throw new ArgumentNullException(nameof(eventHubName));

        _eventHubClient = new EventHubProducerClient(eventHubConnectionString, eventHubName);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _retryPolicy = Policy<TelemetryResponse>
          .Handle<Exception>()
          .WaitAndRetryAsync(3, retryAttempt =>
         TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    public async Task<TelemetryResponse> SendTelemetryAsync(TelemetryRequest request)
    {
        if (request == null || request.Data == null)
            throw new ArgumentNullException(nameof(request));


        return await _retryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                string telemetryDataJson = JsonSerializer.Serialize(request.Data);
                using EventDataBatch eventBatch = await _eventHubClient.CreateBatchAsync();
                var eventData = new EventData(Encoding.UTF8.GetBytes(telemetryDataJson));
                if (!eventBatch.TryAdd(eventData))
                {
                    throw new InvalidOperationException("Failed to add telemetry data to the event batch.");
                }
                await _eventHubClient.SendAsync(eventBatch);
                _logger.LogInformation("Successfully sent telemetry data to Event Hub");
                return new TelemetryResponse { Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending telemetry data to Event Hub");
                return new TelemetryResponse { Success = false };
            }
        });


    }
}


using PhadiaBackgroundService.Abstracts;
using PhadiaBackgroundService.Infrastructure;
using PhadiaBackgroundService.Model;
using Polly;
using System.Threading.Tasks.Dataflow;

namespace PhadiaBackgroundService.Domain
{
    public class PhadiaTelemetryHandler : IPhadiaTelemetryHandler
    {
        private readonly ILogger<PhadiaTelemetryHandler> _logger;
        private readonly ITelemetryServiceClient _eventHubClient;
        private readonly IGrpcClient _grpcClient;
        private readonly IAsyncPolicy _retryPolicy;
        private readonly Metrics _metrics;

        public PhadiaTelemetryHandler(
            ILogger<PhadiaTelemetryHandler> logger,
            ITelemetryServiceClient eventHubClient,
            IGrpcClient grpcClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventHubClient = eventHubClient ?? throw new ArgumentNullException(nameof(eventHubClient));
            _grpcClient = grpcClient ?? throw new ArgumentNullException(nameof(grpcClient));
            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
            _metrics = new Metrics();
        }

        // TPL block for processing telemetry data in parallel
        public async Task HandleTelemetryDataAsync(AllergenTelemetryData data, CancellationToken stoppingToken)
        {
            var processingBlock = new TransformBlock<AllergenTelemetryData, AllergenTelemetryData>(
                async incomingData =>
                {
                    _logger.LogInformation("Pre-processing telemetry data from file.");
                    // You can add any additional pre-processing here
                    return incomingData;
                }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = Environment.ProcessorCount });

            var sendToEventHubBlock = new ActionBlock<AllergenTelemetryData>(
                async data =>
                {
                    await _retryPolicy.ExecuteAsync(async () =>
                    {
                        var eventHubResponse = await _eventHubClient.SendTelemetryAsync(new TelemetryRequest { Data = data });
                        _logger.LogInformation("Sent telemetry data to Event Hub with response: {Success}", eventHubResponse.Success);
                        _metrics.IncrementTransmittedData();
                    });
                }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = Environment.ProcessorCount });

            var sendToGrpcBlock = new ActionBlock<AllergenTelemetryData>(
                async data =>
                {
                    await _retryPolicy.ExecuteAsync(async () =>
                    {
                        var grpcResponse = await _grpcClient.SendTelemetryAsync(data);
                        _logger.LogInformation("Sent telemetry data to gRPC endpoint with response: {Success}", grpcResponse);
                        _metrics.IncrementTransmittedData();
                    });
                }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = Environment.ProcessorCount });

            // Link processing block to both Event Hub and gRPC blocks
            processingBlock.LinkTo(sendToEventHubBlock, new DataflowLinkOptions { PropagateCompletion = true });
            processingBlock.LinkTo(sendToGrpcBlock, new DataflowLinkOptions { PropagateCompletion = true });

            try
            {
                // Send data for processing and transmission
                await processingBlock.SendAsync(data, stoppingToken);

                // Signal completion to the blocks
                processingBlock.Complete();
                await Task.WhenAll(sendToEventHubBlock.Completion, sendToGrpcBlock.Completion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while handling telemetry data.");
            }
        }
    }
}

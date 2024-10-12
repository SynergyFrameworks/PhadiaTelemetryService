using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;
using PhadiaBackgroundService.Abstracts;
using PhadiaBackgroundService.Model;
using PhadiaBackgroundService.Infrastructure;
using Polly;

namespace PhadiaBackgroundService.Domain
{
    public class PhadiaTelemetryHandler : IPhadiaTelemetryHandler, IDisposable
    {
        private readonly ILogger<PhadiaTelemetryHandler> _logger;
        private readonly ITelemetryServiceClient _eventHubClient;
        private readonly IGrpcClient _grpcClient;
        private readonly IAsyncPolicy _retryPolicy;
        private readonly Metrics _metrics;
        private readonly BlockingCollection<AllergenTelemetryData> _dataQueue;
        private readonly CancellationTokenSource _cts;
        private readonly Task _processingTask;
        private TaskCompletionSource<bool> _processingCompletionSource;
        private int _processingCount = 0;
        private int _errorCount = 0;

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
            _dataQueue = new BlockingCollection<AllergenTelemetryData>(new ConcurrentQueue<AllergenTelemetryData>());
            _cts = new CancellationTokenSource();
            _processingTask = Task.Run(ProcessQueueAsync);
            _processingCompletionSource = new TaskCompletionSource<bool>();
        }

        public async Task HandleTelemetryDataAsync(AllergenTelemetryData data, CancellationToken stoppingToken)
        {
            _dataQueue.Add(data, stoppingToken);
            Interlocked.Increment(ref _processingCount);
            _logger.LogInformation("Added telemetry data to queue. Current processing count: {Count}", _processingCount);
            await Task.CompletedTask;
        }

        private async Task ProcessQueueAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    if (_dataQueue.TryTake(out var data, 100, _cts.Token))
                    {
                        await ProcessDataThroughPipelineAsync(data);
                    }
                    else if (_dataQueue.Count == 0 && _processingCount == 0)
                    {
                        await Task.Delay(100, _cts.Token);
                        if (_dataQueue.Count == 0 && _processingCount == 0)
                        {
                            _logger.LogInformation("Queue is empty and no processing in progress. Setting completion source.");
                            _processingCompletionSource.TrySetResult(true);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Operation was canceled.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in ProcessQueueAsync");
                }
            }
        }

        private async Task ProcessDataThroughPipelineAsync(AllergenTelemetryData data)
        {
            try
            {
                _logger.LogInformation("Pre-processing telemetry data.");

                await SendToEventHub(data);
                await SendToGrpc(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessDataThroughPipelineAsync");
                Interlocked.Increment(ref _errorCount);
            }
            finally
            {
                if (Interlocked.Decrement(ref _processingCount) == 0 && _dataQueue.Count == 0)
                {
                    _logger.LogInformation("All data processed. Setting completion source.");
                    _processingCompletionSource.TrySetResult(true);
                }
            }
        }


        private async Task SendToEventHub(AllergenTelemetryData data)
        {
            try
            {
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    _logger.LogInformation("Sending telemetry data to Event Hub");
                    var eventHubResponse = await _eventHubClient.SendTelemetryAsync(new TelemetryRequest { Data = data });
                    _logger.LogInformation("Sent telemetry data to Event Hub with response: {Success}", eventHubResponse.Success);
                    _metrics.IncrementTransmittedData();
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending telemetry data to Event Hub after all retries");
                Interlocked.Increment(ref _errorCount);
            }
        }

        private async Task SendToGrpc(AllergenTelemetryData data)
        {
            try
            {
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    _logger.LogInformation("Sending telemetry data to gRPC endpoint");
                    var grpcResponse = await _grpcClient.SendTelemetryAsync(data);
                    _logger.LogInformation("Sent telemetry data to gRPC endpoint with response: {Success}", grpcResponse);
                    _metrics.IncrementTransmittedData();
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending telemetry data to gRPC endpoint after all retries");
                Interlocked.Increment(ref _errorCount);
            }
        }

        public async Task WaitForProcessingCompletionAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await _processingCompletionSource.Task.WaitAsync(cts.Token);
                _logger.LogInformation("Processing completion task completed successfully. Error count: {ErrorCount}", _errorCount);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Waiting for processing completion timed out after {Timeout}", timeout);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while waiting for processing completion");
            }
            finally
            {
                _processingCompletionSource = new TaskCompletionSource<bool>();
                _errorCount = 0;
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _processingTask.Wait();
            _dataQueue.Dispose();
            _cts.Dispose();
        }
    }
}
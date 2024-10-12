using Microsoft.Extensions.Options;
using PhadiaBackgroundService.Abstracts;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Xml.Serialization;
using System.Threading.Tasks.Dataflow;

namespace PhadiaBackgroundService.Infrastructure
{
    public class FileProcessingService
    {
        private readonly ILogger<FileProcessingService> _logger;
        private readonly TelemetryOptions _options;
        private readonly IPhadiaTelemetryHandler _telemetryHandler;
        private readonly string _processedFilesLog;
        private readonly ConcurrentDictionary<string, DateTime> _processedFiles;
        private readonly IFileProcessor _fileProcessor;
        private readonly Metrics _metrics;
        private TaskCompletionSource<bool> _processingCompletionSource;
        private int _remainingFilesCount;
        private SemaphoreSlim _processingLock = new SemaphoreSlim(Environment.ProcessorCount);

        public FileProcessingService(
            ILogger<FileProcessingService> logger,
            IOptions<TelemetryOptions> options,
            IPhadiaTelemetryHandler telemetryHandler,
            IFileProcessor fileProcessor,
            Metrics metrics)
        {
            _logger = logger;
            _options = options.Value;
            _telemetryHandler = telemetryHandler;
            _processedFilesLog = Path.Combine(_options.DirectoryPath, "processed_files.log");
            _processedFiles = new ConcurrentDictionary<string, DateTime>();
            _fileProcessor = fileProcessor;
            _metrics = metrics;
            LoadProcessedFiles();
            _processingCompletionSource = new TaskCompletionSource<bool>();
        }


        public async Task ProcessNewFilesAsync()
        {
            var directory = new DirectoryInfo(_options.DirectoryPath);
            var files = directory.GetFiles()
                .Where(f => !f.Name.Equals("processed_files.log", StringComparison.OrdinalIgnoreCase)
                            && !IsFileProcessed(f.FullName))
                .ToList();

            _logger.LogInformation("Found {Count} new files to process", files.Count);

            _remainingFilesCount = files.Count;
            _processingCompletionSource = new TaskCompletionSource<bool>();

            var tasks = files.Select(async file =>
            {
                await _processingLock.WaitAsync();
                try
                {
                    await ProcessFileAsync(file.FullName);
                    MarkFileAsProcessed(file.FullName);
                    _metrics.IncrementProcessedFiles();
                    _logger.LogInformation("Processed file: {FileName}", file.Name);
                }
                catch (Exception ex)
                {
                    _metrics.IncrementFailedFiles();
                    _logger.LogError(ex, "Error processing file: {FileName}", file.Name);
                }
                finally
                {
                    _processingLock.Release();
                    if (Interlocked.Decrement(ref _remainingFilesCount) == 0)
                    {
                        _processingCompletionSource.TrySetResult(true);
                    }
                }
            });

            await Task.WhenAll(tasks);

            _logger.LogInformation("Processed {SuccessCount} files successfully, {FailureCount} files failed",
                _metrics.ProcessedFiles,
                _metrics.FailedFiles);
        }


        private async Task ProcessFileAsync(string filePath)
        {
            _logger.LogInformation("Processing file: {filePath}", filePath);

            AllergenTelemetryData allergenData = null;

            string fileContent = await _fileProcessor.ReadLargeFileAsync(filePath);

            if (filePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                allergenData = ParseXmlContent(fileContent);
            }
            else if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                allergenData = ParseJsonContent(fileContent);
            }
            else
            {
                throw new InvalidOperationException($"Unsupported file format: {Path.GetExtension(filePath)}");
            }

            if (allergenData != null)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // 30-second timeout
                await _telemetryHandler.HandleTelemetryDataAsync(allergenData, cts.Token);
            }
            else
            {
                throw new InvalidOperationException($"Failed to parse file: {filePath}");
            }
        }

        private AllergenTelemetryData ParseXmlContent(string content)
        {
            try
            {
                using var reader = new StringReader(content);
                var serializer = new XmlSerializer(typeof(AllergenTelemetryData));
                return (AllergenTelemetryData)serializer.Deserialize(reader);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing XML content");
                return null;
            }
        }

        private AllergenTelemetryData ParseJsonContent(string content)
        {
            try
            {
                return JsonSerializer.Deserialize<AllergenTelemetryData>(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing JSON content");
                return null;
            }
        }

        private bool IsFileProcessed(string filePath)
        {
            return _processedFiles.ContainsKey(filePath);
        }

        private void MarkFileAsProcessed(string filePath)
        {
            var processedTime = DateTime.UtcNow;
            if (_processedFiles.TryAdd(filePath, processedTime))
            {
                try
                {
                    File.AppendAllText(_processedFilesLog, $"{filePath},{processedTime:O}{Environment.NewLine}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error writing to processed files log: {FilePath}", _processedFilesLog);
                }
            }
        }

        private void LoadProcessedFiles()
        {
            if (File.Exists(_processedFilesLog))
            {
                foreach (var line in File.ReadLines(_processedFilesLog))
                {
                    var parts = line.Split(',');
                    if (parts.Length == 2 && DateTime.TryParse(parts[1], out var processedTime))
                    {
                        _processedFiles.TryAdd(parts[0].Trim(), processedTime);
                    }
                }
            }
        }



        public async Task WaitForProcessingCompletionAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await _processingCompletionSource.Task.WaitAsync(cts.Token);
                _logger.LogInformation("Processing completion task completed successfully");
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
            }
        }
    }
}
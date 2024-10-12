using Microsoft.Extensions.Options;
using PhadiaBackgroundService.Abstracts;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Xml.Serialization;

namespace PhadiaBackgroundService.Infrastructure
{
    public class FileProcessingService
    {
        private readonly ILogger<FileProcessingService> _logger;
        private readonly TelemetryOptions _options;
        private readonly IPhadiaTelemetryHandler _telemetryHandler;
        private readonly string _processedFilesLog;
        private readonly ConcurrentDictionary<string, byte> _processedFiles;

        public FileProcessingService(
            ILogger<FileProcessingService> logger,
            IOptions<TelemetryOptions> options,
            IPhadiaTelemetryHandler telemetryHandler)
        {
            _logger = logger;
            _options = options.Value;
            _telemetryHandler = telemetryHandler;
            _processedFilesLog = Path.Combine(_options.DirectoryPath, "processed_files.log");
            _processedFiles = new ConcurrentDictionary<string, byte>();
            LoadProcessedFiles();
        }

        public async Task ProcessNewFilesAsync()
        {
            var directory = new DirectoryInfo(_options.DirectoryPath);
            var files = directory.GetFiles()
                .Where(f => !f.Name.Equals("processed_files.log", StringComparison.OrdinalIgnoreCase)
                            && !IsFileProcessed(f.FullName))
                .ToList();

            _logger.LogInformation("Found {Count} new files to process", files.Count);

            foreach (var file in files)
            {
                try
                {
                    await ProcessFileAsync(file.FullName);
                    MarkFileAsProcessed(file.FullName);
                    _logger.LogInformation("Processed file: {FileName}", file.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing file: {FileName}", file.Name);
                }
            }
        }

        private async Task ProcessFileAsync(string filePath)
        {
            try
            {
                _logger.LogInformation("Processing file: {filePath}", filePath);

                AllergenTelemetryData allergenData = null;

                if (filePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    allergenData = ParseXmlFile(filePath);
                }
                else if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    allergenData = await ParseJsonFileAsync(filePath);
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported file format: {Path.GetExtension(filePath)}");
                }

                if (allergenData != null)
                {
                    using var cts = new CancellationTokenSource();
                    await _telemetryHandler.HandleTelemetryDataAsync(allergenData, cts.Token);
                }
                else
                {
                    throw new InvalidOperationException($"Failed to parse file: {filePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file: {filePath}", filePath);
            }
        }

        private AllergenTelemetryData ParseXmlFile(string filePath)
        {
            try
            {
                using var reader = new StreamReader(filePath);
                var serializer = new XmlSerializer(typeof(AllergenTelemetryData));
                return (AllergenTelemetryData)serializer.Deserialize(reader);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing XML file: {filePath}", filePath);
                return null;
            }
        }

        private async Task<AllergenTelemetryData> ParseJsonFileAsync(string filePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<AllergenTelemetryData>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing JSON file: {filePath}", filePath);
                return null;
            }
        }

        private bool IsFileProcessed(string filePath)
        {
            return _processedFiles.ContainsKey(filePath);
        }

        private void MarkFileAsProcessed(string filePath)
        {
            if (_processedFiles.TryAdd(filePath, 1))
            {
                try
                {
                    File.AppendAllText(_processedFilesLog, filePath + Environment.NewLine);
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
                    _processedFiles.TryAdd(line.Trim(), 1);
                }
            }
        }
    }
}

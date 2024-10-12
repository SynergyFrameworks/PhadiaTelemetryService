using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PhadiaBackgroundService.Abstracts;
using PhadiaBackgroundService.Infrastructure;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PhadiaBackgroundService.Tests
{
    public class FileProcessingServiceTests : IDisposable
    {
        private readonly Mock<ILogger<FileProcessingService>> _loggerMock;
        private readonly Mock<IOptions<TelemetryOptions>> _optionsMock;
        private readonly Mock<IPhadiaTelemetryHandler> _telemetryHandlerMock;
        private readonly Mock<IFileProcessor> _fileProcessorMock;
        private readonly Metrics _metrics;
        private readonly string _tempPath;

        public FileProcessingServiceTests()
        {
            _loggerMock = new Mock<ILogger<FileProcessingService>>();
            _optionsMock = new Mock<IOptions<TelemetryOptions>>();
            _telemetryHandlerMock = new Mock<IPhadiaTelemetryHandler>();
            _fileProcessorMock = new Mock<IFileProcessor>();
            _metrics = new Metrics();
            _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempPath);
            _optionsMock.Setup(o => o.Value).Returns(new TelemetryOptions { DirectoryPath = _tempPath });
        }

        private FileProcessingService CreateFileProcessingService()
        {
            return new FileProcessingService(
                _loggerMock.Object,
                _optionsMock.Object,
                _telemetryHandlerMock.Object,
                _fileProcessorMock.Object,
                _metrics);
        }

        [Fact]
        public async Task ProcessNewFilesAsync_ProcessesNewFiles()
        {
            // Arrange
            var service = CreateFileProcessingService();
            var testFiles = CreateTestFiles(3);
            _fileProcessorMock.Setup(fp => fp.ReadLargeFileAsync(It.IsAny<string>())).ReturnsAsync("{}");

            // Act
            await service.ProcessNewFilesAsync();
            await service.WaitForProcessingCompletionAsync(TimeSpan.FromSeconds(10));

            // Assert
            _telemetryHandlerMock.Verify(h => h.HandleTelemetryDataAsync(It.IsAny<AllergenTelemetryData>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
            VerifyLoggerCalls(LogLevel.Information, "Processed file", Times.Exactly(3));
            VerifyProcessedFilesLog(testFiles);
            Assert.Equal(3, _metrics.ProcessedFiles);

            // Run again to ensure no files are reprocessed
            await service.ProcessNewFilesAsync();
            await service.WaitForProcessingCompletionAsync(TimeSpan.FromSeconds(5));
            _telemetryHandlerMock.Verify(h => h.HandleTelemetryDataAsync(It.IsAny<AllergenTelemetryData>(), It.IsAny<CancellationToken>()), Times.Exactly(3));

            // Create a new file and verify only it is processed
            var newFile = CreateTestFile("test4.json");
            await service.ProcessNewFilesAsync();
            await service.WaitForProcessingCompletionAsync(TimeSpan.FromSeconds(5));
            _telemetryHandlerMock.Verify(h => h.HandleTelemetryDataAsync(It.IsAny<AllergenTelemetryData>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
            Assert.Equal(4, _metrics.ProcessedFiles);
        }

        [Fact]
        public async Task ProcessNewFilesAsync_ProcessesXmlFile()
        {
            // Arrange
            var service = CreateFileProcessingService();
            var xmlContent = "<AllergenTelemetryData><AllergenName>Test</AllergenName></AllergenTelemetryData>";
            var xmlFile = CreateTestFile("test.xml", xmlContent);
            _fileProcessorMock.Setup(fp => fp.ReadLargeFileAsync(It.IsAny<string>())).ReturnsAsync(xmlContent);

            // Act
            await service.ProcessNewFilesAsync();
            await service.WaitForProcessingCompletionAsync(TimeSpan.FromSeconds(5));

            // Assert
            _telemetryHandlerMock.Verify(h => h.HandleTelemetryDataAsync(It.Is<AllergenTelemetryData>(d => d.AllergenName == "Test"), It.IsAny<CancellationToken>()), Times.Once);
            VerifyLoggerCalls(LogLevel.Information, "Processed file", Times.Once());
            Assert.Equal(1, _metrics.ProcessedFiles);
        }

        [Fact]
        public async Task ProcessNewFilesAsync_ProcessesJsonFile()
        {
            // Arrange
            var service = CreateFileProcessingService();
            var jsonContent = "{\"AllergenName\": \"Test\"}";
            var jsonFile = CreateTestFile("test.json", jsonContent);
            _fileProcessorMock.Setup(fp => fp.ReadLargeFileAsync(It.IsAny<string>())).ReturnsAsync(jsonContent);

            // Act
            await service.ProcessNewFilesAsync();
            await service.WaitForProcessingCompletionAsync(TimeSpan.FromSeconds(5));

            // Assert
            _telemetryHandlerMock.Verify(h => h.HandleTelemetryDataAsync(It.Is<AllergenTelemetryData>(d => d.AllergenName == "Test"), It.IsAny<CancellationToken>()), Times.Once);
            VerifyLoggerCalls(LogLevel.Information, "Processed file", Times.Once());
            Assert.Equal(1, _metrics.ProcessedFiles);
        }

        [Fact]
        public async Task ProcessNewFilesAsync_HandlesInvalidFileFormat()
        {
            // Arrange
            var service = CreateFileProcessingService();
            var invalidContent = "This is not a valid XML or JSON file";
            var invalidFile = CreateTestFile("test.txt", invalidContent);
            _fileProcessorMock.Setup(fp => fp.ReadLargeFileAsync(It.IsAny<string>())).ReturnsAsync(invalidContent);

            // Act
            await service.ProcessNewFilesAsync();
            await service.WaitForProcessingCompletionAsync(TimeSpan.FromSeconds(5));

            // Assert
            _telemetryHandlerMock.Verify(h => h.HandleTelemetryDataAsync(It.IsAny<AllergenTelemetryData>(), It.IsAny<CancellationToken>()), Times.Never);
            VerifyLoggerCalls(LogLevel.Error, "Error processing file", Times.Once());
            Assert.Equal(0, _metrics.ProcessedFiles);
            Assert.Equal(1, _metrics.FailedFiles);
        }

        [Fact]
        public async Task ProcessNewFilesAsync_ProcessesFilesConcurrently()
        {
            // Arrange
            var service = CreateFileProcessingService();
            var testFiles = CreateTestFiles(10);
            _fileProcessorMock.Setup(fp => fp.ReadLargeFileAsync(It.IsAny<string>())).ReturnsAsync("{}");

            // Act
            await service.ProcessNewFilesAsync();
            await service.WaitForProcessingCompletionAsync(TimeSpan.FromSeconds(15));

            // Assert
            _telemetryHandlerMock.Verify(h => h.HandleTelemetryDataAsync(It.IsAny<AllergenTelemetryData>(), It.IsAny<CancellationToken>()), Times.Exactly(10));
            VerifyLoggerCalls(LogLevel.Information, "Processed file", Times.Exactly(10));
            VerifyProcessedFilesLog(testFiles);
            Assert.Equal(10, _metrics.ProcessedFiles);
            Assert.Equal(0, _metrics.FailedFiles);
        }

        private string[] CreateTestFiles(int count)
        {
            var files = new string[count];
            for (int i = 0; i < count; i++)
            {
                files[i] = CreateTestFile($"test{i + 1}.json", "{}");
            }
            return files;
        }

        private string CreateTestFile(string fileName, string content = "{}")
        {
            var filePath = Path.Combine(_tempPath, fileName);
            File.WriteAllText(filePath, content);
            File.SetCreationTime(filePath, DateTime.Now.AddMinutes(-1)); // Ensure file is within processing window
            return filePath;
        }

        private void VerifyLoggerCalls(LogLevel logLevel, string messageContains, Times times)
        {
            _loggerMock.Verify(
                x => x.Log(
                    logLevel,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString().Contains(messageContains)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                times);
        }

        private void VerifyProcessedFilesLog(string[] expectedFiles)
        {
            var logPath = Path.Combine(_tempPath, "processed_files.log");
            Assert.True(File.Exists(logPath));
            var processedFiles = File.ReadAllLines(logPath);
            Assert.Equal(expectedFiles.Length, processedFiles.Length);
            Assert.All(expectedFiles, file => Assert.Contains(processedFiles, line => line.StartsWith(file)));
        }

        public void Dispose()
        {
            Directory.Delete(_tempPath, true);
        }
    }
}
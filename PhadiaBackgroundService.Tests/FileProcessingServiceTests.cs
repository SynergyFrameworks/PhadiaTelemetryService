using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PhadiaBackgroundService.Abstracts;
using PhadiaBackgroundService.Infrastructure;
using PhadiaBackgroundService.Model;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PhadiaBackgroundService.Tests
{
    public class FileProcessingServiceTests
    {
        private readonly Mock<ILogger<FileProcessingService>> _loggerMock;
        private readonly Mock<IOptions<TelemetryOptions>> _optionsMock;
        private readonly Mock<IPhadiaTelemetryHandler> _telemetryHandlerMock;
        private readonly string _tempPath;

        public FileProcessingServiceTests()
        {
            _loggerMock = new Mock<ILogger<FileProcessingService>>();
            _optionsMock = new Mock<IOptions<TelemetryOptions>>();
            _telemetryHandlerMock = new Mock<IPhadiaTelemetryHandler>();
            _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempPath);
            _optionsMock.Setup(o => o.Value).Returns(new TelemetryOptions { DirectoryPath = _tempPath });
        }

        [Fact]
        public async Task ProcessNewFilesAsync_ProcessesNewFiles()
        {
            // Arrange
            var service = new FileProcessingService(_loggerMock.Object, _optionsMock.Object, _telemetryHandlerMock.Object);
            var testFiles = CreateTestFiles(3);

            // Act
            await service.ProcessNewFilesAsync();

            // Assert
            _telemetryHandlerMock.Verify(h => h.HandleTelemetryDataAsync(It.IsAny<AllergenTelemetryData>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
            VerifyLoggerCalls(LogLevel.Information, "Processed file", Times.Exactly(3));
            VerifyProcessedFilesLog(testFiles);

            // Run again to ensure no files are reprocessed
            await service.ProcessNewFilesAsync();
            _telemetryHandlerMock.Verify(h => h.HandleTelemetryDataAsync(It.IsAny<AllergenTelemetryData>(), It.IsAny<CancellationToken>()), Times.Exactly(3));

            // Create a new file and verify only it is processed
            var newFile = CreateTestFile("test4.json");
            await service.ProcessNewFilesAsync();
            _telemetryHandlerMock.Verify(h => h.HandleTelemetryDataAsync(It.IsAny<AllergenTelemetryData>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
        }

        [Fact]
        public async Task ProcessNewFilesAsync_ProcessesXmlFile()
        {
            // Arrange
            var service = new FileProcessingService(_loggerMock.Object, _optionsMock.Object, _telemetryHandlerMock.Object);
            var xmlFile = CreateTestFile("test.xml", "<AllergenTelemetryData><AllergenName>Test</AllergenName></AllergenTelemetryData>");

            // Act
            await service.ProcessNewFilesAsync();

            // Assert
            _telemetryHandlerMock.Verify(h => h.HandleTelemetryDataAsync(It.Is<AllergenTelemetryData>(d => d.AllergenName == "Test"), It.IsAny<CancellationToken>()), Times.Once);
            VerifyLoggerCalls(LogLevel.Information, "Processed file", Times.Once());
        }

        [Fact]
        public async Task ProcessNewFilesAsync_ProcessesJsonFile()
        {
            // Arrange
            var service = new FileProcessingService(_loggerMock.Object, _optionsMock.Object, _telemetryHandlerMock.Object);
            var jsonFile = CreateTestFile("test.json", "{\"AllergenName\": \"Test\"}");

            // Act
            await service.ProcessNewFilesAsync();

            // Assert
            _telemetryHandlerMock.Verify(h => h.HandleTelemetryDataAsync(It.Is<AllergenTelemetryData>(d => d.AllergenName == "Test"), It.IsAny<CancellationToken>()), Times.Once);
            VerifyLoggerCalls(LogLevel.Information, "Processed file", Times.Once());
        }

        [Fact]
        public async Task ProcessNewFilesAsync_HandlesInvalidFileFormat()
        {
            // Arrange
            var service = new FileProcessingService(_loggerMock.Object, _optionsMock.Object, _telemetryHandlerMock.Object);
            var invalidFile = CreateTestFile("test.txt", "This is not a valid XML or JSON file");

            // Act
            await service.ProcessNewFilesAsync();

            // Assert
            _telemetryHandlerMock.Verify(h => h.HandleTelemetryDataAsync(It.IsAny<AllergenTelemetryData>(), It.IsAny<CancellationToken>()), Times.Never);
            VerifyLoggerCalls(LogLevel.Error, "Error processing file", Times.Once());
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
            Assert.All(expectedFiles, file => Assert.Contains(file, processedFiles));
        }
    }
}
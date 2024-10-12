using Microsoft.Extensions.Logging;
using Moq;
using PhadiaBackgroundService.Abstracts;
using PhadiaBackgroundService.Domain;
using PhadiaBackgroundService.Model;

namespace PhadiaBackgroundService.Tests
{
    public class PhadiaTelemetryHandlerTests
    {
        private readonly Mock<ILogger<PhadiaTelemetryHandler>> _loggerMock;
        private readonly Mock<ITelemetryServiceClient> _eventHubClientMock;
        private readonly Mock<IGrpcClient> _grpcClientMock;

        public PhadiaTelemetryHandlerTests()
        {
            _loggerMock = new Mock<ILogger<PhadiaTelemetryHandler>>();
            _eventHubClientMock = new Mock<ITelemetryServiceClient>();
            _grpcClientMock = new Mock<IGrpcClient>();
        }

        [Fact]
        public async Task HandleTelemetryDataAsync_SendsDataToEventHub()
        {
            // Arrange
            var handler = CreateHandler();
            var telemetryData = new AllergenTelemetryData { AllergenName = "Test Allergen" };

            _eventHubClientMock.Setup(c => c.SendTelemetryAsync(It.IsAny<TelemetryRequest>()))
                .ReturnsAsync(new TelemetryResponse { Success = true });

            // Act
            await handler.HandleTelemetryDataAsync(telemetryData, CancellationToken.None);

            // Assert
            _eventHubClientMock.Verify(c => c.SendTelemetryAsync(It.Is<TelemetryRequest>(r => r.Data.AllergenName == "Test Allergen")), Times.AtLeastOnce());
            VerifyLoggerCalls(LogLevel.Information, "Sent telemetry data to Event Hub", () => Times.AtLeastOnce());
        }

        [Fact]
        public async Task HandleTelemetryDataAsync_RetriesOnEventHubFailure()
        {
            // Arrange
            var handler = CreateHandler();
            var telemetryData = new AllergenTelemetryData { AllergenName = "Test Allergen" };

            _eventHubClientMock.SetupSequence(c => c.SendTelemetryAsync(It.IsAny<TelemetryRequest>()))
                .ThrowsAsync(new Exception("Simulated failure"))
                .ReturnsAsync(new TelemetryResponse { Success = true });

            // Act
            await handler.HandleTelemetryDataAsync(telemetryData, CancellationToken.None);

            // Assert
            _eventHubClientMock.Verify(c => c.SendTelemetryAsync(It.IsAny<TelemetryRequest>()), Times.Exactly(2));
            VerifyLoggerCalls(LogLevel.Information, "Pre-processing telemetry data from file", () => Times.Once());
            VerifyLoggerCalls(LogLevel.Information, "Sent telemetry data to Event Hub with response: True", () => Times.Once());
        }

        [Fact]
        public async Task HandleTelemetryDataAsync_LogsErrorOnFailure()
        {
            // Arrange
            var handler = CreateHandler();
            var telemetryData = new AllergenTelemetryData { AllergenName = "Test Allergen" };

            _eventHubClientMock.Setup(c => c.SendTelemetryAsync(It.IsAny<TelemetryRequest>()))
                .ThrowsAsync(new Exception("Simulated EventHub failure"));

            // Act
            await handler.HandleTelemetryDataAsync(telemetryData, CancellationToken.None);

            // Assert
            _eventHubClientMock.Verify(c => c.SendTelemetryAsync(It.IsAny<TelemetryRequest>()), Times.AtLeastOnce());
            VerifyLoggerCalls(LogLevel.Error, "Error while handling telemetry data", () => Times.AtLeastOnce());
        }

        private PhadiaTelemetryHandler CreateHandler()
        {
            return new PhadiaTelemetryHandler(
                _loggerMock.Object,
                _eventHubClientMock.Object,
                _grpcClientMock.Object);
        }
        private void VerifyLoggerCalls(LogLevel logLevel, string messageContains, Func<Times> times)
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
    }
}
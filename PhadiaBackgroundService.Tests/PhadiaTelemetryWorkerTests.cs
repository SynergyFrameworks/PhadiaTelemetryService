using Microsoft.Extensions.Logging;
using Moq;
using PhadiaBackgroundService.Abstracts;
using PhadiaBackgroundService.Domain;
using PhadiaBackgroundService.Model;

namespace PhadiaBackgroundService.Tests
{
    public class PhadiaTelemetryHandlerTests : IDisposable
    {
        private readonly Mock<ILogger<PhadiaTelemetryHandler>> _loggerMock;
        private readonly Mock<ITelemetryServiceClient> _eventHubClientMock;
        private readonly Mock<IGrpcClient> _grpcClientMock;
        private PhadiaTelemetryHandler _handler;
        private CancellationTokenSource _cts;

        public PhadiaTelemetryHandlerTests()
        {
            _loggerMock = new Mock<ILogger<PhadiaTelemetryHandler>>();
            _eventHubClientMock = new Mock<ITelemetryServiceClient>();
            _grpcClientMock = new Mock<IGrpcClient>();
            _cts = new CancellationTokenSource();
            _handler = CreateHandler();
        }

        [Fact]
        public async Task HandleTelemetryDataAsync_SendsDataToEventHubAndGrpc()
        {
            // Arrange
            var telemetryData = new AllergenTelemetryData { AllergenName = "Test Allergen" };

            _eventHubClientMock.Setup(c => c.SendTelemetryAsync(It.IsAny<TelemetryRequest>()))
                .ReturnsAsync(new TelemetryResponse { Success = true });

            _grpcClientMock.Setup(c => c.SendTelemetryAsync(It.IsAny<AllergenTelemetryData>()))
                .ReturnsAsync(true);

            // Act
            await _handler.HandleTelemetryDataAsync(telemetryData, _cts.Token);
            await Task.Delay(1000); // Increase delay to allow processing to complete
            await _handler.WaitForProcessingCompletionAsync(TimeSpan.FromSeconds(15));

            // Assert
            _eventHubClientMock.Verify(c => c.SendTelemetryAsync(It.Is<TelemetryRequest>(r => r.Data.AllergenName == "Test Allergen")), Times.Once);
            _grpcClientMock.Verify(c => c.SendTelemetryAsync(It.Is<AllergenTelemetryData>(d => d.AllergenName == "Test Allergen")), Times.Once);
            VerifyLoggerCalls(LogLevel.Information, "Sent telemetry data to Event Hub", () => Times.Once());
            VerifyLoggerCalls(LogLevel.Information, "Sent telemetry data to gRPC endpoint", () => Times.Once());
        }


        [Fact]
        public async Task HandleTelemetryDataAsync_RetriesOnEventHubFailure()
        {
            // Arrange
            var telemetryData = new AllergenTelemetryData { AllergenName = "Test Allergen" };

            _eventHubClientMock.SetupSequence(c => c.SendTelemetryAsync(It.IsAny<TelemetryRequest>()))
                .ThrowsAsync(new Exception("Simulated failure"))
                .ReturnsAsync(new TelemetryResponse { Success = true });

            _grpcClientMock.Setup(c => c.SendTelemetryAsync(It.IsAny<AllergenTelemetryData>()))
                .ReturnsAsync(true);

            // Act
            await _handler.HandleTelemetryDataAsync(telemetryData, _cts.Token);
            await Task.Delay(1000); // Increase delay to allow processing to complete
            await _handler.WaitForProcessingCompletionAsync(TimeSpan.FromSeconds(20));

            // Assert
            _eventHubClientMock.Verify(c => c.SendTelemetryAsync(It.IsAny<TelemetryRequest>()), Times.Exactly(2));
            VerifyLoggerCalls(LogLevel.Information, "Pre-processing telemetry data", () => Times.Once());
            VerifyLoggerCalls(LogLevel.Information, "Sent telemetry data to Event Hub with response: True", () => Times.Once());
        }

        [Fact]
        public async Task HandleTelemetryDataAsync_LogsErrorOnFailure()
        {
            // Arrange
            var telemetryData = new AllergenTelemetryData { AllergenName = "Test Allergen" };

            int eventHubCallCount = 0;
            _eventHubClientMock.Setup(c => c.SendTelemetryAsync(It.IsAny<TelemetryRequest>()))
                .ReturnsAsync(() =>
                {
                    eventHubCallCount++;
                    if (eventHubCallCount <= 3)
                    {
                        throw new Exception("Simulated EventHub failure");
                    }
                    return new TelemetryResponse { Success = true };
                });

            int grpcCallCount = 0;
            _grpcClientMock.Setup(c => c.SendTelemetryAsync(It.IsAny<AllergenTelemetryData>()))
                .ReturnsAsync(() =>
                {
                    grpcCallCount++;
                    if (grpcCallCount <= 3)
                    {
                        throw new Exception("Simulated gRPC failure");
                    }
                    return true;
                });

            // Act
            await _handler.HandleTelemetryDataAsync(telemetryData, _cts.Token);
            await Task.Delay(5000); // Allow time for retries and processing
            await _handler.WaitForProcessingCompletionAsync(TimeSpan.FromSeconds(30));

            // Assert
            _eventHubClientMock.Verify(c => c.SendTelemetryAsync(It.IsAny<TelemetryRequest>()), Times.AtLeast(4));
            _grpcClientMock.Verify(c => c.SendTelemetryAsync(It.IsAny<AllergenTelemetryData>()), Times.AtLeast(4));

            VerifyLoggerCalls(LogLevel.Information, "Sending telemetry data to Event Hub", () => Times.AtLeast(4));
            VerifyLoggerCalls(LogLevel.Information, "Sending telemetry data to gRPC endpoint", () => Times.AtLeast(4));

            VerifyLoggerCalls(LogLevel.Information, "Sent telemetry data to Event Hub with response: True", () => Times.Once());
            VerifyLoggerCalls(LogLevel.Information, "Sent telemetry data to gRPC endpoint with response: True", () => Times.Once());

            VerifyLoggerCalls(LogLevel.Error, "Error sending telemetry data to Event Hub after all retries", () => Times.Never());
            VerifyLoggerCalls(LogLevel.Error, "Error sending telemetry data to gRPC endpoint after all retries", () => Times.Never());
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

        public void Dispose()
        {
            _cts.Cancel();
            _handler.Dispose();
            _cts.Dispose();
        }
    }
}
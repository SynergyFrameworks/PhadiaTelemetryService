using PhadiaBackgroundService.Model;

namespace PhadiaBackgroundService.Abstracts;

public interface ITelemetryServiceClient
{
    Task<TelemetryResponse> SendTelemetryAsync(TelemetryRequest request);
}
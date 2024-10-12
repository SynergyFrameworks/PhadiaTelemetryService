namespace PhadiaBackgroundService.Abstracts;

public interface IGrpcClient
{
    Task<bool> SendTelemetryAsync(AllergenTelemetryData data);
}
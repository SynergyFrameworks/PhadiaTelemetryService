namespace PhadiaBackgroundService.Abstracts;

public interface IPhadiaTelemetryHandler
{
    Task HandleTelemetryDataAsync(AllergenTelemetryData data, CancellationToken stoppingToken);
}
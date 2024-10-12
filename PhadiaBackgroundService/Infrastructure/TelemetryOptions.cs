namespace PhadiaBackgroundService.Infrastructure;
public class TelemetryOptions
{
    public string EventHubConnectionString { get; set; }
    public string EventHubName { get; set; }
    public string DirectoryPath { get; set; }
}

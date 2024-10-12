namespace PhadiaBackgroundService.Model
{
    public class TelemetryRequest
    {
        public AllergenTelemetryData Data { get; set; }
    }

    public class TelemetryResponse
    {
        public bool Success { get; set; }
    }
}

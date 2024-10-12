using PhadiaBackgroundService.Abstracts;
using Polly;
using Polly.CircuitBreaker;



namespace PhadiaBackgroundService.Infrastructure;


public class GrpcClient : IGrpcClient
{
    private readonly PhadiaGrpcService.PhadiaGrpcService.PhadiaGrpcServiceClient _client;
    private readonly ILogger<GrpcClient> _logger;
    private readonly AsyncCircuitBreakerPolicy _circuitBreaker;

    public GrpcClient(
        PhadiaGrpcService.PhadiaGrpcService.PhadiaGrpcServiceClient client,
        ILogger<GrpcClient> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _circuitBreaker = Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (ex, breakDuration) =>
                {
                    _logger.LogWarning(ex, "Circuit breaker opened for {BreakDuration}", breakDuration);
                },
                onReset: () =>
                {
                    _logger.LogInformation("Circuit breaker reset");
                });
    }

    public async Task<bool> SendTelemetryAsync(AllergenTelemetryData data)
    {
        try
        {
            return await _circuitBreaker.ExecuteAsync(async () =>
            {
                var request = new PhadiaGrpcService.TelemetryRequest
                {
                    Data = new PhadiaGrpcService.AllergenTelemetryData
                    {
                        AllergenName = data.AllergenName,
                        CommonAllergens = { data.CommonAllergens },
                        Region = data.Region,
                        SensitizationPercentage = data.SensitizationPercentage,
                        IsPrimaryAllergen = data.IsPrimaryAllergen,
                        ExposureRoute = data.ExposureRoute,
                        RiskOfAsthma = data.RiskOfAsthma,
                        CanInduceRhinitis = data.CanInduceRhinitis,
                        CanInduceConjunctivitis = data.CanInduceConjunctivitis,
                        RiskFactorForAsthma = data.RiskFactorForAsthma,
                        CanInduceAtopicDermatitis = data.CanInduceAtopicDermatitis,
                        IsUsedForAllergenImmunotherapy = data.IsUsedForAllergenImmunotherapy,
                        DiagnosticTests = { data.DiagnosticTests },
                        PediatricPrevalence = { data.PediatricPrevalence },
                        EnvironmentalDistribution = { data.EnvironmentalDistribution }
                    }
                };
                var response = await _client.SendTelemetryAsync(request);
                return response.Success;
            });
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Circuit breaker is open. Skipping operation.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending telemetry data via gRPC");
            return false;
        }
    }
}
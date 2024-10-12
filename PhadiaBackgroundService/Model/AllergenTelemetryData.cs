public record AllergenTelemetryData
{
    public string AllergenName { get; init; }
    public List<string> CommonAllergens { get; init; }
    public string Region { get; init; }
    public double SensitizationPercentage { get; init; }
    public bool IsPrimaryAllergen { get; init; }
    public string ExposureRoute { get; init; }
    public bool RiskOfAsthma { get; init; }
    public bool CanInduceRhinitis { get; init; }
    public bool CanInduceConjunctivitis { get; init; }
    public string RiskFactorForAsthma { get; init; }
    public bool CanInduceAtopicDermatitis { get; init; }
    public bool IsUsedForAllergenImmunotherapy { get; init; }
    public List<string> DiagnosticTests { get; init; }
    public List<string> PediatricPrevalence { get; init; }
    public List<string> EnvironmentalDistribution { get; init; }
}

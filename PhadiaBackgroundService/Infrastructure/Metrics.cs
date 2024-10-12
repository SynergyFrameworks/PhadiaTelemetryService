namespace PhadiaBackgroundService.Infrastructure;

public class Metrics
{
    private int _processedFiles;
    private int _failedFiles;
    private int _transmittedData;
    private int _failedTransmissions;

    public int ProcessedFiles => _processedFiles;
    public int FailedFiles => _failedFiles;
    public int TransmittedData => _transmittedData;
    public int FailedTransmissions => _failedTransmissions;

    public void IncrementProcessedFiles() => Interlocked.Increment(ref _processedFiles);
    public void IncrementFailedFiles() => Interlocked.Increment(ref _failedFiles);
    public void IncrementTransmittedData() => Interlocked.Increment(ref _transmittedData);
    public void IncrementFailedTransmissions() => Interlocked.Increment(ref _failedTransmissions);

    public override string ToString()
    {
        return $"ProcessedFiles: {ProcessedFiles}, FailedFiles: {FailedFiles}, TransmittedData: {TransmittedData}, FailedTransmissions: {FailedTransmissions}";
    }
}
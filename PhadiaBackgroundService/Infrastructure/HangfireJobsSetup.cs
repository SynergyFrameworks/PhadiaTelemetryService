using Hangfire;

namespace PhadiaBackgroundService.Infrastructure
{
    public class HangfireJobsSetup : IHostedService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<HangfireJobsSetup> _logger;

        public HangfireJobsSetup(IConfiguration configuration, ILogger<HangfireJobsSetup> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            int jobIntervalMinutes = _configuration.GetValue<int>("Hangfire:JobIntervalMinutes");

            if (jobIntervalMinutes <= 0)
            {
                _logger.LogWarning("Invalid job interval specified. Defaulting to 5 minutes.");
                jobIntervalMinutes = 5;
            }

            _logger.LogInformation("Scheduling Hangfire job to run every {Interval} minutes", jobIntervalMinutes);

            RecurringJob.AddOrUpdate<FileProcessingService>(
               "check-new-files",
               service => service.ProcessNewFilesAsync(),
               Cron.MinuteInterval(jobIntervalMinutes));

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.Extensions.Options;
using PhadiaBackgroundService.Abstracts;
using PhadiaBackgroundService.Domain;
using PhadiaBackgroundService.Infrastructure;
using Polly;
using Polly.Extensions.Http;
using Serilog;
using Serilog.Sinks.Elasticsearch;


namespace PhadiaBackgroundService;
public class Program
{
    public static void Main(string[] args)
    {
        // Configure Serilog
        var configuration = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddEnvironmentVariables()
        .Build();


        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(configuration["Elasticsearch:Uri"]))
            {
                AutoRegisterTemplate = true,
                IndexFormat = configuration["Elasticsearch:IndexFormat"]
            })
            .CreateLogger();

        try
        {
            Log.Information("Starting PhadiaTelemetry service...");
            CreateHostBuilder(args).Build().Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
       Host.CreateDefaultBuilder(args)
           .UseWindowsService()
           .UseSerilog()
           .ConfigureAppConfiguration((hostContext, configBuilder) =>
           {
               configBuilder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                            .AddEnvironmentVariables();
           })
           .ConfigureServices((hostContext, services) =>
           {
               var config = hostContext.Configuration;
               // Register configuration options
               services.Configure<TelemetryOptions>(config.GetSection("Telemetry"));
               services.Configure<GrpcOptions>(config.GetSection("gRPC"));

               // Register Event Hub TelemetryServiceClient
               services.AddSingleton<TelemetryServiceClient>(sp =>
               {
                   var logger = sp.GetRequiredService<ILogger<TelemetryServiceClient>>();
                   var telemetryOptions = sp.GetRequiredService<IOptions<TelemetryOptions>>().Value;
                   return new TelemetryServiceClient(telemetryOptions.EventHubConnectionString, telemetryOptions.EventHubName, logger);
               });

               // Register gRPC client
               services.AddGrpcClient<PhadiaGrpcService.PhadiaGrpcService.PhadiaGrpcServiceClient>((serviceProvider, options) =>
               {
                   var grpcOptions = serviceProvider.GetRequiredService<IOptions<GrpcOptions>>();
                   options.Address = new Uri(grpcOptions.Value.BaseURI);
               })
               .ConfigurePrimaryHttpMessageHandler(() =>
               {
                   return new SocketsHttpHandler
                   {
                       PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                       KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                       KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                       EnableMultipleHttp2Connections = true
                   };
               })
               .AddPolicyHandler(GetRetryPolicy());

               // Register IGrpcClient
               services.AddSingleton<IGrpcClient, GrpcClient>();

               // Configure Hangfire
               services.AddHangfire(configuration => configuration
                     .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
                     .UseSimpleAssemblyNameTypeSerializer()
                     .UseRecommendedSerializerSettings()
                     .UseMemoryStorage());

               services.AddHangfireServer();

               // Register HangfireJobsSetup
               services.AddHostedService<HangfireJobsSetup>();

               // Register PhadiaTelemetryHandler
               services.AddSingleton<IPhadiaTelemetryHandler, PhadiaTelemetryHandler>();

               // Register ITelemetryServiceClient
               // Register Event Hub TelemetryServiceClient
               services.AddSingleton<TelemetryServiceClient>(sp =>
               {
                   var logger = sp.GetRequiredService<ILogger<TelemetryServiceClient>>();
                   var telemetryOptions = sp.GetRequiredService<IOptions<TelemetryOptions>>().Value;
                   return new TelemetryServiceClient(telemetryOptions.EventHubConnectionString, telemetryOptions.EventHubName, logger);
               });

               // Register ITelemetryServiceClient
               services.AddSingleton<ITelemetryServiceClient>(sp =>
                   sp.GetRequiredService<TelemetryServiceClient>());

               // Add file processing service
               services.AddSingleton<FileProcessingService>();

           });

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.NotFound)
            .WaitAndRetryAsync(6, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }
}

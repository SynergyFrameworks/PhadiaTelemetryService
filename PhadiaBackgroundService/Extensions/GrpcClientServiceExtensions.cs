using PhadiaBackgroundService;
using PhadiaBackgroundService.Abstracts;
using PhadiaBackgroundService.Infrastructure;

namespace PhadiaBackgroundService.Extensions;
public static class GrpcClientServiceExtensions
{
    public static IServiceCollection AddGrpcClient(this IServiceCollection services, string grpcEndpoint)
    {
        services.AddGrpcClient<PhadiaGrpcService.PhadiaGrpcService.PhadiaGrpcServiceClient>(options =>
        {
            options.Address = new Uri(grpcEndpoint);
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
        });
        services.AddSingleton<IGrpcClient, GrpcClient>();
        return services;
    }
}
using Microsoft.Extensions.DependencyInjection;

namespace Zhengyan.DigitalWife.Realtime.OpenAI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAiRealtimeClient(
        this IServiceCollection services,
        OpenAiRealtimeClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<OpenAiRealtimeClient>();
        return services;
    }
}

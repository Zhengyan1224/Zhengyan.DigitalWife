using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zhengyan.DigitalWife.Audio;

namespace Zhengyan.DigitalWife.Audio.OpenAL;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAlAudioPlayer(this IServiceCollection services, OpenAlRuntimeOptions? options = null)
    {
        services.TryAddSingleton(options ?? new OpenAlRuntimeOptions());
        services.TryAddSingleton<OpenAlAudioPlayer>();
        services.TryAddSingleton<IAudioPlayer>(sp => sp.GetRequiredService<OpenAlAudioPlayer>());
        services.TryAddSingleton<IAudioPlaybackTiming>(sp => sp.GetRequiredService<OpenAlAudioPlayer>());
        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zhengyan.DigitalWife.Audio;

namespace Zhengyan.DigitalWife.Audio.PortAudio;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPortAudio(this IServiceCollection services, PortAudioRuntimeOptions? options = null)
    {
        AddPortAudioCore(services, options);
        AddPortAudioInput(services, options);
        AddPortAudioOutput(services, options);
        return services;
    }

    public static IServiceCollection AddPortAudioInput(this IServiceCollection services, PortAudioRuntimeOptions? options = null)
    {
        AddPortAudioCore(services, options);
        services.TryAddSingleton<PortAudioMicrophoneSource>();
        services.TryAddSingleton<IAudioSource>(sp => sp.GetRequiredService<PortAudioMicrophoneSource>());
        return services;
    }

    public static IServiceCollection AddPortAudioOutput(this IServiceCollection services, PortAudioRuntimeOptions? options = null)
    {
        AddPortAudioCore(services, options);
        services.TryAddSingleton<PortAudioSpeakerPlayer>();
        services.TryAddSingleton<IAudioPlayer>(sp => sp.GetRequiredService<PortAudioSpeakerPlayer>());
        services.TryAddSingleton<IAudioPlaybackTiming>(sp => sp.GetRequiredService<PortAudioSpeakerPlayer>());
        return services;
    }

    private static void AddPortAudioCore(IServiceCollection services, PortAudioRuntimeOptions? options)
    {
        services.TryAddSingleton(options ?? new PortAudioRuntimeOptions());
        services.TryAddSingleton<PortAudioDeviceCatalog>();
    }
}

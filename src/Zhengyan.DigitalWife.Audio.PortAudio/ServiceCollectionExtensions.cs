using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Audio;

namespace Zhengyan.DigitalWife.Audio.PortAudio;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPortAudio(this IServiceCollection services, PortAudioRuntimeOptions? options = null)
    {
        services.AddSingleton(options ?? new PortAudioRuntimeOptions());
        services.AddSingleton<PortAudioDeviceCatalog>();
        services.AddSingleton<PortAudioMicrophoneSource>();
        services.AddSingleton<PortAudioSpeakerPlayer>();
        services.AddSingleton<IAudioSource>(sp => sp.GetRequiredService<PortAudioMicrophoneSource>());
        services.AddSingleton<IAudioPlayer>(sp => sp.GetRequiredService<PortAudioSpeakerPlayer>());
        return services;
    }
}


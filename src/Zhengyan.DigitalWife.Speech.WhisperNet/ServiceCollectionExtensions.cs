using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Speech;

namespace Zhengyan.DigitalWife.Speech.WhisperNet;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWhisperNetSpeechRecognizer(
        this IServiceCollection services,
        WhisperNetRecognizerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<WhisperNetSpeechRecognizer>();
        services.AddSingleton<ISpeechRecognizer>(sp => sp.GetRequiredService<WhisperNetSpeechRecognizer>());
        return services;
    }
}


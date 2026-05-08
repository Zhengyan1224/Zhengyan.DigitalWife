using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Speech;

namespace Zhengyan.DigitalWife.Speech.SherpaOnnx;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSherpaOnnxSpeechRecognizer(
        this IServiceCollection services,
        SherpaOnnxRecognizerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<SherpaOnnxSpeechRecognizer>();
        services.AddSingleton<ISpeechRecognizer>(sp => sp.GetRequiredService<SherpaOnnxSpeechRecognizer>());
        return services;
    }

    public static IServiceCollection AddSherpaOnnxTextToSpeech(
        this IServiceCollection services,
        SherpaOnnxTtsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<SherpaOnnxTextToSpeechSynthesizer>();
        services.AddSingleton<ITextToSpeechSynthesizer>(sp => sp.GetRequiredService<SherpaOnnxTextToSpeechSynthesizer>());
        return services;
    }

    public static IServiceCollection AddSherpaOnnxWakeWordDetector(
        this IServiceCollection services,
        SherpaOnnxWakeWordOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<SherpaOnnxWakeWordDetector>();
        services.AddSingleton<IWakeWordDetector>(sp => sp.GetRequiredService<SherpaOnnxWakeWordDetector>());
        return services;
    }
}


using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Llm;

namespace Zhengyan.DigitalWife.Llm.OpenAI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAiCompatibleLlmClient(
        this IServiceCollection services,
        OpenAiCompatibleLlmOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<OpenAiCompatibleLlmClient>();
        services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<OpenAiCompatibleLlmClient>());
        return services;
    }
}


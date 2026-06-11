using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Assistant.Conversation;
using Zhengyan.DigitalWife.Assistant.Text;

namespace Zhengyan.DigitalWife.Assistant;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDigitalWifeAssistantCore(this IServiceCollection services)
    {
        services.AddSingleton<SentenceChunker>();
        services.AddTransient<VoiceAssistantPipeline>();
        return services;
    }
}


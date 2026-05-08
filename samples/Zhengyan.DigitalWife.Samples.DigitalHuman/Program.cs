using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zhengyan.DigitalWife.Assistant;
using Zhengyan.DigitalWife.Assistant.Text;
using Zhengyan.DigitalWife.Audio.PortAudio;
using Zhengyan.DigitalWife.Llm.OpenAI;
using Zhengyan.DigitalWife.Samples.DigitalHuman;
using Zhengyan.DigitalWife.Speech.SherpaOnnx;
using Zhengyan.DigitalWife.Speech.WhisperNet;

string appBasePath = AppContext.BaseDirectory;

IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(appBasePath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .Build();

DigitalHumanAppOptions options = configuration.GetSection("DigitalHuman").Get<DigitalHumanAppOptions>()
    ?? throw new InvalidOperationException("Missing DigitalHuman configuration.");

SamplePathResolver pathResolver = new(appBasePath);
ResolvedDigitalHumanOptions resolvedOptions = DigitalHumanOptionsResolver.Resolve(options, pathResolver);

ServiceCollection services = new();
services.AddLogging(builder =>
{
    builder.ClearProviders();
    builder.AddSimpleConsole(logging =>
    {
        logging.TimestampFormat = "HH:mm:ss ";
        logging.SingleLine = true;
    });
});

services.AddDigitalWifeAssistantCore();
services.AddSingleton(new SentenceChunker(new SentenceChunkerOptions
{
    EnableClauseBoundaries = resolvedOptions.Conversation.ResponseChunking.EnableClauseBoundaries,
    MinClauseCharacters = resolvedOptions.Conversation.ResponseChunking.MinClauseCharacters,
    MaxBufferedCharacters = resolvedOptions.Conversation.ResponseChunking.MaxBufferedCharacters
}));
services.AddPortAudio(resolvedOptions.Audio);
services.AddOpenAiCompatibleLlmClient(resolvedOptions.Llm);
services.AddSherpaOnnxTextToSpeech(resolvedOptions.Tts);

IReadOnlyList<string> recognitionPriority = resolvedOptions.RecognitionPriority.Count > 0
    ? resolvedOptions.RecognitionPriority
    : [resolvedOptions.RecognitionProvider];

foreach (string providerName in recognitionPriority)
{
    switch (providerName.ToLowerInvariant())
    {
        case "sherpa":
            if (resolvedOptions.SherpaRecognizer is null)
            {
                throw new InvalidOperationException("Sherpa recognizer configuration is required when RecognitionPriority contains 'sherpa'.");
            }

            services.AddSherpaOnnxSpeechRecognizer(resolvedOptions.SherpaRecognizer);
            break;

        case "whisper":
            if (resolvedOptions.WhisperRecognizer is null)
            {
                throw new InvalidOperationException("Whisper recognizer configuration is required when RecognitionPriority contains 'whisper'.");
            }

            services.AddWhisperNetSpeechRecognizer(resolvedOptions.WhisperRecognizer);
            break;

        default:
            throw new InvalidOperationException($"Unsupported recognition provider: {providerName}");
    }
}

using ServiceProvider provider = services.BuildServiceProvider();
ILogger<DigitalHumanGame> logger = provider.GetRequiredService<ILogger<DigitalHumanGame>>();

using DigitalHumanGame game = new(resolvedOptions, provider, logger);
game.Run();

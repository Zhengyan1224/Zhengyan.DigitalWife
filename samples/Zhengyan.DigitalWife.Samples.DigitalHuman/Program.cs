using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Audio.PortAudio;
using Zhengyan.DigitalWife.Realtime.OpenAI;
using Zhengyan.DigitalWife.Samples.DigitalHuman;

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

services.AddPortAudioInput(resolvedOptions.Audio.PortAudio);
switch (resolvedOptions.Audio.PlaybackBackend)
{
    case AudioPlaybackBackend.PortAudio:
        services.AddPortAudioOutput(resolvedOptions.Audio.PortAudio);
        break;

    case AudioPlaybackBackend.OpenAL:
        break;

    default:
        throw new InvalidOperationException($"Unsupported audio playback backend: {resolvedOptions.Audio.PlaybackBackend}");
}
services.AddOpenAiRealtimeClient(resolvedOptions.RealtimeClient);

using ServiceProvider provider = services.BuildServiceProvider();
ILogger<DigitalHumanGame> logger = provider.GetRequiredService<ILogger<DigitalHumanGame>>();

using DigitalHumanGame game = new(resolvedOptions, provider, logger);
game.Run();

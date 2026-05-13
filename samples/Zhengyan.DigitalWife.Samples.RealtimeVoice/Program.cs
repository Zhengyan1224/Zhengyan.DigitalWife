using System.Net.WebSockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zhengyan.DigitalWife.Assistant.Text;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Llm.OpenAI;
using Zhengyan.DigitalWife.Realtime.OpenAI;
using Zhengyan.DigitalWife.Samples.RealtimeVoice;
using Zhengyan.DigitalWife.Speech.SherpaOnnx;
using Zhengyan.DigitalWife.Speech.WhisperNet;

string appBasePath = AppContext.BaseDirectory;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Configuration
    .SetBasePath(appBasePath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(logging =>
{
    logging.TimestampFormat = "HH:mm:ss ";
    logging.SingleLine = true;
});

RealtimeVoiceAppOptions options = builder.Configuration.GetSection("RealtimeVoice").Get<RealtimeVoiceAppOptions>()
    ?? throw new InvalidOperationException("Missing RealtimeVoice configuration.");

SamplePathResolver pathResolver = new(appBasePath);
ResolvedRealtimeVoiceOptions resolvedOptions = RealtimeVoiceOptionsResolver.Resolve(options, pathResolver);

builder.Services.AddSingleton(resolvedOptions);
builder.Services.AddSingleton(new SentenceChunker(resolvedOptions.ResponseChunking));
builder.Services.AddOpenAiCompatibleLlmClient(resolvedOptions.Llm);
builder.Services.AddSherpaOnnxTextToSpeech(resolvedOptions.Tts);

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

            builder.Services.AddSherpaOnnxSpeechRecognizer(resolvedOptions.SherpaRecognizer);
            break;

        case "whisper":
            if (resolvedOptions.WhisperRecognizer is null)
            {
                throw new InvalidOperationException("Whisper recognizer configuration is required when RecognitionPriority contains 'whisper'.");
            }

            builder.Services.AddWhisperNetSpeechRecognizer(resolvedOptions.WhisperRecognizer);
            break;

        default:
            throw new InvalidOperationException($"Unsupported recognition provider: {providerName}");
    }
}

builder.Services.AddSingleton<RealtimeVoiceBackend>();

WebApplication app = builder.Build();

using (IServiceScope startupScope = app.Services.CreateScope())
{
    ILogger startupLogger = startupScope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("SpeechRuntimeDiagnostics");
    SpeechRuntimeDiagnosticsLogger.Log(startupScope.ServiceProvider, startupLogger);

    RealtimeVoiceBackend warmupBackend = startupScope.ServiceProvider.GetRequiredService<RealtimeVoiceBackend>();
    await warmupBackend.WarmUpAsync(CancellationToken.None);
}

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(15)
});

app.MapGet("/", () => Results.Ok(new
{
    service = "Zhengyan.DigitalWife.Samples.RealtimeVoice",
    websocket = "/v1/realtime",
    model = resolvedOptions.Session.Model
}));

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    model = resolvedOptions.Session.Model
}));

app.MapPost("/v1/audio/speech", async (
    HttpContext context,
    RealtimeVoiceBackend backend,
    CancellationToken cancellationToken) =>
{
    if (!IsAuthorized(context, resolvedOptions.ApiKey))
    {
        return Results.Unauthorized();
    }

    OpenAiAudioSpeechRequest? request = await context.Request.ReadFromJsonAsync<OpenAiAudioSpeechRequest>(cancellationToken);
    if (request is null || string.IsNullOrWhiteSpace(request.Input))
    {
        return Results.BadRequest(new
        {
            error = "Request body must include a non-empty input field."
        });
    }

    string responseFormat = string.IsNullOrWhiteSpace(request.ResponseFormat)
        ? "wav"
        : request.ResponseFormat.Trim().ToLowerInvariant();

    if (responseFormat is not "wav" and not "pcm")
    {
        return Results.BadRequest(new
        {
            error = $"Unsupported response_format '{request.ResponseFormat}'. Supported values: wav, pcm."
        });
    }

    AudioData audio = await backend.SynthesizeAsync(request.Input, request.Voice, cancellationToken);

    if (request.Speed is float speed && speed > 0f && Math.Abs(speed - 1.0f) > 0.0001f)
    {
        audio = await backend.SynthesizeAsync(
            request.Input,
            request.Voice,
            speed,
            cancellationToken);
    }

    if (responseFormat == "pcm")
    {
        AudioData prepared = OpenAiRealtimeProtocol.PrepareAudio(audio, resolvedOptions.Session.Audio.Output.Format);
        byte[] rawPcm = Convert.FromBase64String(OpenAiRealtimeProtocol.EncodePcm16(prepared, resolvedOptions.Session.Audio.Output.Format));
        return Results.File(rawPcm, "application/octet-stream");
    }

    await using MemoryStream stream = new();
    await WaveFile.WriteAsync(stream, audio, cancellationToken: cancellationToken);
    return Results.File(stream.ToArray(), "audio/wav");
});

app.Map("/v1/realtime", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("A WebSocket request is required.", context.RequestAborted);
        return;
    }

    if (!IsAuthorized(context, resolvedOptions.ApiKey))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.Append("WWW-Authenticate", "Bearer");
        return;
    }

    using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
    RealtimeVoiceBackend backend = context.RequestServices.GetRequiredService<RealtimeVoiceBackend>();
    ILogger<RealtimeVoiceSession> logger = context.RequestServices.GetRequiredService<ILogger<RealtimeVoiceSession>>();

    string? requestedModel = context.Request.Query["model"];
    OpenAiRealtimeSession session = RealtimeVoiceOptionsResolver.CloneSession(
        resolvedOptions.Session,
        string.IsNullOrWhiteSpace(requestedModel) ? null : requestedModel);

    RealtimeVoiceSession realtimeSession = new(socket, backend, resolvedOptions, session, logger);
    await realtimeSession.RunAsync(context.RequestAborted);
});

app.Run();

static bool IsAuthorized(HttpContext context, string? expectedApiKey)
{
    if (string.IsNullOrWhiteSpace(expectedApiKey))
    {
        return true;
    }

    string? authorization = context.Request.Headers.Authorization;
    if (string.IsNullOrWhiteSpace(authorization)
        || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    string provided = authorization["Bearer ".Length..].Trim();
    return string.Equals(provided, expectedApiKey, StringComparison.Ordinal);
}

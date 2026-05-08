using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Llm;
using Zhengyan.DigitalWife.Speech;
using Zhengyan.DigitalWife.Assistant.Text;

namespace Zhengyan.DigitalWife.Assistant.Conversation;

public sealed class VoiceAssistantPipeline
{
    private readonly IAudioSource _audioSource;
    private readonly IReadOnlyList<ISpeechRecognizer> _speechRecognizers;
    private readonly ILlmClient _llmClient;
    private readonly ITextToSpeechSynthesizer _tts;
    private readonly IAudioPlayer _audioPlayer;
    private readonly SentenceChunker _sentenceChunker;
    private readonly ILogger<VoiceAssistantPipeline> _logger;

    public VoiceAssistantPipeline(
        IAudioSource audioSource,
        IEnumerable<ISpeechRecognizer> speechRecognizers,
        ILlmClient llmClient,
        ITextToSpeechSynthesizer tts,
        IAudioPlayer audioPlayer,
        SentenceChunker sentenceChunker,
        ILogger<VoiceAssistantPipeline> logger)
    {
        _audioSource = audioSource;
        _speechRecognizers = speechRecognizers.ToArray();
        _llmClient = llmClient;
        _tts = tts;
        _audioPlayer = audioPlayer;
        _sentenceChunker = sentenceChunker;
        _logger = logger;
    }

    public async Task<VoiceAssistantTurnResult> RunTurnAsync(
        VoiceAssistantTurnOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        _logger.LogInformation("Recording user utterance.");
        var capturedAudio = await _audioSource.RecordUntilSilenceAsync(options.CaptureOptions, cancellationToken);

        if (!string.IsNullOrWhiteSpace(options.CapturedAudioPath))
        {
            await WaveFile.WriteAsync(options.CapturedAudioPath, capturedAudio, cancellationToken: cancellationToken);
        }

        var recognition = await RecognizeWithFallbackAsync(capturedAudio, options.RecognitionOptions, cancellationToken);

        if (string.IsNullOrWhiteSpace(recognition.Text))
        {
            _logger.LogWarning("Speech recognition returned empty text.");
            return new VoiceAssistantTurnResult
            {
                UserText = string.Empty,
                AssistantText = string.Empty,
                SpokenSentences = []
            };
        }

        var messages = new List<LlmChatMessage>();
        if (!string.IsNullOrWhiteSpace(options.SystemPrompt))
        {
            messages.Add(new LlmChatMessage("system", options.SystemPrompt));
        }

        messages.AddRange(options.History);
        messages.Add(new LlmChatMessage("user", recognition.Text));

        _logger.LogInformation("Sending prompt to LLM model {Model}.", options.LlmOptions.Model);

        var llmUpdates = _llmClient.StreamChatAsync(messages, options.LlmOptions, cancellationToken);
        var deltaChannel = Channel.CreateUnbounded<string>();
        var assistantText = new StringBuilder();
        var spokenSentences = new ConcurrentQueue<string>();

        var producer = Task.Run(async () =>
        {
            await foreach (var update in llmUpdates.WithCancellation(cancellationToken))
            {
                if (string.IsNullOrEmpty(update.Delta))
                {
                    continue;
                }

                assistantText.Append(update.Delta);
                await deltaChannel.Writer.WriteAsync(update.Delta, cancellationToken);
            }

            deltaChannel.Writer.TryComplete();
        }, cancellationToken);

        var speaker = Task.Run(async () =>
        {
            async IAsyncEnumerable<string> EnumerateTokens([EnumeratorCancellation] CancellationToken ct)
            {
                await foreach (var delta in deltaChannel.Reader.ReadAllAsync(ct))
                {
                    yield return delta;
                }
            }

            await foreach (var sentence in _sentenceChunker.ChunkAsync(EnumerateTokens(cancellationToken), cancellationToken: cancellationToken))
            {
                spokenSentences.Enqueue(sentence);
                _logger.LogDebug("Synthesizing sentence: {Sentence}", sentence);
                var audio = await _tts.SynthesizeAsync(sentence, options.SynthesisOptions, cancellationToken);
                await _audioPlayer.PlayAsync(audio, cancellationToken);
            }
        }, cancellationToken);

        await Task.WhenAll(producer, speaker);

        var responseText = assistantText.ToString();

        return new VoiceAssistantTurnResult
        {
            UserText = recognition.Text,
            AssistantText = responseText,
            SpokenSentences = spokenSentences.ToArray()
        };
    }

    private async Task<SpeechRecognitionResult> RecognizeWithFallbackAsync(
        AudioData capturedAudio,
        SpeechRecognitionOptions options,
        CancellationToken cancellationToken)
    {
        if (_speechRecognizers.Count == 0)
        {
            throw new InvalidOperationException("No speech recognizers are registered.");
        }

        SpeechRecognitionResult? last = null;

        foreach (var recognizer in _speechRecognizers)
        {
            _logger.LogInformation("Running speech recognition with provider {Provider}.", recognizer.Name);
            last = await recognizer.RecognizeAsync(capturedAudio, options, cancellationToken);
            if (!string.IsNullOrWhiteSpace(last.Text))
            {
                return last;
            }

            _logger.LogWarning("Speech recognizer {Provider} returned empty text; trying next provider if available.", recognizer.Name);
        }

        return last ?? new SpeechRecognitionResult { Text = string.Empty };
    }
}

public sealed class VoiceAssistantTurnOptions
{
    public string? SystemPrompt { get; init; }

    public IReadOnlyList<LlmChatMessage> History { get; init; } = [];

    public required LlmRequestOptions LlmOptions { get; init; }

    public VoiceActivityCaptureOptions CaptureOptions { get; init; } = new();

    public SpeechRecognitionOptions RecognitionOptions { get; init; } = new();

    public SpeechSynthesisOptions SynthesisOptions { get; init; } = new();

    public string? CapturedAudioPath { get; init; }
}

public sealed class VoiceAssistantTurnResult
{
    public required string UserText { get; init; }

    public required string AssistantText { get; init; }

    public IReadOnlyList<string> SpokenSentences { get; init; } = [];
}


using Microsoft.Extensions.Logging;
using Zhengyan.DigitalWife.Assistant.Text;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Llm;
using Zhengyan.DigitalWife.Realtime.OpenAI;
using Zhengyan.DigitalWife.Speech;

namespace Zhengyan.DigitalWife.Samples.RealtimeVoice;

internal sealed class RealtimeVoiceBackend
{
    private readonly ResolvedRealtimeVoiceOptions _options;
    private readonly IReadOnlyList<ISpeechRecognizer> _speechRecognizers;
    private readonly ILlmClient _llmClient;
    private readonly ITextToSpeechSynthesizer _tts;
    private readonly SentenceChunker _sentenceChunker;
    private readonly ILogger<RealtimeVoiceBackend> _logger;
    private readonly SemaphoreSlim _recognitionGate = new(1, 1);
    private readonly SemaphoreSlim _ttsGate = new(1, 1);

    public RealtimeVoiceBackend(
        ResolvedRealtimeVoiceOptions options,
        IEnumerable<ISpeechRecognizer> speechRecognizers,
        ILlmClient llmClient,
        ITextToSpeechSynthesizer tts,
        SentenceChunker sentenceChunker,
        ILogger<RealtimeVoiceBackend> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _speechRecognizers = speechRecognizers?.ToArray() ?? throw new ArgumentNullException(nameof(speechRecognizers));
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _tts = tts ?? throw new ArgumentNullException(nameof(tts));
        _sentenceChunker = sentenceChunker ?? throw new ArgumentNullException(nameof(sentenceChunker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SentenceChunker SentenceChunker => _sentenceChunker;

    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        if (_speechRecognizers.Count == 0)
        {
            throw new InvalidOperationException("No speech recognizers are registered.");
        }

        AudioData silence = CreateSilenceAudio();
        foreach (ISpeechRecognizer recognizer in _speechRecognizers)
        {
            _logger.LogInformation("Warming up ASR provider {Provider}.", recognizer.Name);

            await _recognitionGate.WaitAsync(cancellationToken);
            try
            {
                _ = await recognizer.RecognizeAsync(
                    silence,
                    new SpeechRecognitionOptions
                    {
                        Language = "zh",
                        EnableTimestamps = true
                    },
                    cancellationToken);
            }
            finally
            {
                _recognitionGate.Release();
            }
        }

        _logger.LogInformation("Warming up TTS provider {Provider}.", _tts.Name);
        await _ttsGate.WaitAsync(cancellationToken);
        try
        {
            _ = await _tts.SynthesizeAsync(
                "你好",
                new SpeechSynthesisOptions
                {
                    ModelKind = _options.Tts.ModelKind,
                    Speed = _options.Synthesis.Speed,
                    SpeakerId = _options.Synthesis.SpeakerId
                },
                cancellationToken);
        }
        finally
        {
            _ttsGate.Release();
        }
    }

    public async Task<SpeechRecognitionResult> RecognizeAsync(
        AudioData audio,
        OpenAiRealtimeInputAudioTranscription? transcription,
        CancellationToken cancellationToken)
    {
        if (_speechRecognizers.Count == 0)
        {
            throw new InvalidOperationException("No speech recognizers are registered.");
        }

        SpeechRecognitionResult? last = null;
        int maxAttempts = _options.UseFallbackRecognizersForTranscription
            ? _speechRecognizers.Count
            : 1;

        for (int i = 0; i < maxAttempts; i++)
        {
            ISpeechRecognizer recognizer = _speechRecognizers[i];

            await _recognitionGate.WaitAsync(cancellationToken);
            try
            {
                _logger.LogInformation("Transcribing audio with provider {Provider}.", recognizer.Name);
                last = await recognizer.RecognizeAsync(
                    audio,
                    new SpeechRecognitionOptions
                    {
                        Language = transcription?.Language,
                        EnableTimestamps = true
                    },
                    cancellationToken);
            }
            finally
            {
                _recognitionGate.Release();
            }

            if (!string.IsNullOrWhiteSpace(last.Text))
            {
                return last;
            }
        }

        return last ?? new SpeechRecognitionResult
        {
            Text = string.Empty
        };
    }

    public IAsyncEnumerable<LlmStreamUpdate> StreamChatAsync(
        IReadOnlyList<LlmChatMessage> messages,
        CancellationToken cancellationToken)
    {
        return _llmClient.StreamChatAsync(
            messages,
            new LlmRequestOptions
            {
                Model = !string.IsNullOrWhiteSpace(_options.Llm.Model)
                    ? _options.Llm.Model
                    : throw new InvalidOperationException("RealtimeVoice.Llm.Model is required.")
            },
            cancellationToken);
    }

    public async Task<AudioData> SynthesizeAsync(string text, string? requestedVoice, CancellationToken cancellationToken)
        => await SynthesizeAsync(text, requestedVoice, speedOverride: null, cancellationToken);

    public async Task<AudioData> SynthesizeAsync(string text, string? requestedVoice, float? speedOverride, CancellationToken cancellationToken)
    {
        await _ttsGate.WaitAsync(cancellationToken);
        try
        {
            return await _tts.SynthesizeAsync(
                text,
                new SpeechSynthesisOptions
                {
                    ModelKind = _options.Tts.ModelKind,
                    Speed = speedOverride ?? _options.Synthesis.Speed,
                    SpeakerId = ResolveSpeakerId(requestedVoice)
                },
                cancellationToken);
        }
        finally
        {
            _ttsGate.Release();
        }
    }

    private int ResolveSpeakerId(string? requestedVoice)
    {
        if (int.TryParse(requestedVoice, out int speakerId))
        {
            return speakerId;
        }

        return _options.Synthesis.SpeakerId;
    }

    private AudioData CreateSilenceAudio()
    {
        int sampleRate = _options.SherpaRecognizer?.SampleRate
            ?? _options.WhisperRecognizer?.SampleRate
            ?? 16_000;
        int sampleCount = Math.Max(1, sampleRate / 5);
        return new AudioData(new float[sampleCount], new AudioFormat(sampleRate, 1));
    }
}

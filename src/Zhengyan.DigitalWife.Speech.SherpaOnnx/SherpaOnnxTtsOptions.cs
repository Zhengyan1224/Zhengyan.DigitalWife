using Zhengyan.DigitalWife.Speech;

namespace Zhengyan.DigitalWife.Speech.SherpaOnnx;

public sealed class SherpaOnnxTtsOptions
{
    public required string ModelPath { get; init; }

    public required string TokensPath { get; init; }

    public SpeechSynthesisModelKind ModelKind { get; init; } = SpeechSynthesisModelKind.Vits;

    public string? LexiconPath { get; init; }

    public string? DataDirectory { get; init; }

    public string? DictDirectory { get; init; }

    public string? VocoderPath { get; init; }

    public string? RuleFars { get; init; }

    public string? RuleFsts { get; init; }

    public string Provider { get; init; } = "cpu";

    public int Threads { get; init; } = Math.Max(1, Environment.ProcessorCount / 2);

    public float NoiseScale { get; init; } = 0.667f;

    public float NoiseScaleW { get; init; } = 0.8f;

    public float LengthScale { get; init; } = 1.0f;
}

namespace Zhengyan.DigitalWife.Speech.SherpaOnnx;

public enum SherpaOnnxRecognizerModelKind
{
    OfflineWhisper,
    OfflineParaformer,
    OfflineTransducer,
    OfflineZipformerCtc,
    OfflineWenetCtc,
    OnlineTransducer,
    OnlineParaformer,
    OnlineZipformer2Ctc
}

public sealed class SherpaOnnxRecognizerOptions
{
    public required SherpaOnnxRecognizerModelKind ModelKind { get; init; }

    public required string TokensPath { get; init; }

    public string? EncoderPath { get; init; }

    public string? DecoderPath { get; init; }

    public string? JoinerPath { get; init; }

    public string? ModelPath { get; init; }

    public string Language { get; init; } = "zh";

    public string Provider { get; init; } = "cpu";

    public int SampleRate { get; init; } = 16_000;

    public int FeatureDim { get; init; } = 80;

    public int Threads { get; init; } = Math.Max(1, Environment.ProcessorCount / 2);

    public string DecodingMethod { get; init; } = "greedy_search";

    public float HotwordsScore { get; init; } = 1.5f;

    public string? HotwordsFile { get; init; }
}


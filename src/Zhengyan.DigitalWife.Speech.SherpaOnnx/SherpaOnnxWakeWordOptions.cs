using Zhengyan.DigitalWife.Audio;

namespace Zhengyan.DigitalWife.Speech.SherpaOnnx;

public sealed class SherpaOnnxWakeWordOptions
{
    public required string TokensPath { get; init; }

    public required string EncoderPath { get; init; }

    public required string DecoderPath { get; init; }

    public required string JoinerPath { get; init; }

    public required string KeywordsFile { get; init; }

    public int SampleRate { get; init; } = 16_000;

    public int FeatureDim { get; init; } = 80;

    public int Threads { get; init; } = Math.Max(1, Environment.ProcessorCount / 2);

    public string Provider { get; init; } = "cpu";

    public float KeywordsThreshold { get; init; } = 0.35f;

    public float KeywordsScore { get; init; } = 1.0f;

    public int NumTrailingBlanks { get; init; } = 1;

    public AudioCaptureOptions CaptureOptions { get; init; } = new()
    {
        SampleRate = 16_000,
        Channels = 1,
        FramesPerBuffer = 512
    };
}


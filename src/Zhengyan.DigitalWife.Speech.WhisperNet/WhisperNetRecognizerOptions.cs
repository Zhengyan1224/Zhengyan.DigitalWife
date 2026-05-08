namespace Zhengyan.DigitalWife.Speech.WhisperNet;

public sealed class WhisperNetRecognizerOptions
{
    public required string ModelPath { get; init; }

    public string Language { get; init; } = "auto";

    public bool TranslateToEnglish { get; init; }

    public bool UseGpu { get; init; }

    public int Threads { get; init; } = Math.Max(1, Environment.ProcessorCount / 2);

    public int SampleRate { get; init; } = 16_000;
}


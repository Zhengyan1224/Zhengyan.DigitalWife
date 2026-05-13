namespace Zhengyan.DigitalWife.Speech.WhisperNet;

public sealed class WhisperNetRuntimeDiagnostics
{
    public required bool RequestedUseGpu { get; init; }

    public required string NativeSearchRoot { get; init; }

    public IReadOnlyList<string> FoundNativeFiles { get; init; } = [];

    public string? LoadedRuntimeLibrary { get; init; }

    public IReadOnlyList<string> RuntimeLibraryOrder { get; init; } = [];

    public string? InitializationError { get; init; }
}

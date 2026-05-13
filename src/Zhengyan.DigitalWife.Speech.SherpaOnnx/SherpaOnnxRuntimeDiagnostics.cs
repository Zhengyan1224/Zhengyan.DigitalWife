namespace Zhengyan.DigitalWife.Speech.SherpaOnnx;

public sealed class SherpaOnnxRuntimeDiagnostics
{
    public required string RequestedProvider { get; init; }

    public required SherpaOnnxRecognizerModelKind ModelKind { get; init; }

    public required string NativeSearchRoot { get; init; }

    public IReadOnlyList<string> FoundNativeFiles { get; init; } = [];

    public bool CudaProviderBinaryDetected { get; init; }

    public bool RequestedGpu =>
        !string.Equals(RequestedProvider, "cpu", StringComparison.OrdinalIgnoreCase);
}

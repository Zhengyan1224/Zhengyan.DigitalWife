using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zhengyan.DigitalWife.Speech;
using Zhengyan.DigitalWife.Speech.SherpaOnnx;
using Zhengyan.DigitalWife.Speech.WhisperNet;

namespace Zhengyan.DigitalWife.Samples.RealtimeVoice;

internal static class SpeechRuntimeDiagnosticsLogger
{
    public static void Log(IServiceProvider services, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        foreach (ISpeechRecognizer recognizer in services.GetServices<ISpeechRecognizer>())
        {
            switch (recognizer)
            {
                case SherpaOnnxSpeechRecognizer sherpa:
                    LogSherpaDiagnostics(sherpa.GetRuntimeDiagnostics(), logger);
                    break;

                case WhisperNetSpeechRecognizer whisper:
                    LogWhisperDiagnostics(whisper.GetRuntimeDiagnostics(), logger);
                    break;

                default:
                    logger.LogInformation("ASR runtime diagnostic: provider={Provider}.", recognizer.Name);
                    break;
            }
        }
    }

    private static void LogSherpaDiagnostics(SherpaOnnxRuntimeDiagnostics diagnostics, ILogger logger)
    {
        logger.LogInformation(
            "ASR runtime diagnostic: provider=SherpaOnnx:{ModelKind}, requestedProvider={RequestedProvider}, cudaProviderBinaryDetected={CudaDetected}, nativeFiles=[{NativeFiles}]",
            diagnostics.ModelKind,
            diagnostics.RequestedProvider,
            diagnostics.CudaProviderBinaryDetected,
            FormatList(diagnostics.FoundNativeFiles));

        if (diagnostics.RequestedGpu && !diagnostics.CudaProviderBinaryDetected)
        {
            logger.LogWarning(
                "SherpaOnnx was configured with provider '{RequestedProvider}', but no local ONNX Runtime CUDA provider binaries were detected under {SearchRoot}. Current deployment may still run on CPU unless GPU-specific native libraries are available on the process library search path.",
                diagnostics.RequestedProvider,
                diagnostics.NativeSearchRoot);
        }
    }

    private static void LogWhisperDiagnostics(WhisperNetRuntimeDiagnostics diagnostics, ILogger logger)
    {
        logger.LogInformation(
            "ASR runtime diagnostic: provider=Whisper.net, useGpu={UseGpu}, loadedRuntimeLibrary={LoadedRuntime}, runtimeOrder=[{RuntimeOrder}], nativeFiles=[{NativeFiles}]",
            diagnostics.RequestedUseGpu,
            diagnostics.LoadedRuntimeLibrary ?? "<unknown>",
            FormatList(diagnostics.RuntimeLibraryOrder),
            FormatList(diagnostics.FoundNativeFiles));

        if (!string.IsNullOrWhiteSpace(diagnostics.InitializationError))
        {
            logger.LogWarning(
                "Whisper.net runtime initialization reported: {Error}",
                diagnostics.InitializationError);
            return;
        }

        if (diagnostics.RequestedUseGpu
            && !string.Equals(diagnostics.LoadedRuntimeLibrary, "Cuda", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Whisper.net GPU was requested, but the loaded runtime library is '{LoadedRuntime}'. This usually means the service is not using NVIDIA CUDA acceleration yet.",
                diagnostics.LoadedRuntimeLibrary ?? "<unknown>");
        }
    }

    private static string FormatList(IEnumerable<string> values)
    {
        string[] items = values
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        return items.Length == 0 ? "<none>" : string.Join(", ", items);
    }
}

using System.Reflection;

static void DumpAssembly(string title, params string[] filters)
{
    Console.WriteLine($"=== {title} ===");
    var assembly = title switch
    {
        "SherpaOnnx" => typeof(SherpaOnnx.OfflineRecognizer).Assembly,
        "Whisper" => typeof(Whisper.net.WhisperFactory).Assembly,
        "PortAudio" => typeof(PortAudioSharp.PortAudio).Assembly,
        _ => throw new InvalidOperationException(title)
    };

    foreach (var type in assembly.GetExportedTypes()
                 .Where(t => filters.Length == 0 || filters.Any(f => t.FullName?.Contains(f, StringComparison.OrdinalIgnoreCase) == true))
                 .OrderBy(t => t.FullName))
    {
        Console.WriteLine(type.FullName);

        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            Console.WriteLine($"  ctor {ctor}");
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(m => !m.IsSpecialName)
                     .OrderBy(m => m.Name))
        {
            Console.WriteLine($"  method {method}");
        }

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .OrderBy(p => p.Name))
        {
            Console.WriteLine($"  prop {prop.PropertyType.Name} {prop.Name}");
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .OrderBy(f => f.Name))
        {
            Console.WriteLine($"  field {field.FieldType.Name} {field.Name}");
        }
    }
}

DumpAssembly("SherpaOnnx",
    "OfflineRecognizerConfig",
    "OfflineModelConfig",
    "OfflineWhisperModelConfig",
    "OfflineParaformerModelConfig",
    "OfflineTransducerModelConfig",
    "OfflineZipformerCtcModelConfig",
    "OfflineWenetCtcModelConfig",
    "OnlineRecognizerConfig",
    "OnlineModelConfig",
    "OnlineTransducerModelConfig",
    "OnlineParaformerModelConfig",
    "OnlineZipformer2CtcModelConfig",
    "KeywordSpotterConfig",
    "OfflineTtsConfig",
    "OfflineTtsModelConfig",
    "OfflineTtsVitsModelConfig",
    "FeatureConfig",
    "OnlineStream",
    "OfflineStream");
DumpAssembly("Whisper", "WhisperFactory", "WhisperFactoryOptions", "WhisperProcessorBuilder", "WhisperProcessor", "SegmentData");
DumpAssembly("PortAudio", "PortAudio", "Stream", "DeviceInfo", "StreamParameters");

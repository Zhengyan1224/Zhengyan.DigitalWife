using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static class AndroidScriptLinkerTests
{
    // Inspect the Release trimmer output, not the desktop framework used by
    // ordinary tests. AddSeconds used to disappear only in the Android APK.
    internal static void Verify(string linkedDirectory)
    {
        VerifyMembers(Path.Combine(linkedDirectory, "System.Private.CoreLib.dll"), "System", "DateTimeOffset",
            ["get_UtcNow", "AddSeconds", "AddMilliseconds", "AddMinutes", "op_Subtraction", "op_GreaterThanOrEqual"], ["MinValue"]);
        VerifyMembers(Path.Combine(linkedDirectory, "System.Private.CoreLib.dll"), "System", "DateTime",
            ["get_Now", "AddSeconds", "Subtract", "ToString"], []);
        VerifyMembers(Path.Combine(linkedDirectory, "System.Private.CoreLib.dll"), "System", "TimeSpan",
            ["FromSeconds", "get_TotalSeconds"], []);
        VerifyMembers(Path.Combine(linkedDirectory, "Zhengyan.DigitalWife.GameProjects.Core.dll"),
            "Zhengyan.DigitalWife.GameProjects", "ScriptBinding", ["get_Enabled", "set_Enabled"], []);
        VerifyMembers(Path.Combine(linkedDirectory, "Zhengyan.DigitalWife.GamePlayer.Android.dll"),
            "Zhengyan.DigitalWife.GamePlayer.Android", "AndroidScriptInput", ["get_Touches", "get_TouchTransitions", "get_IsTouchAvailable"], []);
        VerifyMembers(Path.Combine(linkedDirectory, "Zhengyan.DigitalWife.GamePlayer.Android.Scripting.dll"),
            "Zhengyan.DigitalWife.GamePlayer.Android", "AndroidScriptInputApi", ["TryGetTouch", "get_PrimaryTouch", "get_ActiveTouchCount"], []);
        VerifyMembers(Path.Combine(linkedDirectory, "Zhengyan.DigitalWife.Abstractions.dll"),
            "Zhengyan.DigitalWife.GameProjects", "AndroidScriptGlobalsContract", ["get_PlatformName", "get_IsAndroid", "get_IsWindows", "get_IsLinux", "get_IsMacOS"], []);
        VerifyMembers(Path.Combine(linkedDirectory, "Zhengyan.DigitalWife.Mmd.Game.dll"),
            "Zhengyan.DigitalWife.Mmd.Game.Input", "TouchPoint", ["get_X", "get_Y", "get_Phase", "get_IsActive", "get_IsEnded", "get_Kind"], []);
        Console.WriteLine("PASS Linked Android time APIs and disabled-binding serialization members");
        Console.WriteLine("PASS Linked Android script Touch APIs and platform identifiers");
        VerifyMembers(Path.Combine(linkedDirectory, "Zhengyan.DigitalWife.GamePlayer.Android.Scripting.dll"),
            "Zhengyan.DigitalWife.GamePlayer.Android", "AndroidScriptAsr", ["StartStreamingRecognition", "StartWakeWordMonitoring", "Cancel", "StopStreamingRecognition"], []);
        VerifyMembers(Path.Combine(linkedDirectory, "Zhengyan.DigitalWife.GamePlayer.Android.Scripting.dll"),
            "Zhengyan.DigitalWife.GamePlayer.Android", "AndroidScriptTts", ["SpeakAsync", "Stop"], []);
        VerifyMembers(Path.Combine(linkedDirectory, "Zhengyan.DigitalWife.Speech.SherpaOnnx.dll"),
            "Zhengyan.DigitalWife.Speech.SherpaOnnx", "SherpaOnnxSpeechRecognizer", ["RecognizeAsync", "CreateStreamingSession"], []);
        VerifyMembers(Path.Combine(linkedDirectory, "sherpa-onnx.dll"), "SherpaOnnx", "OfflineTts", ["Generate"], []);
        Console.WriteLine("PASS Linked local Sherpa ASR/TTS and native binding APIs");
    }

    private static void VerifyMembers(string path, string ns, string name, string[] methods, string[] fields)
    {
        using FileStream file = File.OpenRead(path);
        using PEReader pe = new(file);
        MetadataReader metadata = pe.GetMetadataReader();
        TypeDefinition type = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition).Single(type =>
            metadata.GetString(type.Namespace) == ns && metadata.GetString(type.Name) == name);
        HashSet<string> actualMethods = type.GetMethods().Select(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name)).ToHashSet();
        HashSet<string> actualFields = type.GetFields().Select(handle => metadata.GetString(metadata.GetFieldDefinition(handle).Name)).ToHashSet();
        foreach (string method in methods)
            if (!actualMethods.Contains(method)) throw new InvalidOperationException($"Android trimming removed {ns}.{name}.{method} from {path}.");
        foreach (string field in fields)
            if (!actualFields.Contains(field)) throw new InvalidOperationException($"Android trimming removed {ns}.{name}.{field} from {path}.");
    }
}

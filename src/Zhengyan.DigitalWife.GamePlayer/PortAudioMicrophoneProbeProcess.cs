using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Zhengyan.DigitalWife.Audio.PortAudio;

namespace Zhengyan.DigitalWife.GamePlayer;

internal static class PortAudioMicrophoneProbeProcess
{
    private const string ProbeArgument = "--dw-portaudio-microphone-probe";
    private const string ProbeOutputEnvironmentVariable = "DW_PORTAUDIO_MICROPHONE_PROBE_OUTPUT";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static PortAudioMicrophoneDetectionResult Detect(PortAudioMicrophoneDetectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string tempDirectory = Path.Combine(Path.GetTempPath(), "dw-portaudio-microphone-probe-" + Guid.NewGuid().ToString("N"));
        string optionsPath = Path.Combine(tempDirectory, "options.json");
        string resultPath = Path.Combine(tempDirectory, "result.json");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            File.WriteAllText(optionsPath, JsonSerializer.Serialize(options, JsonOptions), Encoding.UTF8);

            using Process process = new()
            {
                StartInfo = CreateProbeStartInfo(optionsPath, resultPath)
            };

            if (!process.Start())
            {
                return PortAudioMicrophoneDetectionResult.NotDetected("Failed to start PortAudio microphone probe process.", []);
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(ProbeTimeout))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                TryWaitForExit(process);
                return PortAudioMicrophoneDetectionResult.NotDetected("PortAudio microphone probe process timed out.", []);
            }

            string stdout = ReadCompletedOutput(stdoutTask);
            string stderr = ReadCompletedOutput(stderrTask);
            if (!File.Exists(resultPath))
            {
                string message = $"PortAudio microphone probe process exited with code {process.ExitCode} without writing a result.";
                string details = BuildProcessDetails(stdout, stderr);
                return PortAudioMicrophoneDetectionResult.NotDetected(
                    string.IsNullOrWhiteSpace(details) ? message : $"{message} {details}",
                    []);
            }

            string resultJson = File.ReadAllText(resultPath, Encoding.UTF8);
            PortAudioMicrophoneDetectionResult? result = JsonSerializer.Deserialize<PortAudioMicrophoneDetectionResult>(resultJson, JsonOptions);
            if (result is null)
            {
                return PortAudioMicrophoneDetectionResult.NotDetected("PortAudio microphone probe process returned an empty result.", []);
            }

            return result;
        }
        catch (Exception ex)
        {
            return PortAudioMicrophoneDetectionResult.NotDetected($"PortAudio microphone probe process failed: {ex.Message}", []);
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    public static int RunProbeChild(string optionsPath)
    {
        try
        {
            string outputPath = Environment.GetEnvironmentVariable(ProbeOutputEnvironmentVariable) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                Console.Error.WriteLine($"{ProbeOutputEnvironmentVariable} is not set.");
                return 2;
            }

            string optionsJson = File.ReadAllText(optionsPath, Encoding.UTF8);
            PortAudioMicrophoneDetectionOptions? options = JsonSerializer.Deserialize<PortAudioMicrophoneDetectionOptions>(optionsJson, JsonOptions);
            if (options is null)
            {
                Console.Error.WriteLine("PortAudio microphone probe options are invalid.");
                return 2;
            }

            PortAudioMicrophoneDetectionResult result = new PortAudioMicrophoneAutoDetector(
                NullLogger<PortAudioMicrophoneAutoDetector>.Instance).Detect(options);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(result, JsonOptions), Encoding.UTF8);
            return result.Success ? 0 : 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static ProcessStartInfo CreateProbeStartInfo(string optionsPath, string resultPath)
    {
        string executablePath = Environment.ProcessPath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(executablePath)
            && !string.Equals(Path.GetExtension(executablePath), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add(ProbeArgument);
            startInfo.ArgumentList.Add(optionsPath);
            startInfo.Environment[ProbeOutputEnvironmentVariable] = resultPath;
            return startInfo;
        }

        string assemblyPath = typeof(Program).Assembly.Location;
        var dotnetStartInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        dotnetStartInfo.ArgumentList.Add(assemblyPath);
        dotnetStartInfo.ArgumentList.Add(ProbeArgument);
        dotnetStartInfo.ArgumentList.Add(optionsPath);
        dotnetStartInfo.Environment[ProbeOutputEnvironmentVariable] = resultPath;
        return dotnetStartInfo;
    }

    private static string BuildProcessDetails(string stdout, string stderr)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(stdout))
        {
            builder.Append("stdout: ").Append(Truncate(stdout.Trim(), 512));
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append("stderr: ").Append(Truncate(stderr.Trim(), 512));
        }

        return builder.ToString();
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string ReadCompletedOutput(Task<string> outputTask)
    {
        try
        {
            return outputTask.IsCompleted
                ? outputTask.GetAwaiter().GetResult()
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryWaitForExit(Process process)
    {
        try
        {
            process.WaitForExit(1000);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}

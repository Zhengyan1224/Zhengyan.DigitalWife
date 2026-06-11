using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Zhengyan.DigitalWife.GamePlayer;

public sealed class RuntimeCommandResult
{
    public int ExitCode { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;

    public bool TimedOut { get; init; }

    public bool Success => !TimedOut && ExitCode == 0;
}

public sealed class RuntimeProjectControl
{
    private readonly GamePlayerGame _game;

    internal RuntimeProjectControl(GamePlayerGame game)
    {
        _game = game;
    }

    public bool UseOpenCL
    {
        get => _game.Project.Runtime.UseOpenCL;
        set => SetUseOpenCL(value);
    }

    public bool IsUsingOpenCL => _game.IsUsingOpenClRuntime;

    public string ComputeBackend => _game.CurrentComputeBackend;

    public void SetUseOpenCL(bool useOpenCl)
    {
        _game.Project.Runtime.UseOpenCL = useOpenCl;
        _game.ApplyRuntimeSettings();
    }

    public RuntimeCommandResult ExecuteCommand(string fileName, string arguments = "", int timeoutMilliseconds = 30000, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Command file name cannot be empty.", nameof(fileName));
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? _game.ProjectDirectory
                : workingDirectory!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return Execute(startInfo, timeoutMilliseconds);
    }

    public RuntimeCommandResult ExecuteCommand(
        string fileName,
        IEnumerable<string> arguments,
        int timeoutMilliseconds = 30000,
        string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Command file name cannot be empty.", nameof(fileName));
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? _game.ProjectDirectory
                : workingDirectory!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in arguments ?? [])
        {
            startInfo.ArgumentList.Add(argument ?? string.Empty);
        }

        return Execute(startInfo, timeoutMilliseconds);
    }

    public RuntimeCommandResult ExecuteShellCommand(string command, int timeoutMilliseconds = 30000, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Shell command cannot be empty.", nameof(command));
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ExecuteCommand("cmd.exe", "/c " + QuoteShellArgument(command), timeoutMilliseconds, workingDirectory)
            : ExecuteCommand("/bin/sh", "-c " + QuoteShellArgument(command), timeoutMilliseconds, workingDirectory);
    }

    private static string QuoteShellArgument(string value)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        return "'" + value.Replace("'", "'\"'\"'") + "'";
    }

    private static RuntimeCommandResult Execute(ProcessStartInfo startInfo, int timeoutMilliseconds)
    {
        using Process process = new()
        {
            StartInfo = startInfo
        };

        process.Start();
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();

        int timeout = timeoutMilliseconds <= 0 ? Timeout.Infinite : timeoutMilliseconds;
        bool exited = process.WaitForExit(timeout);
        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }

        try
        {
            Task.WaitAll([outputTask, errorTask], 1000);
        }
        catch
        {
        }

        return new RuntimeCommandResult
        {
            ExitCode = exited ? process.ExitCode : -1,
            StandardOutput = outputTask.IsCompletedSuccessfully ? outputTask.Result : string.Empty,
            StandardError = errorTask.IsCompletedSuccessfully ? errorTask.Result : string.Empty,
            TimedOut = !exited
        };
    }
}

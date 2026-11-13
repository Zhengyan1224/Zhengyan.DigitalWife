using System.Diagnostics;
using System.Runtime.InteropServices;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed class RuntimeCommandResult
{
    public int ExitCode {get;init;}
    public string StandardOutput {get;init;}=string.Empty;
    public string StandardError {get;init;}=string.Empty;
    public bool TimedOut {get;init;}
    public bool Success=>!TimedOut&&ExitCode==0;
}

public sealed class RuntimeProjectControl
{
    private GameRuntimeSettings _settings=new();
    private string _directory=Environment.CurrentDirectory;
    private Func<string> _backend=()=>OperatingSystem.IsBrowser()?"WASM":"CPU";
    private Action? _apply;
    private bool _openClSupported,_vulkanSupported;
    public bool UseOpenCL {get=>_openClSupported&&_settings.UseOpenCL;set=>SetUseOpenCL(value);}
    public bool UseVulkanCompute {get=>_vulkanSupported&&_settings.UseVulkanCompute;set=>SetUseVulkanCompute(value);}
    public bool IsUsingOpenCL=>ComputeBackend=="OpenCL";
    public bool IsUsingVulkanCompute=>ComputeBackend=="Vulkan Compute";
    public string ComputeBackend=>_backend();
    public void Configure(GameRuntimeSettings settings,string directory,Func<string>? backend=null,Action? apply=null,bool openClSupported=false,bool vulkanSupported=false)
    {_settings=settings;_directory=directory;_backend=backend??_backend;_apply=apply;_openClSupported=openClSupported;_vulkanSupported=vulkanSupported;}
    public void SetUseOpenCL(bool useOpenCl)
    {if(useOpenCl&&!_openClSupported)throw new PlatformNotSupportedException("OpenCL compute is not available on this player.");_settings.UseOpenCL=useOpenCl;_apply?.Invoke();}
    public void SetUseVulkanCompute(bool useVulkanCompute)
    {if(useVulkanCompute&&!_vulkanSupported)throw new PlatformNotSupportedException("Vulkan compute is not available on this player.");_settings.UseVulkanCompute=useVulkanCompute;_apply?.Invoke();}

    public RuntimeCommandResult ExecuteCommand(string fileName, string arguments = "", int timeoutMilliseconds = 30000, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Command file name cannot be empty.", nameof(fileName));
        }
        if(OperatingSystem.IsBrowser())return UnsupportedCommand();

        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? _directory
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
        if(OperatingSystem.IsBrowser())return UnsupportedCommand();

        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? _directory
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
            ? ExecuteCommand("cmd.exe", new[]{"/c",command}, timeoutMilliseconds, workingDirectory)
            : ExecuteCommand(OperatingSystem.IsAndroid()?"/system/bin/sh":"/bin/sh", new[]{"-c",command}, timeoutMilliseconds, workingDirectory);
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
        if(OperatingSystem.IsBrowser())return UnsupportedCommand();
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
    private static RuntimeCommandResult UnsupportedCommand()=>new(){ExitCode=-1,StandardError="Browsers cannot execute system commands. Use an explicitly configured server API."};
}

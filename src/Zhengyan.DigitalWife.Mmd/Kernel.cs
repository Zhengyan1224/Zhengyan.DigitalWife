using System.Diagnostics;
using System.Reflection;
using System.Text;
using Silk.NET.OpenCL;

namespace Zhengyan.DigitalWife.Mmd;

public unsafe class Kernel : IDisposable
{
    private readonly CL _cl;
    private readonly nint _platform;
    private readonly nint _device;
    private readonly nint _context;
    private readonly nint _queue;
    private readonly nint _program;
    private readonly nint _kernel;
    private readonly List<nint> _buffers;
    private static bool? _openClProbeResult;

    public static bool UseOpenCL { get; set; } = true;

    public double Version { get; }

    public uint Alignment { get; }

    public uint[] MaxWorkItemSizes { get; }

    public bool UseCoarseBuffer { get; }

    internal Kernel(CL cl, nint platform, nint device, nint context, nint queue, nint program, nint kernel)
    {
        _cl = cl;
        _platform = platform;
        _device = device;
        _context = context;
        _queue = queue;
        _program = program;
        _kernel = kernel;
        _buffers = [];

        Version = GetVersion(cl, device);
        Alignment = GetAlignment(cl, device);
        MaxWorkItemSizes = GetMaxWorkItemSizes(cl, device);

        if (Version >= 2.0)
        {
            DeviceSvmCapabilities svmCapabilities;
            cl.GetDeviceInfo(device, DeviceInfo.SvmCapabilities, sizeof(DeviceSvmCapabilities), &svmCapabilities, null);

            if (svmCapabilities.HasFlag(DeviceSvmCapabilities.CoarseGrainBuffer))
            {
                UseCoarseBuffer = true;
            }
        }
    }

    public nint CreateBuffer<T>(int length, T* host = null, MemFlags flags = MemFlags.None) where T : unmanaged
    {
        nint bufferId = _cl.CreateBuffer(_context, flags, (uint)(length * sizeof(T)), host, null);
        _buffers.Add(bufferId);
        return bufferId;
    }

    public void DeleteBuffer(nint buffer)
    {
        if (_buffers.Contains(buffer))
        {
            _cl.ReleaseMemObject(buffer);
            _buffers.Remove(buffer);
        }
    }

    public T* SvmAlloc<T>(int length, MemFlags flags = MemFlags.None) where T : unmanaged
    {
        return (T*)_cl.Svmalloc(_context, (SvmMemFlags)flags, (uint)(length * sizeof(T)), 0);
    }

    public void FreeSvm(void* ptr)
    {
        _cl.Svmfree(_context, ptr);
    }

    public T* MapBuffer<T>(nint buffer, int length, MapFlags flags = MapFlags.None) where T : unmanaged
    {
        return (T*)_cl.EnqueueMapBuffer(_queue, buffer, false, flags, 0, (uint)(length * sizeof(T)), 0, null, null, null);
    }

    public void UnmapBuffer(nint buffer, void* ptr)
    {
        _cl.EnqueueUnmapMemObject(_queue, buffer, ptr, 0, null, null);
    }

    public void MapSvm<T>(T* ptr, int length, MapFlags flags = MapFlags.None) where T : unmanaged
    {
        _cl.EnqueueSvmmap(_queue, false, flags, ptr, (uint)(length * sizeof(T)), 0, null, null);
    }

    public void UnmapSvm(void* ptr)
    {
        _cl.EnqueueSvmunmap(_queue, ptr, 0, null, null);
    }

    public void Flush()
    {
        State(_cl.Flush(_queue));
    }

    public void Finish()
    {
        State(_cl.Finish(_queue));
    }

    public void SetArgument(uint index, nint buffer)
    {
        int state = _cl.SetKernelArg(_kernel, index, (uint)sizeof(nint), &buffer);
        State(state);
    }

    public void SetSvmArgument(uint index, void* ptr)
    {
        int state = _cl.SetKernelArgSvmpointer(_kernel, index, ptr);
        State(state);
    }

    public void Run1(int size)
    {
        uint maxWorkItemSize = MaxWorkItemSizes[0] / 2;

        nuint* globalWorkSize = stackalloc nuint[1];
        globalWorkSize[0] = (nuint)(size / maxWorkItemSize + 1) * maxWorkItemSize;

        nuint* localWorkSize = stackalloc nuint[1];
        localWorkSize[0] = maxWorkItemSize;

        int state = _cl.EnqueueNdrangeKernel(_queue, _kernel, 1, null, globalWorkSize, localWorkSize, 0, null, null);
        State(state);
    }

    public void Dispose()
    {
        foreach (nint buffer in _buffers)
        {
            _cl.ReleaseMemObject(buffer);
        }

        _buffers.Clear();
        _cl.ReleaseKernel(_kernel);
        _cl.ReleaseProgram(_program);
        _cl.ReleaseCommandQueue(_queue);
        _cl.ReleaseContext(_context);
        _cl.ReleaseDevice(_device);
        _cl.Dispose();
        GC.SuppressFinalize(this);
    }

    public static Kernel? Create(string code, string method, string[]? options = null)
    {
        if (!UseOpenCL)
        {
            return null;
        }

        if (!CanUseOpenClSafely())
        {
            return null;
        }

        CL cl = CL.GetApi();

        nint platform;
        nint device;
        nint context;
        nint queue;
        nint program;
        nint kernel;

        (platform, device) = GetHighestVersion(cl);
        if (platform == 0 || device == 0)
        {
            return null;
        }

        context = cl.CreateContext(null, 1, in device, null, null, null);
        if (context == 0)
        {
            return null;
        }

        queue = cl.CreateCommandQueue(context, device, CommandQueueProperties.None, null);
        if (queue == 0)
        {
            return null;
        }

        options ??= ["-cl-opt-disable"];
        program = cl.CreateProgramWithSource(context, 1, [code], null, null);

        if (cl.BuildProgram(program, 1, &device, string.Join(" ", options), null, null) != 0)
        {
            nuint length;
            cl.GetProgramBuildInfo(program, device, ProgramBuildInfo.BuildLog, 0, null, &length);
            byte* buffer = stackalloc byte[(int)length];
            cl.GetProgramBuildInfo(program, device, ProgramBuildInfo.BuildLog, length, buffer, null);
            Console.WriteLine("Build error: " + Encoding.UTF8.GetString(buffer, (int)length));
            return null;
        }

        kernel = cl.CreateKernel(program, method, null);
        if (kernel == 0)
        {
            return null;
        }

        return new Kernel(cl, platform, device, context, queue, program, kernel);
    }

    public static bool CanUseOpenClSafely()
    {
        if (!UseOpenCL)
        {
            return false;
        }

        if (_openClProbeResult.HasValue)
        {
            return _openClProbeResult.Value;
        }

        _openClProbeResult = ProbeOpenClOutOfProcess();
        return _openClProbeResult.Value;
    }

    public static void ResetOpenClProbe()
    {
        _openClProbeResult = null;
    }

    public static bool ProbeCurrentProcessUnsafe()
    {
        CL cl = CL.GetApi();
        (nint platform, nint device) = GetHighestVersion(cl);
        if (device != 0)
        {
            cl.ReleaseDevice(device);
        }

        cl.Dispose();
        return platform != 0 && device != 0;
    }

    private static void State(int errorCode)
    {
        if ((ErrorCodes)errorCode != ErrorCodes.Success)
        {
            Console.WriteLine(errorCode);
        }
    }

    private static (nint Platform, nint Device) GetHighestVersion(CL cl)
    {
        uint* numPlatforms = stackalloc uint[1];
        cl.GetPlatformIDs(0, null, numPlatforms);

        nint* platformIds = stackalloc nint[(int)*numPlatforms];
        cl.GetPlatformIDs(*numPlatforms, platformIds, null);

        nint highestPlatform = 0;
        nint highestDevice = 0;
        double highestVersion = 0.0;
        for (int i = 0; i < *numPlatforms; i++)
        {
            nint platform = platformIds[i];
            uint numDevices = GetMaxDevices(cl, platform);
            nint[] deviceIds = GetDevices(cl, platform, numDevices);

            for (int j = 0; j < numDevices; j++)
            {
                nint device = deviceIds[j];
                double version = GetVersion(cl, device);

                if (version > highestVersion)
                {
                    if (highestVersion > 0.0)
                    {
                        cl.ReleaseDevice(highestDevice);
                    }

                    highestPlatform = platform;
                    highestDevice = device;
                    highestVersion = version;
                }
                else
                {
                    cl.ReleaseDevice(device);
                }
            }
        }

        return (highestPlatform, highestDevice);
    }

    private static uint GetMaxDevices(CL cl, nint platform)
    {
        uint* numDevices = stackalloc uint[1];
        cl.GetDeviceIDs(platform, DeviceType.Gpu, 0, null, numDevices);
        return *numDevices;
    }

    private static nint[] GetDevices(CL cl, nint platform, uint numDevices)
    {
        nint[] deviceIds = new nint[(int)numDevices];
        fixed (nint* ptr = deviceIds)
        {
            cl.GetDeviceIDs(platform, DeviceType.Gpu, numDevices, ptr, null);
        }

        return deviceIds;
    }

    private static double GetVersion(CL cl, nint device)
    {
        byte* version = stackalloc byte[1024];
        cl.GetDeviceInfo(device, DeviceInfo.Version, 1024, version, null);

        string versionString = new((sbyte*)version);
        int index = versionString.IndexOf(' ');
        versionString = versionString.Substring(index + 1, 3);
        return Convert.ToDouble(versionString);
    }

    private static uint GetAlignment(CL cl, nint device)
    {
        uint* alignment = stackalloc uint[1];
        cl.GetDeviceInfo(device, DeviceInfo.MemBaseAddrAlign, sizeof(uint), alignment, null);
        return *alignment;
    }

    private static uint[] GetMaxWorkItemSizes(CL cl, nint device)
    {
        uint maxWorkItemDimensions = 0;
        cl.GetDeviceInfo(device, DeviceInfo.MaxWorkItemDimensions, sizeof(uint), &maxWorkItemDimensions, null);

        nuint* maxWorkItemSizes = stackalloc nuint[(int)maxWorkItemDimensions];
        cl.GetDeviceInfo(device, DeviceInfo.MaxWorkItemSizes, (nuint)(sizeof(nuint) * maxWorkItemDimensions), maxWorkItemSizes, null);

        uint[] sizes = new uint[(int)maxWorkItemDimensions];
        for (int i = 0; i < maxWorkItemDimensions; i++)
        {
            sizes[i] = (uint)maxWorkItemSizes[i];
        }

        return sizes;
    }

    private static bool ProbeOpenClOutOfProcess()
    {
        try
        {
            string processPath = Environment.ProcessPath ?? string.Empty;
            string? entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
            {
                return false;
            }

            ProcessStartInfo startInfo = new()
            {
                FileName = processPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            string processFileName = Path.GetFileName(processPath);
            bool isDotnetHost = string.Equals(processFileName, "dotnet", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processFileName, "dotnet.exe", StringComparison.OrdinalIgnoreCase);
            if (isDotnetHost)
            {
                if (string.IsNullOrWhiteSpace(entryAssemblyPath) || !File.Exists(entryAssemblyPath))
                {
                    return false;
                }

                startInfo.ArgumentList.Add(entryAssemblyPath);
            }

            startInfo.ArgumentList.Add("--dw-opencl-probe");

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start OpenCL probe process.");
            process.WaitForExit(5000);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

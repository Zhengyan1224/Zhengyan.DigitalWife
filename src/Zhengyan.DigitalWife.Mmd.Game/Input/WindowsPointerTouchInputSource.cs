using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Windowing;

namespace Zhengyan.DigitalWife.Mmd.Game.Input;

internal sealed unsafe class WindowsPointerTouchInputSource : ITouchInputSource
{
    private const int GwlpWndProc = -4;
    private const uint WmPointerUpdate = 0x0245;
    private const uint WmPointerDown = 0x0246;
    private const uint WmPointerUp = 0x0247;
    private const uint WmPointerCaptureChanged = 0x024C;
    private const uint PtTouch = 2;
    private const uint PtPen = 3;

    private readonly object _lock = new();
    private readonly List<TouchInputEvent> _events = [];
    private readonly WndProcDelegate _wndProc;
    private readonly IntPtr _hwnd;
    private readonly IntPtr _previousWndProc;
    private bool _disposed;

    static WindowsPointerTouchInputSource()
    {
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(WindowsPointerTouchInputSource).Assembly, ResolveNativeLibrary);
        }
        catch (InvalidOperationException)
        {
            // A resolver may already be installed for the engine assembly.
        }
    }

    private WindowsPointerTouchInputSource(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _wndProc = WndProc;
        _previousWndProc = SetWindowLongPtr(_hwnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_wndProc));
        IsAvailable = _previousWndProc != IntPtr.Zero;
    }

    public bool IsAvailable { get; }

    public static ITouchInputSource Create(IWindow window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return NullTouchInputSource.Instance;
        }

        IntPtr hwnd = TryGetWin32Hwnd(window);
        if (hwnd == IntPtr.Zero)
        {
            return NullTouchInputSource.Instance;
        }

        try
        {
            WindowsPointerTouchInputSource source = new(hwnd);
            if (source.IsAvailable)
            {
                return source;
            }

            source.Dispose();
            return NullTouchInputSource.Instance;
        }
        catch
        {
            return NullTouchInputSource.Instance;
        }
    }

    public IReadOnlyList<TouchInputEvent> ConsumeEvents()
    {
        lock (_lock)
        {
            if (_events.Count == 0)
            {
                return [];
            }

            TouchInputEvent[] snapshot = _events.ToArray();
            _events.Clear();
            return snapshot;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hwnd != IntPtr.Zero && _previousWndProc != IntPtr.Zero)
        {
            try
            {
                _ = SetWindowLongPtr(_hwnd, GwlpWndProc, _previousWndProc);
            }
            catch
            {
            }
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            HandlePointerMessage(hWnd, message, wParam, lParam);
        }
        catch
        {
        }

        return _previousWndProc != IntPtr.Zero
            ? CallWindowProc(_previousWndProc, hWnd, message, wParam, lParam)
            : DefWindowProc(hWnd, message, wParam, lParam);
    }

    private void HandlePointerMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        TouchPhase phase = message switch
        {
            WmPointerDown => TouchPhase.Started,
            WmPointerUpdate => TouchPhase.Moved,
            WmPointerUp => TouchPhase.Ended,
            WmPointerCaptureChanged => TouchPhase.Cancelled,
            _ => default
        };

        if (message is not (WmPointerDown or WmPointerUpdate or WmPointerUp or WmPointerCaptureChanged))
        {
            return;
        }

        uint pointerId = GetPointerId(wParam);
        if (!GetPointerInfo(pointerId, out PointerInfo pointerInfo))
        {
            Point point = PointFromLParam(lParam);
            Enqueue(new TouchInputEvent((int)pointerId, point.X, point.Y, phase, TouchInputKind.Unknown, phase == TouchPhase.Ended ? 0.0f : 1.0f));
            return;
        }

        TouchInputKind kind = pointerInfo.PointerType switch
        {
            PtTouch => TouchInputKind.Touch,
            PtPen => TouchInputKind.Pen,
            _ => TouchInputKind.Unknown
        };
        if (kind == TouchInputKind.Unknown)
        {
            return;
        }

        Point clientPoint = pointerInfo.PtPixelLocation;
        _ = ScreenToClient(hWnd, ref clientPoint);
        float pressure = phase is TouchPhase.Ended or TouchPhase.Cancelled ? 0.0f : 1.0f;
        Enqueue(new TouchInputEvent((int)pointerId, clientPoint.X, clientPoint.Y, phase, kind, pressure));
    }

    private void Enqueue(TouchInputEvent touchEvent)
    {
        lock (_lock)
        {
            _events.Add(touchEvent);
        }
    }

    private static uint GetPointerId(IntPtr wParam)
    {
        return (uint)(wParam.ToInt64() & 0xFFFF);
    }

    private static Point PointFromLParam(IntPtr lParam)
    {
        long value = lParam.ToInt64();
        return new Point(unchecked((short)(value & 0xFFFF)), unchecked((short)((value >> 16) & 0xFFFF)));
    }

    private static IntPtr TryGetWin32Hwnd(IWindow window)
    {
        IntPtr glfwWindow = TryGetGlfwWindowPointer(window);
        if (glfwWindow == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            return GetWin32Window(glfwWindow);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static IntPtr TryGetGlfwWindowPointer(IWindow window)
    {
        IntPtr handle = window.Handle;
        return handle != IntPtr.Zero
            ? handle
            : TryFindGlfwWindowPointer(window, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
    }

    private static IntPtr TryFindGlfwWindowPointer(object? value, HashSet<object> visited, int depth)
    {
        if (value is null || depth > 6)
        {
            return IntPtr.Zero;
        }

        Type type = value.GetType();
        if (!type.IsValueType && !visited.Add(value))
        {
            return IntPtr.Zero;
        }

        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            object? fieldValue;
            try
            {
                fieldValue = field.GetValue(value);
            }
            catch
            {
                continue;
            }

            if (fieldValue is null)
            {
                continue;
            }

            Type fieldType = field.FieldType;
            if (fieldType.IsPointer && fieldType.Name.Contains("WindowHandle", StringComparison.Ordinal))
            {
                return (IntPtr)Pointer.Unbox(fieldValue);
            }

            if (fieldType.IsPrimitive || fieldType.IsEnum || fieldType == typeof(string) || fieldType.IsPointer)
            {
                continue;
            }

            IntPtr nested = TryFindGlfwWindowPointer(fieldValue, visited, depth + 1);
            if (nested != IntPtr.Zero)
            {
                return nested;
            }
        }

        return IntPtr.Zero;
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        _ = assembly;
        _ = searchPath;

        if (!libraryName.Equals("glfw3", StringComparison.OrdinalIgnoreCase)
            && !libraryName.Equals("glfw3.dll", StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        foreach (string candidate in GetBundledGlfwCandidates())
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> GetBundledGlfwCandidates()
    {
        const string fileName = "glfw3.dll";
        string baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, fileName);

        string? rid = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(rid))
        {
            yield return Path.Combine(baseDirectory, "runtimes", rid, "native", fileName);
        }
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, index, newLong)
            : new IntPtr(SetWindowLong32(hWnd, index, newLong.ToInt32()));
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointerInfo
    {
        public uint PointerType;
        public uint PointerId;
        public uint FrameId;
        public uint PointerFlags;
        public IntPtr SourceDevice;
        public IntPtr HwndTarget;
        public Point PtPixelLocation;
        public Point PtHimetricLocation;
        public Point PtPixelLocationRaw;
        public Point PtHimetricLocationRaw;
        public uint Time;
        public uint HistoryCount;
        public int InputData;
        public uint KeyStates;
        public ulong PerformanceCount;
        public uint ButtonChangeType;
    }

    [DllImport("glfw3", EntryPoint = "glfwGetWin32Window", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GetWin32Window(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetPointerInfo", SetLastError = true)]
    private static extern bool GetPointerInfo(uint pointerId, out PointerInfo pointerInfo);

    [DllImport("user32.dll", EntryPoint = "ScreenToClient", SetLastError = true)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref Point point);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr newLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int index, int newLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW", SetLastError = true)]
    private static extern IntPtr CallWindowProc(IntPtr previousWndProc, IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", SetLastError = true)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
}

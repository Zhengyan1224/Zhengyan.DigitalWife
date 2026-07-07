using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Windowing;

namespace Zhengyan.DigitalWife.Mmd.Game.Input;

internal sealed unsafe class X11TouchInputSource : ITouchInputSource
{
    private const string XInputExtensionName = "XInputExtension";
    private const int GenericEvent = 35;
    private const int Success = 0;
    private const int XIAllMasterDevices = 1;
    private const int XITouchBegin = 18;
    private const int XITouchUpdate = 19;
    private const int XITouchEnd = 20;
    private const int XIEventMaskBytes = 4;
    private const int XEventBufferSize = 192;
    private const int MaxTouchEventsPerFrame = 256;

    private readonly object _lock = new();
    private readonly List<TouchInputEvent> _events = [];
    private readonly IntPtr _display;
    private bool _disposed;

    static X11TouchInputSource()
    {
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(X11TouchInputSource).Assembly, ResolveNativeLibrary);
        }
        catch (InvalidOperationException)
        {
            // A resolver may already be installed for the engine assembly.
        }
    }

    private X11TouchInputSource(IntPtr display)
    {
        _display = display;
        IsAvailable = true;
    }

    public bool IsAvailable { get; }

    public static ITouchInputSource Create(IWindow window)
    {
        if (!OperatingSystem.IsLinux())
        {
            return NullTouchInputSource.Instance;
        }

        try
        {
            IntPtr glfwWindow = TryGetGlfwWindowPointer(window);
            if (glfwWindow == IntPtr.Zero)
            {
                return NullTouchInputSource.Instance;
            }

            nint x11Window = GetX11Window(glfwWindow);
            if (x11Window == 0)
            {
                return NullTouchInputSource.Instance;
            }

            IntPtr display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
            {
                return NullTouchInputSource.Instance;
            }

            if (!TrySelectTouchEvents(display, x11Window))
            {
                XCloseDisplay(display);
                return NullTouchInputSource.Instance;
            }

            return new X11TouchInputSource(display);
        }
        catch
        {
            return NullTouchInputSource.Instance;
        }
    }

    public IReadOnlyList<TouchInputEvent> ConsumeEvents()
    {
        if (_disposed || _display == IntPtr.Zero)
        {
            return [];
        }

        PumpEvents();
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
        if (_display != IntPtr.Zero)
        {
            try
            {
                XCloseDisplay(_display);
            }
            catch
            {
            }
        }
    }

    private void PumpEvents()
    {
        int eventCount = Math.Min(Math.Max(XPending(_display), 0), MaxTouchEventsPerFrame);
        byte* eventBytes = stackalloc byte[XEventBufferSize];
        for (int i = 0; i < eventCount; i++)
        {
            for (int b = 0; b < XEventBufferSize; b++)
            {
                eventBytes[b] = 0;
            }

            if (XNextEvent(_display, (IntPtr)eventBytes) != Success)
            {
                continue;
            }

            X11GenericEventCookie* cookie = (X11GenericEventCookie*)eventBytes;
            if (cookie->Type != GenericEvent || cookie->Extension == 0)
            {
                continue;
            }

            if (XGetEventData(_display, (IntPtr)cookie) == 0)
            {
                continue;
            }

            try
            {
                if (cookie->EvType is XITouchBegin or XITouchUpdate or XITouchEnd && cookie->Data != IntPtr.Zero)
                {
                    X11DeviceEvent* touchEvent = (X11DeviceEvent*)cookie->Data;
                    TouchPhase phase = cookie->EvType switch
                    {
                        XITouchBegin => TouchPhase.Started,
                        XITouchEnd => TouchPhase.Ended,
                        _ => TouchPhase.Moved
                    };
                    Enqueue(new TouchInputEvent(
                        touchEvent->Detail,
                        (float)touchEvent->EventX,
                        (float)touchEvent->EventY,
                        phase,
                        TouchInputKind.Touch,
                        phase == TouchPhase.Ended ? 0.0f : 1.0f));
                }
            }
            finally
            {
                XFreeEventData(_display, (IntPtr)cookie);
            }
        }
    }

    private void Enqueue(TouchInputEvent touchEvent)
    {
        lock (_lock)
        {
            _events.Add(touchEvent);
        }
    }

    private static bool TrySelectTouchEvents(IntPtr display, nint window)
    {
        if (XQueryExtension(display, XInputExtensionName, out _, out _, out _) == 0)
        {
            return false;
        }

        int major = 2;
        int minor = 2;
        if (XIQueryVersion(display, ref major, ref minor) != Success || major < 2 || (major == 2 && minor < 2))
        {
            return false;
        }

        byte* maskBytes = stackalloc byte[XIEventMaskBytes];
        for (int i = 0; i < XIEventMaskBytes; i++)
        {
            maskBytes[i] = 0;
        }

        SetXInputEventMask(maskBytes, XITouchBegin);
        SetXInputEventMask(maskBytes, XITouchUpdate);
        SetXInputEventMask(maskBytes, XITouchEnd);

        X11XIEventMask eventMask = new()
        {
            DeviceId = XIAllMasterDevices,
            MaskLen = XIEventMaskBytes,
            Mask = (IntPtr)maskBytes
        };

        int result = XISelectEvents(display, window, (IntPtr)(&eventMask), 1);
        _ = XFlush(display);
        return result == Success;
    }

    private static void SetXInputEventMask(byte* mask, int eventId)
    {
        mask[eventId >> 3] |= (byte)(1 << (eventId & 7));
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

        if (!libraryName.Equals("libglfw.so.3", StringComparison.OrdinalIgnoreCase)
            && !libraryName.Equals("glfw3", StringComparison.OrdinalIgnoreCase))
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
        const string fileName = "libglfw.so.3";
        string baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, fileName);

        string? rid = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "linux-x64",
            Architecture.Arm64 => "linux-arm64",
            Architecture.Arm => "linux-arm",
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(rid))
        {
            yield return Path.Combine(baseDirectory, "runtimes", rid, "native", fileName);
        }
    }

    [DllImport("libglfw.so.3", EntryPoint = "glfwGetX11Window", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint GetX11Window(IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XPending(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XNextEvent(IntPtr display, IntPtr eventReturn);

    [DllImport("libX11.so.6")]
    private static extern int XGetEventData(IntPtr display, IntPtr eventCookie);

    [DllImport("libX11.so.6")]
    private static extern void XFreeEventData(IntPtr display, IntPtr eventCookie);

    [DllImport("libX11.so.6")]
    private static extern int XQueryExtension(
        IntPtr display,
        string name,
        out int majorOpcodeReturn,
        out int firstEventReturn,
        out int firstErrorReturn);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    [DllImport("libXi.so.6")]
    private static extern int XIQueryVersion(IntPtr display, ref int majorVersionInOut, ref int minorVersionInOut);

    [DllImport("libXi.so.6")]
    private static extern int XISelectEvents(IntPtr display, nint window, IntPtr masks, int numMasks);

    [StructLayout(LayoutKind.Sequential)]
    private struct X11XIEventMask
    {
        public int DeviceId;

        public int MaskLen;

        public IntPtr Mask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct X11GenericEventCookie
    {
        public int Type;

        public nuint Serial;

        public int SendEvent;

        public IntPtr Display;

        public int Extension;

        public int EvType;

        public uint Cookie;

        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct X11DeviceEvent
    {
        public int Type;

        public nuint Serial;

        public int SendEvent;

        public IntPtr Display;

        public int Extension;

        public int EvType;

        public nuint Time;

        public int DeviceId;

        public int SourceId;

        public int Detail;

        public nint Root;

        public nint Event;

        public nint Child;

        public double RootX;

        public double RootY;

        public double EventX;

        public double EventY;

        public int Flags;

        public X11ButtonState Buttons;

        public X11ValuatorState Valuators;

        public X11ModifierState Mods;

        public X11ModifierState Group;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct X11ButtonState
    {
        public int MaskLen;

        public IntPtr Mask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct X11ValuatorState
    {
        public int MaskLen;

        public IntPtr Mask;

        public IntPtr Values;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct X11ModifierState
    {
        public int Base;

        public int Latched;

        public int Locked;

        public int Effective;
    }
}

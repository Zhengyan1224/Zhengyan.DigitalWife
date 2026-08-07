using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace Zhengyan.DigitalWife.GamePlayer;

internal static unsafe class DesktopSpritePlatform
{
    private const int RegionSampleStep = 4;
    private const int X11RegionSampleStep = 1;
    private const byte RegionAlphaThreshold = 8;
    private const byte X11RegionAlphaThreshold = 48;
    private static readonly object LogLock = new();
    private static readonly object X11RawScrollLock = new();
    private static readonly HashSet<string> LoggedMessages = [];
    private static readonly Dictionary<IntPtr, WindowsClickThroughState> WindowsClickThroughStates = [];
    private static readonly Dictionary<nint, X11ClickThroughState> X11ClickThroughStates = [];
    private static readonly Dictionary<IntPtr, MacClickThroughState> MacClickThroughStatesByView = [];
    private static readonly MacHitTestDelegate MacHitTestCallback = MacHitTest;
    private static IntPtr _x11RawScrollDisplay;
    private static int _x11RawScrollOpcode;
    private static IntPtr _fallbackX11Display;
    private static nint _fallbackX11Window;
    private static bool _x11RawScrollInitialized;
    private static bool _x11RawScrollUnavailable;
    private static bool _glfwX11DisplayUnavailable;
    private static bool _glfwX11WindowUnavailable;
    private static bool _x11NativeUnavailable;

    static DesktopSpritePlatform()
    {
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(DesktopSpritePlatform).Assembly, ResolveNativeLibrary);
        }
        catch (InvalidOperationException)
        {
            // Another component already installed a resolver for this assembly.
        }
    }

    public static void PreferX11ForDesktopSprite()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GLFW_PLATFORM")))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            return;
        }

        Environment.SetEnvironmentVariable("GLFW_PLATFORM", "x11");
    }

    public static void ApplyClickThrough(IWindow window, bool enabled)
    {
        if (window is null)
        {
            return;
        }

        if (!enabled)
        {
            if (OperatingSystem.IsWindows())
            {
                TryEnableWindowsClickThrough(window, false);
            }
            else if (OperatingSystem.IsMacOS())
            {
                TryEnableMacClickThrough(window, false);
            }
            else if (OperatingSystem.IsLinux())
            {
                TryEnableX11ClickThrough(window, false);
            }

            return;
        }

        if (OperatingSystem.IsWindows())
        {
            TryEnableWindowsClickThrough(window, true);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            TryEnableMacClickThrough(window, true);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            TryEnableX11ClickThrough(window, true);
        }
    }

    public static void SyncClickThroughRegion(IWindow window, byte[] framebufferRgba, int width, int height, bool enabled)
    {
        if (!enabled)
        {
            ApplyClickThrough(window, false);
            return;
        }

        width = Math.Max(width, 1);
        height = Math.Max(height, 1);

        if (OperatingSystem.IsWindows())
        {
            SyncWindowsClickThroughRegion(window, framebufferRgba, width, height);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            SyncMacClickThroughRegion(window, framebufferRgba, width, height);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            SyncX11ClickThroughRegion(window, framebufferRgba, width, height);
        }
    }

    public static bool TryGetGlobalCursorPosition(IWindow window, out System.Numerics.Vector2 position)
    {
        position = default;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (WindowsNative.GetCursorPos(out WindowsPoint point))
                {
                    position = new System.Numerics.Vector2(point.X, point.Y);
                    return true;
                }

                return false;
            }

            if (OperatingSystem.IsLinux())
            {
                if (!TryGetX11Display(out IntPtr display, allowFallbackOpenDisplay: true, logFailure: false))
                {
                    return false;
                }

                nint root = X11Native.XDefaultRootWindow(display);
                if (root == 0)
                {
                    return false;
                }

                int result = X11Native.XQueryPointer(
                    display,
                    root,
                    out _,
                    out _,
                    out int rootX,
                    out int rootY,
                    out _,
                    out _,
                    out _);
                if (result == 0)
                {
                    return false;
                }

                position = new System.Numerics.Vector2(rootX, rootY);
                return true;
            }

            if (OperatingSystem.IsMacOS())
            {
                IntPtr nsEvent = MacNative.objc_getClass("NSEvent");
                if (nsEvent == IntPtr.Zero)
                {
                    return false;
                }

                MacPoint point = MacNative.objc_msgSend_MacPoint(nsEvent, MacNative.sel_registerName("mouseLocation"));
                double screenHeight = TryGetMacMainScreenHeight();
                position = new System.Numerics.Vector2((float)point.X, (float)(screenHeight - point.Y));
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public static bool TryGetGlobalMouseButtonState(IWindow window, MouseButton button, out bool isDown)
    {
        _ = window;
        isDown = false;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                int virtualKey = button switch
                {
                    MouseButton.Left => WindowsNative.VkLButton,
                    MouseButton.Right => WindowsNative.VkRButton,
                    MouseButton.Middle => WindowsNative.VkMButton,
                    _ => 0
                };
                if (virtualKey == 0)
                {
                    return false;
                }

                isDown = (WindowsNative.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
                return true;
            }

            if (OperatingSystem.IsLinux())
            {
                if (!TryGetX11Display(out IntPtr display, allowFallbackOpenDisplay: true, logFailure: false))
                {
                    return false;
                }

                nint root = X11Native.XDefaultRootWindow(display);
                if (root == 0)
                {
                    return false;
                }

                int result = X11Native.XQueryPointer(
                    display,
                    root,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out uint mask);
                if (result == 0)
                {
                    return false;
                }

                uint buttonMask = button switch
                {
                    MouseButton.Left => X11Native.Button1Mask,
                    MouseButton.Middle => X11Native.Button2Mask,
                    MouseButton.Right => X11Native.Button3Mask,
                    _ => 0
                };
                if (buttonMask == 0)
                {
                    return false;
                }

                isDown = (mask & buttonMask) != 0;
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public static bool TryIsGlobalCursorOverVisiblePixel(IWindow window, out bool isVisible)
    {
        isVisible = false;

        if (window is null || !TryGetGlobalCursorPosition(window, out System.Numerics.Vector2 globalPosition))
        {
            return false;
        }

        if (OperatingSystem.IsLinux())
        {
            return TryHitTestX11VisiblePixel(window, globalPosition, out isVisible);
        }

        return false;
    }

    public static float ConsumeGlobalScrollDeltaY(IWindow window)
    {
        if (window is null || !OperatingSystem.IsLinux())
        {
            return 0.0f;
        }

        lock (X11RawScrollLock)
        {
            if (!TryEnsureX11RawScroll())
            {
                return 0.0f;
            }

            int pending = X11Native.XPending(_x11RawScrollDisplay);
            if (pending <= 0)
            {
                return 0.0f;
            }

            bool canUseScroll = TryIsGlobalCursorOverVisiblePixel(window, out bool isVisible) && isVisible;
            int scrollSteps = 0;
            int eventCount = Math.Min(pending, X11Native.MaxRawScrollEventsPerFrame);
            for (int i = 0; i < eventCount; i++)
            {
                if (!TryReadNextX11RawScrollStep(out int step))
                {
                    continue;
                }

                if (canUseScroll)
                {
                    scrollSteps += step;
                }
            }

            return scrollSteps;
        }
    }

    private static double TryGetMacMainScreenHeight()
    {
        IntPtr nsScreen = MacNative.objc_getClass("NSScreen");
        if (nsScreen == IntPtr.Zero)
        {
            return 0.0;
        }

        IntPtr mainScreen = MacNative.objc_msgSend(nsScreen, MacNative.sel_registerName("mainScreen"));
        if (mainScreen == IntPtr.Zero)
        {
            return 0.0;
        }

        MacRect frame = MacNative.objc_msgSend_MacRect(mainScreen, MacNative.sel_registerName("frame"));
        return Math.Max(frame.Height, 1.0);
    }

    private static void SyncWindowsClickThroughRegion(IWindow window, byte[] framebufferRgba, int width, int height)
    {
        IntPtr hwnd = TryGetWindowsHwnd(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        WindowsClickThroughState state = EnsureWindowsClickThroughState(hwnd);
        if (!state.Enabled)
        {
            state.Enabled = true;
            state.LastWidth = 0;
            state.LastHeight = 0;
            ClearWindowsTransparentStyle(hwnd);
        }

        state.FramebufferBytes = framebufferRgba;

        IntPtr region = BuildWindowsAlphaRegion(state.FramebufferBytes, width, height);
        if (region == IntPtr.Zero)
        {
            region = WindowsNative.CreateRectRgn(0, 0, width, height);
        }

        if (WindowsNative.SetWindowRgn(hwnd, region, true) == 0)
        {
            WindowsNative.DeleteObject(region);
            return;
        }

        state.LastWidth = width;
        state.LastHeight = height;
    }

    private static void TryEnableWindowsClickThrough(IWindow window, bool enabled)
    {
        IntPtr hwnd = TryGetWindowsHwnd(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        WindowsClickThroughState state = EnsureWindowsClickThroughState(hwnd);
        state.Enabled = enabled;
        state.LastWidth = 0;
        state.LastHeight = 0;
        ClearWindowsTransparentStyle(hwnd);
        _ = WindowsNative.SetWindowRgn(hwnd, IntPtr.Zero, true);
    }

    private static void ClearWindowsTransparentStyle(IntPtr hwnd)
    {
        IntPtr current = WindowsNative.GetWindowLongPtr(hwnd, WindowsNative.GwlExStyle);
        long exStyle = current.ToInt64();
        long next = exStyle & ~WindowsNative.WsExTransparent;
        if (next != exStyle)
        {
            WindowsNative.SetWindowLongPtr(hwnd, WindowsNative.GwlExStyle, new IntPtr(next));
        }

        WindowsNative.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            WindowsNative.SwpNomove
            | WindowsNative.SwpNosize
            | WindowsNative.SwpNozorder
            | WindowsNative.SwpNoactivate
            | WindowsNative.SwpFrameChanged);
    }

    public static IntPtr TryGetWindowsHwnd(IWindow window)
    {
        IntPtr glfwWindow = TryGetGlfwWindowPointer(window);
        if (glfwWindow == IntPtr.Zero)
        {
            Console.WriteLine("[DesktopSprite] Failed to locate GLFW window pointer on Windows.");
            return IntPtr.Zero;
        }

        IntPtr hwnd = WindowsNative.GetWin32Window(glfwWindow);
        if (hwnd == IntPtr.Zero)
        {
            Console.WriteLine("[DesktopSprite] Failed to get Win32 HWND from GLFW window.");
        }

        return hwnd;
    }

    private static void SyncX11ClickThroughRegion(IWindow window, byte[] framebufferRgba, int width, int height)
    {
        if (!TryGetX11Handles(window, out IntPtr display, out nint windowHandle, logFailure: true))
        {
            return;
        }

        X11ClickThroughState state = EnsureX11ClickThroughState(windowHandle);
        state.Enabled = true;
        state.FramebufferBytes = framebufferRgba;

        IntPtr region = BuildX11AlphaRegion(state.FramebufferBytes, width, height);
        if (region == IntPtr.Zero)
        {
            return;
        }

        X11Native.XShapeCombineRegion(display, windowHandle, X11Native.ShapeBounding, 0, 0, region, X11Native.ShapeSet);
        X11Native.XShapeCombineRegion(display, windowHandle, X11Native.ShapeInput, 0, 0, region, X11Native.ShapeSet);
        X11Native.XDestroyRegion(region);
        X11Native.XFlush(display);
        state.LastWidth = width;
        state.LastHeight = height;
    }

    private static bool TryHitTestX11VisiblePixel(IWindow window, System.Numerics.Vector2 globalPosition, out bool isVisible)
    {
        isVisible = false;

        if (!TryGetX11Handles(window, out _, out nint windowHandle, logFailure: false))
        {
            return false;
        }

        X11ClickThroughState? state;
        lock (X11ClickThroughStates)
        {
            _ = X11ClickThroughStates.TryGetValue(windowHandle, out state);
        }

        if (state is null || !state.Enabled || state.LastWidth <= 0 || state.LastHeight <= 0)
        {
            return false;
        }

        byte[] pixels = state.FramebufferBytes;
        int framebufferWidth = state.LastWidth;
        int framebufferHeight = state.LastHeight;
        if (pixels.Length < framebufferWidth * framebufferHeight * 4)
        {
            return false;
        }

        float localX = globalPosition.X - window.Position.X;
        float localY = globalPosition.Y - window.Position.Y;
        int windowWidth = Math.Max(window.Size.X, 1);
        int windowHeight = Math.Max(window.Size.Y, 1);
        if (localX < 0.0f || localY < 0.0f || localX >= windowWidth || localY >= windowHeight)
        {
            isVisible = false;
            return true;
        }

        int pixelX = Math.Clamp((int)MathF.Floor(localX * framebufferWidth / windowWidth), 0, framebufferWidth - 1);
        int windowPixelY = Math.Clamp((int)MathF.Floor(localY * framebufferHeight / windowHeight), 0, framebufferHeight - 1);
        int sourceY = framebufferHeight - 1 - windowPixelY;
        int alphaIndex = ((sourceY * framebufferWidth) + pixelX) * 4 + 3;
        isVisible = pixels[alphaIndex] >= X11RegionAlphaThreshold;
        return true;
    }

    private static bool TryEnsureX11RawScroll()
    {
        if (_x11RawScrollInitialized)
        {
            return true;
        }

        if (_x11RawScrollUnavailable)
        {
            return false;
        }

        try
        {
            IntPtr display = X11Native.XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
            {
                _x11RawScrollUnavailable = true;
                return false;
            }

            if (X11Native.XQueryExtension(display, X11Native.XInputExtensionName, out int opcode, out _, out _) == 0)
            {
                _x11RawScrollUnavailable = true;
                return false;
            }

            int major = 2;
            int minor = 0;
            if (X11Native.XIQueryVersion(display, ref major, ref minor) != X11Native.Success)
            {
                _x11RawScrollUnavailable = true;
                return false;
            }

            nint root = X11Native.XDefaultRootWindow(display);
            if (root == 0)
            {
                _x11RawScrollUnavailable = true;
                return false;
            }

            byte* maskBytes = stackalloc byte[X11Native.XIEventMaskBytes];
            for (int i = 0; i < X11Native.XIEventMaskBytes; i++)
            {
                maskBytes[i] = 0;
            }

            SetXInputEventMask(maskBytes, X11Native.XIRawButtonPress);
            X11XIEventMask eventMask = new()
            {
                DeviceId = X11Native.XIAllMasterDevices,
                MaskLen = X11Native.XIEventMaskBytes,
                Mask = (IntPtr)maskBytes
            };

            if (X11Native.XISelectEvents(display, root, (IntPtr)(&eventMask), 1) != X11Native.Success)
            {
                _x11RawScrollUnavailable = true;
                return false;
            }

            X11Native.XFlush(display);
            _x11RawScrollDisplay = display;
            _x11RawScrollOpcode = opcode;
            _x11RawScrollInitialized = true;
            return true;
        }
        catch (Exception ex) when (IsNativeBindingFailure(ex))
        {
            _x11RawScrollUnavailable = true;
            return false;
        }
    }

    private static bool TryReadNextX11RawScrollStep(out int step)
    {
        step = 0;

        byte* eventBytes = stackalloc byte[X11Native.XEventBufferSize];
        for (int i = 0; i < X11Native.XEventBufferSize; i++)
        {
            eventBytes[i] = 0;
        }

        if (X11Native.XNextEvent(_x11RawScrollDisplay, (IntPtr)eventBytes) != X11Native.Success)
        {
            return false;
        }

        X11GenericEventCookie cookie = Marshal.PtrToStructure<X11GenericEventCookie>((IntPtr)eventBytes);
        if (cookie.Type != X11Native.GenericEvent)
        {
            return false;
        }

        if (X11Native.XGetEventData(_x11RawScrollDisplay, (IntPtr)eventBytes) == 0)
        {
            return false;
        }

        try
        {
            cookie = Marshal.PtrToStructure<X11GenericEventCookie>((IntPtr)eventBytes);
            if (cookie.Extension != _x11RawScrollOpcode
                || cookie.EvType != X11Native.XIRawButtonPress
                || cookie.Data == IntPtr.Zero)
            {
                return false;
            }

            X11RawEventHeader rawEvent = Marshal.PtrToStructure<X11RawEventHeader>(cookie.Data);
            step = rawEvent.Detail switch
            {
                X11Native.X11WheelUpButton => 1,
                X11Native.X11WheelDownButton => -1,
                _ => 0
            };
            return step != 0;
        }
        finally
        {
            X11Native.XFreeEventData(_x11RawScrollDisplay, (IntPtr)eventBytes);
        }
    }

    private static void SetXInputEventMask(byte* maskBytes, int eventType)
    {
        maskBytes[eventType >> 3] |= (byte)(1 << (eventType & 7));
    }

    private static void SyncMacClickThroughRegion(IWindow window, byte[] framebufferRgba, int width, int height)
    {
        IntPtr glfwWindow = TryGetGlfwWindowPointer(window);
        if (glfwWindow == IntPtr.Zero)
        {
            Console.WriteLine("[DesktopSprite] Failed to locate GLFW window pointer on macOS.");
            return;
        }

        IntPtr cocoaWindow = MacNative.GetCocoaWindow(glfwWindow);
        if (cocoaWindow == IntPtr.Zero)
        {
            Console.WriteLine("[DesktopSprite] Failed to get Cocoa NSWindow from GLFW window.");
            return;
        }

        MacClickThroughState state = EnsureMacClickThroughState(cocoaWindow);
        state.Enabled = true;
        state.FramebufferBytes = framebufferRgba;
        BuildAlphaMask(state, width, height);
    }

    private static void TryEnableMacClickThrough(IWindow window, bool enabled)
    {
        IntPtr glfwWindow = TryGetGlfwWindowPointer(window);
        if (glfwWindow == IntPtr.Zero)
        {
            Console.WriteLine("[DesktopSprite] Failed to locate GLFW window pointer on macOS.");
            return;
        }

        IntPtr cocoaWindow = MacNative.GetCocoaWindow(glfwWindow);
        if (cocoaWindow == IntPtr.Zero)
        {
            Console.WriteLine("[DesktopSprite] Failed to get Cocoa NSWindow from GLFW window.");
            return;
        }

        MacClickThroughState state = EnsureMacClickThroughState(cocoaWindow);
        state.Enabled = enabled;
        if (!enabled)
        {
            state.Width = 0;
            state.Height = 0;
            state.AlphaMask = [];
        }
    }

    private static void TryEnableX11ClickThrough(IWindow window, bool enabled)
    {
        if (!TryGetX11Handles(window, out IntPtr display, out nint windowHandle, logFailure: enabled))
        {
            return;
        }

        X11ClickThroughState state = EnsureX11ClickThroughState(windowHandle);
        state.Enabled = enabled;
        if (!enabled)
        {
            state.LastWidth = 0;
            state.LastHeight = 0;
            X11Native.XShapeCombineMask(display, windowHandle, X11Native.ShapeBounding, 0, 0, IntPtr.Zero, X11Native.ShapeSet);
            X11Native.XShapeCombineMask(display, windowHandle, X11Native.ShapeInput, 0, 0, IntPtr.Zero, X11Native.ShapeSet);
        }

        X11Native.XFlush(display);
    }

    private static IntPtr TryGetGlfwWindowPointer(IWindow window)
    {
        IntPtr handle = window.Handle;
        return handle != IntPtr.Zero
            ? handle
            : TryFindGlfwWindowPointer(window, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
    }

    private static bool TryGetX11Handles(IWindow window, out IntPtr display, out nint windowHandle, bool logFailure)
    {
        display = IntPtr.Zero;
        windowHandle = 0;

        if (_x11NativeUnavailable)
        {
            return false;
        }

        IntPtr glfwWindow = TryGetGlfwWindowPointer(window);
        if (glfwWindow == IntPtr.Zero)
        {
            if (logFailure)
            {
                LogOnce("linux-glfw-window-missing", "[DesktopSprite] Failed to locate GLFW window pointer on Linux; Linux click-through is disabled for this window.");
            }

            return false;
        }

        if (!_glfwX11WindowUnavailable)
        {
            try
            {
                windowHandle = X11Native.GetX11Window(glfwWindow);
            }
            catch (Exception ex) when (IsNativeBindingFailure(ex))
            {
                _glfwX11WindowUnavailable = true;
                if (logFailure)
                {
                    LogOnce("linux-x11-native-unavailable", $"[DesktopSprite] X11/GLFW native functions are unavailable: {ex.Message}");
                }

                windowHandle = 0;
            }
        }

        if (!TryGetX11Display(out display, allowFallbackOpenDisplay: true, logFailure))
        {
            return false;
        }

        if (windowHandle == 0)
        {
            if (TryFindCurrentProcessX11Window(display, out nint fallbackWindow))
            {
                windowHandle = fallbackWindow;
                _fallbackX11Window = fallbackWindow;
                if (logFailure)
                {
                    LogOnce("linux-x11-window-fallback-ready", $"[DesktopSprite] X11 click-through fallback window found by process id. glfwWindow=0x{glfwWindow.ToInt64():X}, x11Window=0x{windowHandle:X}.");
                }

                return true;
            }

            if (logFailure)
            {
                string sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? string.Empty;
                string displayName = Environment.GetEnvironmentVariable("DISPLAY") ?? string.Empty;
                string waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? string.Empty;
                LogOnce(
                    "linux-x11-window-missing",
                    $"[DesktopSprite] GLFW did not expose an X11 window handle; Linux transparent click-through is disabled. glfwWindow=0x{glfwWindow.ToInt64():X}, session={sessionType}, DISPLAY={displayName}, WAYLAND_DISPLAY={waylandDisplay}. If this is a Wayland session, run under X11 or set GLFW_PLATFORM=x11 before starting GamePlayer.");
            }

            return false;
        }

        if (logFailure)
        {
            LogOnce("linux-x11-handles-ready", $"[DesktopSprite] X11 click-through handles ready. glfwWindow=0x{glfwWindow.ToInt64():X}, x11Window=0x{windowHandle:X}.");
        }

        return true;
    }

    private static bool TryFindCurrentProcessX11Window(IntPtr display, out nint windowHandle)
    {
        windowHandle = 0;
        if (_fallbackX11Window != 0)
        {
            windowHandle = _fallbackX11Window;
            return true;
        }

        try
        {
            nint root = X11Native.XDefaultRootWindow(display);
            if (root == 0)
            {
                return false;
            }

            nint pidAtom = X11Native.XInternAtom(display, "_NET_WM_PID", true);
            if (pidAtom == 0)
            {
                return false;
            }

            int currentProcessId = Environment.ProcessId;
            return TryFindCurrentProcessTopLevelX11Window(display, root, pidAtom, currentProcessId, out windowHandle)
                || TryFindCurrentProcessX11WindowRecursive(display, root, pidAtom, currentProcessId, 0, out windowHandle);
        }
        catch (Exception ex) when (IsNativeBindingFailure(ex))
        {
            _x11NativeUnavailable = true;
            LogOnce("linux-x11-native-unavailable", $"[DesktopSprite] X11 native functions are unavailable: {ex.Message}");
            return false;
        }
    }

    private static bool TryFindCurrentProcessX11WindowRecursive(
        IntPtr display,
        nint window,
        nint pidAtom,
        int currentProcessId,
        int depth,
        out nint match)
    {
        match = 0;
        if (depth > 8)
        {
            return false;
        }

        if (TryGetX11WindowPid(display, window, pidAtom, out int pid) && pid == currentProcessId)
        {
            match = window;
            return true;
        }

        if (X11Native.XQueryTree(display, window, out _, out _, out IntPtr children, out uint childCount) == 0)
        {
            return false;
        }

        try
        {
            for (uint i = 0; i < childCount; i++)
            {
                nint child = Marshal.ReadIntPtr(children, checked((int)(i * (uint)IntPtr.Size)));
                if (TryFindCurrentProcessX11WindowRecursive(display, child, pidAtom, currentProcessId, depth + 1, out match))
                {
                    return true;
                }
            }
        }
        finally
        {
            if (children != IntPtr.Zero)
            {
                X11Native.XFree(children);
            }
        }

        return false;
    }

    private static bool TryFindCurrentProcessTopLevelX11Window(
        IntPtr display,
        nint root,
        nint pidAtom,
        int currentProcessId,
        out nint match)
    {
        match = 0;
        if (X11Native.XQueryTree(display, root, out _, out _, out IntPtr children, out uint childCount) == 0)
        {
            return false;
        }

        try
        {
            for (uint i = 0; i < childCount; i++)
            {
                nint child = Marshal.ReadIntPtr(children, checked((int)(i * (uint)IntPtr.Size)));
                if (TryGetX11WindowPid(display, child, pidAtom, out int pid) && pid == currentProcessId)
                {
                    match = child;
                    return true;
                }
            }
        }
        finally
        {
            if (children != IntPtr.Zero)
            {
                X11Native.XFree(children);
            }
        }

        return false;
    }

    private static bool TryGetX11WindowPid(IntPtr display, nint window, nint pidAtom, out int pid)
    {
        pid = 0;
        int result = X11Native.XGetWindowProperty(
            display,
            window,
            pidAtom,
            0,
            1,
            false,
            X11Native.AnyPropertyType,
            out _,
            out int actualFormat,
            out ulong itemCount,
            out _,
            out IntPtr property);

        if (result != X11Native.Success || property == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (actualFormat != 32 || itemCount == 0)
            {
                return false;
            }

            pid = Marshal.ReadInt32(property);
            return pid > 0;
        }
        finally
        {
            X11Native.XFree(property);
        }
    }

    private static bool TryGetX11Display(out IntPtr display, bool allowFallbackOpenDisplay, bool logFailure)
    {
        display = IntPtr.Zero;
        if (_x11NativeUnavailable)
        {
            return false;
        }

        if (!_glfwX11DisplayUnavailable)
        {
            try
            {
                display = X11Native.GetX11Display();
            }
            catch (Exception ex) when (IsNativeBindingFailure(ex))
            {
                _glfwX11DisplayUnavailable = true;
                if (logFailure && !allowFallbackOpenDisplay)
                {
                    LogOnce("linux-x11-native-unavailable", $"[DesktopSprite] X11/GLFW native functions are unavailable: {ex.Message}");
                }

                display = IntPtr.Zero;
            }
        }

        if (display != IntPtr.Zero)
        {
            return true;
        }

        if (!allowFallbackOpenDisplay)
        {
            if (logFailure)
            {
                LogOnce("linux-x11-display-missing", "[DesktopSprite] GLFW did not expose an X11 display handle.");
            }

            return false;
        }

        if (_fallbackX11Display != IntPtr.Zero)
        {
            display = _fallbackX11Display;
            return true;
        }

        try
        {
            _fallbackX11Display = X11Native.XOpenDisplay(IntPtr.Zero);
        }
        catch (Exception ex) when (IsNativeBindingFailure(ex))
        {
            _x11NativeUnavailable = true;
            if (logFailure)
            {
                LogOnce("linux-x11-native-unavailable", $"[DesktopSprite] X11 native functions are unavailable: {ex.Message}");
            }

            return false;
        }

        display = _fallbackX11Display;
        if (display != IntPtr.Zero)
        {
            return true;
        }

        if (logFailure)
        {
            string displayName = Environment.GetEnvironmentVariable("DISPLAY") ?? string.Empty;
            LogOnce("linux-x11-display-missing", $"[DesktopSprite] Failed to open X11 display; Linux click-through and global cursor fallback are disabled. DISPLAY={displayName}");
        }

        return false;
    }

    private static bool IsNativeBindingFailure(Exception ex)
    {
        return ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;
    }

    private static void LogOnce(string key, string message)
    {
        lock (LogLock)
        {
            if (!LoggedMessages.Add(key))
            {
                return;
            }
        }

        Console.WriteLine(message);
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        _ = assembly;
        _ = searchPath;

        if (!IsGlfwLibraryName(libraryName))
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

    private static bool IsGlfwLibraryName(string libraryName)
    {
        return libraryName.Equals("glfw3", StringComparison.OrdinalIgnoreCase)
            || libraryName.Equals("glfw3.dll", StringComparison.OrdinalIgnoreCase)
            || libraryName.Equals("libglfw.so.3", StringComparison.OrdinalIgnoreCase)
            || libraryName.Equals("libglfw.3.dylib", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetBundledGlfwCandidates()
    {
        string fileName = OperatingSystem.IsWindows()
            ? "glfw3.dll"
            : OperatingSystem.IsMacOS() ? "libglfw.3.dylib" : "libglfw.so.3";

        string baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, fileName);

        string? rid = GetNativeRuntimeIdentifier();
        if (!string.IsNullOrWhiteSpace(rid))
        {
            yield return Path.Combine(baseDirectory, "runtimes", rid, "native", fileName);
        }
    }

    private static string? GetNativeRuntimeIdentifier()
    {
        Architecture architecture = RuntimeInformation.ProcessArchitecture;
        if (OperatingSystem.IsWindows())
        {
            return architecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.X86 => "win-x86",
                Architecture.Arm64 => "win-arm64",
                _ => null
            };
        }

        if (OperatingSystem.IsLinux())
        {
            return architecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                Architecture.Arm => "linux-arm",
                _ => null
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return architecture switch
            {
                Architecture.X64 => "osx-x64",
                Architecture.Arm64 => "osx-arm64",
                _ => null
            };
        }

        return null;
    }

    private static WindowsClickThroughState EnsureWindowsClickThroughState(IntPtr hwnd)
    {
        lock (WindowsClickThroughStates)
        {
            if (WindowsClickThroughStates.TryGetValue(hwnd, out WindowsClickThroughState? state))
            {
                return state;
            }

            state = new WindowsClickThroughState();
            WindowsClickThroughStates[hwnd] = state;
            return state;
        }
    }

    private static X11ClickThroughState EnsureX11ClickThroughState(nint windowHandle)
    {
        lock (X11ClickThroughStates)
        {
            if (X11ClickThroughStates.TryGetValue(windowHandle, out X11ClickThroughState? state))
            {
                return state;
            }

            state = new X11ClickThroughState();
            X11ClickThroughStates[windowHandle] = state;
            return state;
        }
    }

    private static MacClickThroughState EnsureMacClickThroughState(IntPtr cocoaWindow)
    {
        IntPtr contentView = MacNative.objc_msgSend(cocoaWindow, MacNative.sel_registerName("contentView"));
        if (contentView == IntPtr.Zero)
        {
            return new MacClickThroughState(IntPtr.Zero, IntPtr.Zero);
        }

        lock (MacClickThroughStatesByView)
        {
            if (MacClickThroughStatesByView.TryGetValue(contentView, out MacClickThroughState? state))
            {
                return state;
            }

            IntPtr originalClass = MacNative.object_getClass(contentView);
            IntPtr subclass = EnsureMacHitTestViewClass(originalClass);
            if (subclass != IntPtr.Zero)
            {
                _ = MacNative.object_setClass(contentView, subclass);
            }

            state = new MacClickThroughState(contentView, originalClass);
            MacClickThroughStatesByView[contentView] = state;
            return state;
        }
    }

    private static IntPtr BuildWindowsAlphaRegion(byte[] pixels, int width, int height)
    {
        IntPtr result = WindowsNative.CreateRectRgn(0, 0, 0, 0);
        if (result == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr rowRegion = IntPtr.Zero;
        bool hasVisiblePixels = false;
        int step = Math.Max(RegionSampleStep, 1);

        for (int sourceY = height - 1; sourceY >= 0; sourceY -= step)
        {
            int windowY = height - 1 - sourceY;
            int rowEndY = Math.Min(windowY + step, height);
            int runStart = -1;

            for (int x = 0; x < width; x += step)
            {
                if (HasVisibleAlphaInBlock(pixels, width, height, x, sourceY, step))
                {
                    if (runStart < 0)
                    {
                        runStart = x;
                    }

                    continue;
                }

                if (runStart >= 0)
                {
                    rowRegion = AddWindowsRegionRun(result, rowRegion, runStart, windowY, x, rowEndY);
                    hasVisiblePixels = true;
                    runStart = -1;
                }
            }

            if (runStart >= 0)
            {
                rowRegion = AddWindowsRegionRun(result, rowRegion, runStart, windowY, width, rowEndY);
                hasVisiblePixels = true;
            }
        }

        if (rowRegion != IntPtr.Zero)
        {
            WindowsNative.DeleteObject(rowRegion);
        }

        if (!hasVisiblePixels)
        {
            return result;
        }

        return result;
    }

    private static IntPtr BuildX11AlphaRegion(byte[] pixels, int width, int height)
    {
        IntPtr region = X11Native.XCreateRegion();
        if (region == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        int step = Math.Max(X11RegionSampleStep, 1);
        for (int sourceY = height - 1; sourceY >= 0; sourceY -= step)
        {
            int windowY = height - 1 - sourceY;
            ushort rowHeight = (ushort)Math.Min(step, height - windowY);
            int runStart = -1;

            for (int x = 0; x < width; x += step)
            {
                if (HasVisibleAlphaInBlock(pixels, width, height, x, sourceY, step, X11RegionAlphaThreshold))
                {
                    if (runStart < 0)
                    {
                        runStart = x;
                    }

                    continue;
                }

                if (runStart >= 0)
                {
                    AddX11RegionRun(region, runStart, windowY, x - runStart, rowHeight);
                    runStart = -1;
                }
            }

            if (runStart >= 0)
            {
                AddX11RegionRun(region, runStart, windowY, width - runStart, rowHeight);
            }
        }

        return region;
    }

    private static void AddX11RegionRun(IntPtr region, int x, int y, int width, ushort height)
    {
        if (width <= 0 || height == 0)
        {
            return;
        }

        X11Rectangle rectangle = new()
        {
            X = (short)Math.Clamp(x, short.MinValue, short.MaxValue),
            Y = (short)Math.Clamp(y, short.MinValue, short.MaxValue),
            Width = (ushort)Math.Clamp(width, 0, ushort.MaxValue),
            Height = height
        };
        X11Native.XUnionRectWithRegion(ref rectangle, region, region);
    }

    private static void BuildAlphaMask(MacClickThroughState state, int width, int height)
    {
        int length = checked(width * height);
        if (state.AlphaMask.Length != length)
        {
            state.AlphaMask = new bool[length];
        }

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width * 4;
            int maskRow = (height - 1 - y) * width;
            for (int x = 0; x < width; x++)
            {
                state.AlphaMask[maskRow + x] = state.FramebufferBytes[rowOffset + (x * 4) + 3] >= RegionAlphaThreshold;
            }
        }

        state.Width = width;
        state.Height = height;
    }

    private static IntPtr AddWindowsRegionRun(IntPtr result, IntPtr reusableRegion, int left, int top, int right, int bottom)
    {
        if (reusableRegion != IntPtr.Zero)
        {
            WindowsNative.SetRectRgn(reusableRegion, left, top, right, bottom);
        }
        else
        {
            reusableRegion = WindowsNative.CreateRectRgn(left, top, right, bottom);
        }

        if (reusableRegion != IntPtr.Zero)
        {
            _ = WindowsNative.CombineRgn(result, result, reusableRegion, WindowsNative.RgnOr);
        }

        return reusableRegion;
    }

    private static bool HasVisibleAlphaInBlock(byte[] pixels, int width, int height, int startX, int startY, int step, byte threshold = RegionAlphaThreshold)
    {
        int endX = Math.Min(startX + step, width);
        int endY = Math.Max(startY - step + 1, 0);
        for (int y = startY; y >= endY; y--)
        {
            int rowOffset = y * width * 4;
            for (int x = startX; x < endX; x++)
            {
                if (pixels[rowOffset + (x * 4) + 3] >= threshold)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IntPtr EnsureMacHitTestViewClass(IntPtr originalClass)
    {
        if (originalClass == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        string className = $"{MacNative.HitTestViewClassName}_{originalClass.ToInt64():X}";
        IntPtr existing = MacNative.objc_getClass(className);
        if (existing != IntPtr.Zero)
        {
            return existing;
        }

        IntPtr subclass = MacNative.objc_allocateClassPair(originalClass, className, 0);
        if (subclass == IntPtr.Zero)
        {
            return MacNative.objc_getClass(className);
        }

        IntPtr selector = MacNative.sel_registerName("hitTest:");
        IntPtr implementation = Marshal.GetFunctionPointerForDelegate(MacHitTestCallback);
        _ = MacNative.class_addMethod(subclass, selector, implementation, "@@:{CGPoint=dd}");
        MacNative.objc_registerClassPair(subclass);
        return subclass;
    }

    private static IntPtr MacHitTest(IntPtr self, IntPtr selector, MacPoint point)
    {
        _ = selector;

        MacClickThroughState? state;
        lock (MacClickThroughStatesByView)
        {
            _ = MacClickThroughStatesByView.TryGetValue(self, out state);
        }

        if (state is null)
        {
            IntPtr currentClass = MacNative.object_getClass(self);
            IntPtr superClass = currentClass == IntPtr.Zero ? IntPtr.Zero : MacNative.class_getSuperclass(currentClass);
            return MacHitTestSuper(self, superClass, point);
        }

        if (!state.Enabled || state.Width <= 0 || state.Height <= 0 || state.AlphaMask.Length == 0)
        {
            return MacHitTestSuper(self, state.OriginalClass, point);
        }

        int x = (int)Math.Floor(point.X);
        int y = state.Height - 1 - (int)Math.Floor(point.Y);
        if (x < 0 || y < 0 || x >= state.Width || y >= state.Height)
        {
            return IntPtr.Zero;
        }

        return state.AlphaMask[(y * state.Width) + x]
            ? MacHitTestSuper(self, state.OriginalClass, point)
            : IntPtr.Zero;
    }

    private static IntPtr MacHitTestSuper(IntPtr self, IntPtr originalClass, MacPoint point)
    {
        if (originalClass == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        MacSuper super = new(self, originalClass);
        return MacNative.objc_msgSendSuper_HitTest(ref super, MacNative.sel_registerName("hitTest:"), point);
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

    private static class WindowsNative
    {
        internal const int VkLButton = 0x01;
        internal const int VkRButton = 0x02;
        internal const int VkMButton = 0x04;
        internal const int GwlExStyle = -20;
        internal const long WsExTransparent = 0x20L;
        internal const uint SwpNosize = 0x0001;
        internal const uint SwpNomove = 0x0002;
        internal const uint SwpNozorder = 0x0004;
        internal const uint SwpNoactivate = 0x0010;
        internal const uint SwpFrameChanged = 0x0020;
        internal const int RgnOr = 2;

        [DllImport("glfw3", EntryPoint = "glfwGetWin32Window", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr GetWin32Window(IntPtr window);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "SetWindowRgn")]
        internal static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("gdi32.dll", EntryPoint = "CreateRectRgn")]
        internal static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

        [DllImport("gdi32.dll", EntryPoint = "SetRectRgn")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetRectRgn(IntPtr hrgn, int left, int top, int right, int bottom);

        [DllImport("gdi32.dll", EntryPoint = "CombineRgn")]
        internal static extern int CombineRgn(IntPtr hrgnDst, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int iMode);

        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out WindowsPoint point);

        [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
        internal static extern short GetAsyncKeyState(int virtualKey);
    }

    private static class X11Native
    {
        internal const int GenericEvent = 35;
        internal const int Success = 0;
        internal const nint AnyPropertyType = 0;
        internal const int ShapeSet = 0;
        internal const int ShapeBounding = 0;
        internal const int ShapeInput = 2;
        internal const uint Button1Mask = 1 << 8;
        internal const uint Button2Mask = 1 << 9;
        internal const uint Button3Mask = 1 << 10;
        internal const string XInputExtensionName = "XInputExtension";
        internal const int XIAllMasterDevices = 1;
        internal const int XIRawButtonPress = 15;
        internal const int XIEventMaskBytes = 4;
        internal const int X11WheelUpButton = 4;
        internal const int X11WheelDownButton = 5;
        internal const int XEventBufferSize = 192;
        internal const int MaxRawScrollEventsPerFrame = 128;

        [DllImport("libglfw.so.3", EntryPoint = "glfwGetX11Display", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr GetX11Display();

        [DllImport("libglfw.so.3", EntryPoint = "glfwGetX11Window", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint GetX11Window(IntPtr window);

        [DllImport("libX11.so.6")]
        internal static extern IntPtr XCreateRegion();

        [DllImport("libX11.so.6")]
        internal static extern int XDestroyRegion(IntPtr region);

        [DllImport("libX11.so.6")]
        internal static extern int XUnionRectWithRegion(ref X11Rectangle rectangle, IntPtr sourceRegion, IntPtr destinationRegion);

        [DllImport("libX11.so.6")]
        internal static extern int XPending(IntPtr display);

        [DllImport("libX11.so.6")]
        internal static extern int XNextEvent(IntPtr display, IntPtr eventReturn);

        [DllImport("libX11.so.6")]
        internal static extern int XGetEventData(IntPtr display, IntPtr eventCookie);

        [DllImport("libX11.so.6")]
        internal static extern void XFreeEventData(IntPtr display, IntPtr eventCookie);

        [DllImport("libX11.so.6")]
        internal static extern int XQueryExtension(
            IntPtr display,
            string name,
            out int majorOpcodeReturn,
            out int firstEventReturn,
            out int firstErrorReturn);

        [DllImport("libX11.so.6")]
        internal static extern int XFlush(IntPtr display);

        [DllImport("libX11.so.6")]
        internal static extern nint XInternAtom(IntPtr display, string atomName, bool onlyIfExists);

        [DllImport("libX11.so.6")]
        internal static extern int XQueryTree(
            IntPtr display,
            nint window,
            out nint rootReturn,
            out nint parentReturn,
            out IntPtr childrenReturn,
            out uint childCountReturn);

        [DllImport("libX11.so.6")]
        internal static extern int XGetWindowProperty(
            IntPtr display,
            nint window,
            nint property,
            nint longOffset,
            nint longLength,
            bool delete,
            nint requiredType,
            out nint actualTypeReturn,
            out int actualFormatReturn,
            out ulong itemCountReturn,
            out ulong bytesAfterReturn,
            out IntPtr propertyReturn);

        [DllImport("libX11.so.6")]
        internal static extern int XFree(IntPtr data);

        [DllImport("libXext.so.6")]
        internal static extern void XShapeCombineRegion(IntPtr display, nint window, int destKind, int xOff, int yOff, IntPtr region, int op);

        [DllImport("libX11.so.6")]
        internal static extern nint XDefaultRootWindow(IntPtr display);

        [DllImport("libX11.so.6")]
        internal static extern IntPtr XOpenDisplay(IntPtr displayName);

        [DllImport("libX11.so.6")]
        internal static extern int XQueryPointer(
            IntPtr display,
            nint window,
            out nint rootReturn,
            out nint childReturn,
            out int rootXReturn,
            out int rootYReturn,
            out int winXReturn,
            out int winYReturn,
            out uint maskReturn);

        [DllImport("libXext.so.6")]
        internal static extern void XShapeCombineMask(IntPtr display, nint window, int destKind, int xOff, int yOff, IntPtr bitmap, int op);

        [DllImport("libXi.so.6")]
        internal static extern int XIQueryVersion(IntPtr display, ref int majorVersionInOut, ref int minorVersionInOut);

        [DllImport("libXi.so.6")]
        internal static extern int XISelectEvents(IntPtr display, nint window, IntPtr masks, int numMasks);
    }

    private static class MacNative
    {
        internal const string HitTestViewClassName = "ZhengyanDigitalWifeDesktopSpriteHitTestView";

        [DllImport("libglfw.3.dylib", EntryPoint = "glfwGetCocoaWindow", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr GetCocoaWindow(IntPtr window);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
        internal static extern IntPtr sel_registerName(string selectorName);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        internal static extern void objc_msgSend_bool(IntPtr receiver, IntPtr selector, bool value);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        internal static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        internal static extern MacPoint objc_msgSend_MacPoint(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        internal static extern MacRect objc_msgSend_MacRect(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSendSuper")]
        internal static extern IntPtr objc_msgSendSuper_HitTest(ref MacSuper receiver, IntPtr selector, MacPoint point);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
        internal static extern IntPtr objc_getClass(string className);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_allocateClassPair")]
        internal static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_registerClassPair")]
        internal static extern void objc_registerClassPair(IntPtr cls);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "class_addMethod")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool class_addMethod(IntPtr cls, IntPtr name, IntPtr imp, string types);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "object_setClass")]
        internal static extern IntPtr object_setClass(IntPtr obj, IntPtr cls);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "object_getClass")]
        internal static extern IntPtr object_getClass(IntPtr obj);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "class_getSuperclass")]
        internal static extern IntPtr class_getSuperclass(IntPtr cls);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsPoint
    {
        public int X;

        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct X11Rectangle
    {
        public short X;

        public short Y;

        public ushort Width;

        public ushort Height;
    }

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
    private struct X11RawEventHeader
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

        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MacPoint(double x, double y)
    {
        public double X { get; } = x;

        public double Y { get; } = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MacRect(double x, double y, double width, double height)
    {
        public double X { get; } = x;

        public double Y { get; } = y;

        public double Width { get; } = width;

        public double Height { get; } = height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MacSuper(IntPtr receiver, IntPtr superClass)
    {
        public IntPtr Receiver = receiver;

        public IntPtr SuperClass = superClass;
    }

    private sealed class WindowsClickThroughState
    {
        public bool Enabled { get; set; }

        public int LastWidth { get; set; }

        public int LastHeight { get; set; }

        public byte[] FramebufferBytes { get; set; } = [];
    }

    private sealed class X11ClickThroughState
    {
        public bool Enabled { get; set; }

        public int LastWidth { get; set; }

        public int LastHeight { get; set; }

        public byte[] FramebufferBytes { get; set; } = [];
    }

    private sealed class MacClickThroughState(IntPtr contentView, IntPtr originalClass)
    {
        public IntPtr ContentView { get; } = contentView;

        public IntPtr OriginalClass { get; } = originalClass;

        public bool Enabled { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public byte[] FramebufferBytes { get; set; } = [];

        public bool[] AlphaMask { get; set; } = [];
    }

    private delegate IntPtr MacHitTestDelegate(IntPtr self, IntPtr selector, MacPoint point);
}

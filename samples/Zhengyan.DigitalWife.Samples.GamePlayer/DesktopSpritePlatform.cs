using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.OpenGLES;
using Silk.NET.Windowing;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

internal static unsafe class DesktopSpritePlatform
{
    private const int RegionSampleStep = 4;
    private const byte RegionAlphaThreshold = 8;
    private static readonly Dictionary<IntPtr, WindowsClickThroughState> WindowsClickThroughStates = [];
    private static readonly Dictionary<nint, X11ClickThroughState> X11ClickThroughStates = [];
    private static readonly Dictionary<IntPtr, MacClickThroughState> MacClickThroughStatesByView = [];
    private static readonly MacHitTestDelegate MacHitTestCallback = MacHitTest;

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
            string sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? string.Empty;
            if (sessionType.Equals("wayland", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[DesktopSprite] Mouse click-through is not enabled on Wayland. X11 is required for Linux click-through.");
                return;
            }

            TryEnableX11ClickThrough(window, true);
        }
    }

    public static void SyncClickThroughRegionFromFramebuffer(IWindow window, GL gl, int width, int height, bool enabled)
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
            SyncWindowsClickThroughRegion(window, gl, width, height);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            SyncMacClickThroughRegion(window, gl, width, height);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            string sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? string.Empty;
            if (!sessionType.Equals("wayland", StringComparison.OrdinalIgnoreCase))
            {
                SyncX11ClickThroughRegion(window, gl, width, height);
            }
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
                IntPtr display = X11Native.GetX11Display();
                if (display == IntPtr.Zero)
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

    private static void SyncWindowsClickThroughRegion(IWindow window, GL gl, int width, int height)
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

        int bufferLength = checked(width * height * 4);
        if (state.FramebufferBytes.Length != bufferLength)
        {
            state.FramebufferBytes = new byte[bufferLength];
        }

        ReadFramebufferRgba(gl, state.FramebufferBytes, width, height);

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

    private static void SyncX11ClickThroughRegion(IWindow window, GL gl, int width, int height)
    {
        IntPtr glfwWindow = TryGetGlfwWindowPointer(window);
        if (glfwWindow == IntPtr.Zero)
        {
            Console.WriteLine("[DesktopSprite] Failed to locate GLFW window pointer on Linux.");
            return;
        }

        IntPtr display = X11Native.GetX11Display();
        nint windowHandle = X11Native.GetX11Window(glfwWindow);
        if (display == IntPtr.Zero || windowHandle == 0)
        {
            Console.WriteLine("[DesktopSprite] Failed to get X11 display/window handles from GLFW.");
            return;
        }

        X11ClickThroughState state = EnsureX11ClickThroughState(windowHandle);
        state.Enabled = true;
        int bufferLength = checked(width * height * 4);
        if (state.FramebufferBytes.Length != bufferLength)
        {
            state.FramebufferBytes = new byte[bufferLength];
        }

        ReadFramebufferRgba(gl, state.FramebufferBytes, width, height);

        IntPtr region = BuildX11AlphaRegion(state.FramebufferBytes, width, height);
        X11Native.XShapeCombineRegion(display, windowHandle, X11Native.ShapeInput, 0, 0, region, X11Native.ShapeSet);
        X11Native.XDestroyRegion(region);
        X11Native.XFlush(display);
        state.LastWidth = width;
        state.LastHeight = height;
    }

    private static void SyncMacClickThroughRegion(IWindow window, GL gl, int width, int height)
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
        int bufferLength = checked(width * height * 4);
        if (state.FramebufferBytes.Length != bufferLength)
        {
            state.FramebufferBytes = new byte[bufferLength];
        }

        ReadFramebufferRgba(gl, state.FramebufferBytes, width, height);
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
        IntPtr glfwWindow = TryGetGlfwWindowPointer(window);
        if (glfwWindow == IntPtr.Zero)
        {
            Console.WriteLine("[DesktopSprite] Failed to locate GLFW window pointer on Linux.");
            return;
        }

        IntPtr display = X11Native.GetX11Display();
        nint windowHandle = X11Native.GetX11Window(glfwWindow);
        if (display == IntPtr.Zero || windowHandle == 0)
        {
            Console.WriteLine("[DesktopSprite] Failed to get X11 display/window handles from GLFW.");
            return;
        }

        X11ClickThroughState state = EnsureX11ClickThroughState(windowHandle);
        state.Enabled = enabled;
        if (!enabled)
        {
            state.LastWidth = 0;
            state.LastHeight = 0;
            IntPtr fullRegion = X11Native.XCreateRegion();
            X11Native.XShapeCombineRegion(display, windowHandle, X11Native.ShapeInput, 0, 0, fullRegion, X11Native.ShapeSet);
            X11Native.XDestroyRegion(fullRegion);
        }

        X11Native.XFlush(display);
    }

    private static IntPtr TryGetGlfwWindowPointer(IWindow window)
    {
        return TryFindGlfwWindowPointer(window, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
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

    private static void ReadFramebufferRgba(GL gl, byte[] target, int width, int height)
    {
        fixed (byte* pixels = target)
        {
            gl.BindFramebuffer(GLEnum.Framebuffer, 0);
            gl.PixelStore(GLEnum.PackAlignment, 1);
            gl.ReadPixels(0, 0, (uint)width, (uint)height, GLEnum.Rgba, GLEnum.UnsignedByte, pixels);
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

        int step = Math.Max(RegionSampleStep, 1);
        for (int sourceY = height - 1; sourceY >= 0; sourceY -= step)
        {
            int windowY = height - 1 - sourceY;
            ushort rowHeight = (ushort)Math.Min(step, height - windowY);
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

    private static bool HasVisibleAlphaInBlock(byte[] pixels, int width, int height, int startX, int startY, int step)
    {
        int endX = Math.Min(startX + step, width);
        int endY = Math.Max(startY - step + 1, 0);
        for (int y = startY; y >= endY; y--)
        {
            int rowOffset = y * width * 4;
            for (int x = startX; x < endX; x++)
            {
                if (pixels[rowOffset + (x * 4) + 3] >= RegionAlphaThreshold)
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
    }

    private static class X11Native
    {
        internal const int ShapeSet = 0;
        internal const int ShapeInput = 2;

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
        internal static extern int XFlush(IntPtr display);

        [DllImport("libXext.so.6")]
        internal static extern void XShapeCombineRegion(IntPtr display, nint window, int destKind, int xOff, int yOff, IntPtr region, int op);

        [DllImport("libX11.so.6")]
        internal static extern nint XDefaultRootWindow(IntPtr display);

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

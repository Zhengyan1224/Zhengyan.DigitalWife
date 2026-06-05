using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Windowing;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

internal static unsafe class DesktopSpritePlatform
{
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
                TryApplyWindowsClickThrough(window, false);
            }
            else if (OperatingSystem.IsMacOS())
            {
                TryApplyMacClickThrough(window, false);
            }

            return;
        }

        if (OperatingSystem.IsWindows())
        {
            TryApplyWindowsClickThrough(window, true);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            TryApplyMacClickThrough(window, true);
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

            TryApplyX11ClickThrough(window);
        }
    }

    private static void TryApplyWindowsClickThrough(IWindow window, bool enabled)
    {
        IntPtr glfwWindow = TryGetGlfwWindowPointer(window);
        if (glfwWindow == IntPtr.Zero)
        {
            Console.WriteLine("[DesktopSprite] Failed to locate GLFW window pointer on Windows.");
            return;
        }

        IntPtr hwnd = WindowsNative.GetWin32Window(glfwWindow);
        if (hwnd == IntPtr.Zero)
        {
            Console.WriteLine("[DesktopSprite] Failed to get Win32 HWND from GLFW window.");
            return;
        }

        IntPtr current = WindowsNative.GetWindowLongPtr(hwnd, WindowsNative.GwlExStyle);
        long exStyle = current.ToInt64();
        long next = enabled
            ? exStyle | WindowsNative.WsExTransparent
            : exStyle & ~WindowsNative.WsExTransparent;
        WindowsNative.SetWindowLongPtr(hwnd, WindowsNative.GwlExStyle, new IntPtr(next));
    }

    private static void TryApplyMacClickThrough(IWindow window, bool enabled)
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

        IntPtr selector = MacNative.sel_registerName("setIgnoresMouseEvents:");
        MacNative.objc_msgSend_bool(cocoaWindow, selector, enabled);
    }

    private static void TryApplyX11ClickThrough(IWindow window)
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

        IntPtr emptyRegion = X11Native.XCreateRegion();
        X11Native.XShapeCombineRegion(display, windowHandle, X11Native.ShapeInput, 0, 0, emptyRegion, X11Native.ShapeSet);
        X11Native.XDestroyRegion(emptyRegion);
        X11Native.XFlush(display);
    }

    private static IntPtr TryGetGlfwWindowPointer(IWindow window)
    {
        return TryFindGlfwWindowPointer(window, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
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

        [DllImport("glfw3", EntryPoint = "glfwGetWin32Window", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr GetWin32Window(IntPtr window);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
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
        internal static extern int XFlush(IntPtr display);

        [DllImport("libXext.so.6")]
        internal static extern void XShapeCombineRegion(IntPtr display, nint window, int destKind, int xOff, int yOff, IntPtr region, int op);
    }

    private static class MacNative
    {
        [DllImport("libglfw.3.dylib", EntryPoint = "glfwGetCocoaWindow", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr GetCocoaWindow(IntPtr window);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
        internal static extern IntPtr sel_registerName(string selectorName);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        internal static extern void objc_msgSend_bool(IntPtr receiver, IntPtr selector, bool value);
    }
}

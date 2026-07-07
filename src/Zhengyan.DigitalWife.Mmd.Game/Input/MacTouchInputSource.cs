using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Windowing;

namespace Zhengyan.DigitalWife.Mmd.Game.Input;

internal sealed unsafe class MacTouchInputSource : ITouchInputSource
{
    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";
    private const nuint NSTouchPhaseBegan = 1 << 0;
    private const nuint NSTouchPhaseMoved = 1 << 1;
    private const nuint NSTouchPhaseEnded = 1 << 3;
    private const nuint NSTouchPhaseCancelled = 1 << 4;

    private static readonly object ViewStatesLock = new();
    private static readonly Dictionary<IntPtr, ViewState> ViewStates = [];
    private static readonly TouchEventDelegate TouchesBeganCallback = TouchesBegan;
    private static readonly TouchEventDelegate TouchesMovedCallback = TouchesMoved;
    private static readonly TouchEventDelegate TouchesEndedCallback = TouchesEnded;
    private static readonly TouchEventDelegate TouchesCancelledCallback = TouchesCancelled;
    private static readonly IntPtr ContentViewSelector = SelRegisterName("contentView");
    private static readonly IntPtr SetAcceptsTouchEventsSelector = SelRegisterName("setAcceptsTouchEvents:");
    private static readonly IntPtr SetWantsRestingTouchesSelector = SelRegisterName("setWantsRestingTouches:");
    private static readonly IntPtr RespondsToSelectorSelector = SelRegisterName("respondsToSelector:");
    private static readonly IntPtr TouchesMatchingPhaseInViewSelector = SelRegisterName("touchesMatchingPhase:inView:");
    private static readonly IntPtr CountSelector = SelRegisterName("count");
    private static readonly IntPtr AllObjectsSelector = SelRegisterName("allObjects");
    private static readonly IntPtr ObjectAtIndexSelector = SelRegisterName("objectAtIndex:");
    private static readonly IntPtr IdentitySelector = SelRegisterName("identity");
    private static readonly IntPtr NormalizedPositionSelector = SelRegisterName("normalizedPosition");
    private static readonly IntPtr TouchesBeganSelector = SelRegisterName("touchesBeganWithEvent:");
    private static readonly IntPtr TouchesMovedSelector = SelRegisterName("touchesMovedWithEvent:");
    private static readonly IntPtr TouchesEndedSelector = SelRegisterName("touchesEndedWithEvent:");
    private static readonly IntPtr TouchesCancelledSelector = SelRegisterName("touchesCancelledWithEvent:");

    private readonly object _lock = new();
    private readonly IWindow _window;
    private readonly List<TouchInputEvent> _events = [];
    private readonly Dictionary<IntPtr, int> _touchIds = [];
    private readonly IntPtr _contentView;
    private readonly IntPtr _originalClass;
    private readonly IntPtr _subclassClass;
    private int _nextTouchId = 1;
    private bool _disposed;

    static MacTouchInputSource()
    {
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(MacTouchInputSource).Assembly, ResolveNativeLibrary);
        }
        catch (InvalidOperationException)
        {
            // A resolver may already be installed for the engine assembly.
        }
    }

    private MacTouchInputSource(IWindow window, IntPtr contentView, IntPtr originalClass, IntPtr subclassClass)
    {
        _window = window;
        _contentView = contentView;
        _originalClass = originalClass;
        _subclassClass = subclassClass;
        IsAvailable = true;
    }

    public bool IsAvailable { get; }

    public static ITouchInputSource Create(IWindow window)
    {
        if (!OperatingSystem.IsMacOS())
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

            IntPtr cocoaWindow = GetCocoaWindow(glfwWindow);
            if (cocoaWindow == IntPtr.Zero)
            {
                return NullTouchInputSource.Instance;
            }

            IntPtr contentView = IntPtrObjCMsgSend(cocoaWindow, ContentViewSelector);
            if (contentView == IntPtr.Zero)
            {
                return NullTouchInputSource.Instance;
            }

            IntPtr originalClass = ObjectGetClass(contentView);
            if (originalClass == IntPtr.Zero)
            {
                return NullTouchInputSource.Instance;
            }

            IntPtr subclassClass = CreateTouchViewSubclass(originalClass);
            if (subclassClass == IntPtr.Zero)
            {
                return NullTouchInputSource.Instance;
            }

            MacTouchInputSource source = new(window, contentView, originalClass, subclassClass);
            lock (ViewStatesLock)
            {
                ViewStates[contentView] = new ViewState(source, originalClass);
            }

            ObjectSetClass(contentView, subclassClass);
            SetTouchAcceptance(contentView, enabled: true);
            return source;
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
        if (_contentView != IntPtr.Zero)
        {
            lock (ViewStatesLock)
            {
                _ = ViewStates.Remove(_contentView);
            }

            try
            {
                SetTouchAcceptance(_contentView, enabled: false);
                if (_originalClass != IntPtr.Zero)
                {
                    ObjectSetClass(_contentView, _originalClass);
                }
            }
            catch
            {
            }
        }
    }

    private void HandleTouchEvent(IntPtr nsEvent)
    {
        if (_disposed || nsEvent == IntPtr.Zero)
        {
            return;
        }

        ProcessPhase(nsEvent, NSTouchPhaseBegan, TouchPhase.Started);
        ProcessPhase(nsEvent, NSTouchPhaseMoved, TouchPhase.Moved);
        ProcessPhase(nsEvent, NSTouchPhaseEnded, TouchPhase.Ended);
        ProcessPhase(nsEvent, NSTouchPhaseCancelled, TouchPhase.Cancelled);
    }

    private void ProcessPhase(IntPtr nsEvent, nuint nsPhase, TouchPhase phase)
    {
        IntPtr touches = IntPtrObjCMsgSendNUIntIntPtr(nsEvent, TouchesMatchingPhaseInViewSelector, nsPhase, _contentView);
        if (touches == IntPtr.Zero)
        {
            return;
        }

        IntPtr array = IntPtrObjCMsgSend(touches, AllObjectsSelector);
        if (array == IntPtr.Zero)
        {
            return;
        }

        nuint count = NUIntObjCMsgSend(array, CountSelector);
        for (nuint i = 0; i < count; i++)
        {
            IntPtr touch = IntPtrObjCMsgSendNUInt(array, ObjectAtIndexSelector, i);
            if (touch == IntPtr.Zero)
            {
                continue;
            }

            ProcessTouch(touch, phase);
        }
    }

    private void ProcessTouch(IntPtr touch, TouchPhase phase)
    {
        IntPtr identity = IntPtrObjCMsgSend(touch, IdentitySelector);
        if (identity == IntPtr.Zero)
        {
            return;
        }

        MacPoint normalizedPosition = MacPointObjCMsgSend(touch, NormalizedPositionSelector);
        var size = _window.Size;
        float width = Math.Max(size.X, 1);
        float height = Math.Max(size.Y, 1);
        float x = (float)Math.Clamp(normalizedPosition.X, 0.0, 1.0) * width;
        float y = (float)(1.0 - Math.Clamp(normalizedPosition.Y, 0.0, 1.0)) * height;
        float pressure = phase is TouchPhase.Ended or TouchPhase.Cancelled ? 0.0f : 1.0f;

        lock (_lock)
        {
            int id = GetOrCreateTouchId(identity);
            _events.Add(new TouchInputEvent(id, x, y, phase, TouchInputKind.Touch, pressure));
            if (phase is TouchPhase.Ended or TouchPhase.Cancelled)
            {
                _ = _touchIds.Remove(identity);
            }
        }
    }

    private int GetOrCreateTouchId(IntPtr identity)
    {
        if (_touchIds.TryGetValue(identity, out int id))
        {
            return id;
        }

        id = _nextTouchId++;
        if (_nextTouchId == int.MaxValue)
        {
            _nextTouchId = 1;
        }

        _touchIds[identity] = id;
        return id;
    }

    private static void SetTouchAcceptance(IntPtr contentView, bool enabled)
    {
        if (RespondsToSelector(contentView, SetAcceptsTouchEventsSelector))
        {
            VoidObjCMsgSendBool(contentView, SetAcceptsTouchEventsSelector, enabled);
        }

        if (RespondsToSelector(contentView, SetWantsRestingTouchesSelector))
        {
            VoidObjCMsgSendBool(contentView, SetWantsRestingTouchesSelector, enabled);
        }
    }

    private static bool RespondsToSelector(IntPtr receiver, IntPtr selector)
    {
        return receiver != IntPtr.Zero && BoolObjCMsgSendIntPtr(receiver, RespondsToSelectorSelector, selector);
    }

    private static IntPtr CreateTouchViewSubclass(IntPtr originalClass)
    {
        string className = "ZhengyanTouchView_" + Guid.NewGuid().ToString("N");
        IntPtr subclass = ObjCAllocateClassPair(originalClass, className, 0);
        if (subclass == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        bool added = AddTouchMethod(subclass, TouchesBeganSelector, TouchesBeganCallback)
            && AddTouchMethod(subclass, TouchesMovedSelector, TouchesMovedCallback)
            && AddTouchMethod(subclass, TouchesEndedSelector, TouchesEndedCallback)
            && AddTouchMethod(subclass, TouchesCancelledSelector, TouchesCancelledCallback);
        if (!added)
        {
            ObjCDisposeClassPair(subclass);
            return IntPtr.Zero;
        }

        ObjCRegisterClassPair(subclass);
        return subclass;
    }

    private static bool AddTouchMethod(IntPtr cls, IntPtr selector, TouchEventDelegate callback)
    {
        return ClassAddMethod(cls, selector, Marshal.GetFunctionPointerForDelegate(callback), "v@:@");
    }

    private static void TouchesBegan(IntPtr self, IntPtr selector, IntPtr nsEvent)
    {
        HandleNativeTouchEvent(self, selector, nsEvent);
    }

    private static void TouchesMoved(IntPtr self, IntPtr selector, IntPtr nsEvent)
    {
        HandleNativeTouchEvent(self, selector, nsEvent);
    }

    private static void TouchesEnded(IntPtr self, IntPtr selector, IntPtr nsEvent)
    {
        HandleNativeTouchEvent(self, selector, nsEvent);
    }

    private static void TouchesCancelled(IntPtr self, IntPtr selector, IntPtr nsEvent)
    {
        HandleNativeTouchEvent(self, selector, nsEvent);
    }

    private static void HandleNativeTouchEvent(IntPtr self, IntPtr selector, IntPtr nsEvent)
    {
        ViewState? state;
        lock (ViewStatesLock)
        {
            _ = ViewStates.TryGetValue(self, out state);
        }

        try
        {
            state?.Source.HandleTouchEvent(nsEvent);
        }
        catch
        {
        }

        if (state is not null)
        {
            CallSuper(state, self, selector, nsEvent);
        }
    }

    private static void CallSuper(ViewState state, IntPtr self, IntPtr selector, IntPtr nsEvent)
    {
        if (state.OriginalClass == IntPtr.Zero)
        {
            return;
        }

        ObjCSuper super = new()
        {
            Receiver = self,
            SuperClass = state.OriginalClass
        };
        VoidObjCMsgSendSuper(ref super, selector, nsEvent);
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
            && !libraryName.Equals("libglfw.3.dylib", StringComparison.OrdinalIgnoreCase))
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
        const string fileName = "libglfw.3.dylib";
        string baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, fileName);

        string? rid = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "osx-x64",
            Architecture.Arm64 => "osx-arm64",
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(rid))
        {
            yield return Path.Combine(baseDirectory, "runtimes", rid, "native", fileName);
        }
    }

    private delegate void TouchEventDelegate(IntPtr self, IntPtr selector, IntPtr nsEvent);

    private sealed class ViewState(MacTouchInputSource source, IntPtr originalClass)
    {
        public MacTouchInputSource Source { get; } = source;

        public IntPtr OriginalClass { get; } = originalClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MacPoint
    {
        public double X;

        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjCSuper
    {
        public IntPtr Receiver;

        public IntPtr SuperClass;
    }

    [DllImport("glfw3", EntryPoint = "glfwGetCocoaWindow", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GetCocoaWindow(IntPtr window);

    [DllImport(ObjCLibrary, EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(ObjCLibrary, EntryPoint = "object_getClass")]
    private static extern IntPtr ObjectGetClass(IntPtr obj);

    [DllImport(ObjCLibrary, EntryPoint = "object_setClass")]
    private static extern IntPtr ObjectSetClass(IntPtr obj, IntPtr cls);

    [DllImport(ObjCLibrary, EntryPoint = "objc_allocateClassPair")]
    private static extern IntPtr ObjCAllocateClassPair(IntPtr superclass, [MarshalAs(UnmanagedType.LPStr)] string name, nint extraBytes);

    [DllImport(ObjCLibrary, EntryPoint = "objc_registerClassPair")]
    private static extern void ObjCRegisterClassPair(IntPtr cls);

    [DllImport(ObjCLibrary, EntryPoint = "objc_disposeClassPair")]
    private static extern void ObjCDisposeClassPair(IntPtr cls);

    [DllImport(ObjCLibrary, EntryPoint = "class_addMethod")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ClassAddMethod(IntPtr cls, IntPtr name, IntPtr imp, [MarshalAs(UnmanagedType.LPStr)] string types);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtrObjCMsgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtrObjCMsgSendNUInt(IntPtr receiver, IntPtr selector, nuint argument);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtrObjCMsgSendNUIntIntPtr(IntPtr receiver, IntPtr selector, nuint argument1, IntPtr argument2);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nuint NUIntObjCMsgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern MacPoint MacPointObjCMsgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void VoidObjCMsgSendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool argument);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool BoolObjCMsgSendIntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSendSuper")]
    private static extern void VoidObjCMsgSendSuper(ref ObjCSuper super, IntPtr selector, IntPtr argument);
}

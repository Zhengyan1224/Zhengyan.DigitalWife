using System.Runtime.InteropServices;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidGlesBindings
{
    // Keep the system library loaded for the lifetime of the process. A GL
    // binding can outlive a window surface during an Android lifecycle change.
    private static readonly Lazy<nint> GlesLibrary = new(() => NativeLibrary.Load("libGLESv2.so"));

    public static GL Create() => GL.GetApi(Resolve);

    private static nint Resolve(string name)
    {
        nint address = EglGetProcAddress(name);
        if (address == 0)
        {
            NativeLibrary.TryGetExport(GlesLibrary.Value, name, out address);
        }
        return address;
    }

    [DllImport("libEGL.so", EntryPoint = "eglGetProcAddress", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint EglGetProcAddress([MarshalAs(UnmanagedType.LPStr)] string name);
}

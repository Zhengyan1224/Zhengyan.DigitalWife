using System.Reflection;
using System.Runtime.Loader;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

/// <summary>Owns one binding's static state while sharing the host's API assemblies.</summary>
internal sealed class AndroidScriptInstance : IDisposable
{
    private AssemblyLoadContext? _loadContext;
    private Func<object?[], Task<object?>>? _factory;
    private readonly object?[] _submissionArray = new object?[2];

    internal AndroidScriptInstance(byte[] image)
    {
        AssemblyLoadContext context = new("AndroidScriptInstance", isCollectible: true);
        try
        {
            using MemoryStream stream = new(image, writable: false);
            Assembly assembly = context.LoadFromStream(stream);
            Type scriptType = assembly.GetType("Script")
                ?? throw new InvalidOperationException("Android C# script did not produce a Script type.");
            MethodInfo factory = scriptType.GetMethod("<Factory>", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Android C# script did not produce an execution factory.");
            _factory = factory.CreateDelegate<Func<object?[], Task<object?>>>();
            _loadContext = context;
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    internal object? ExecuteResult(AndroidScriptGlobals globals)
    {
        ObjectDisposedException.ThrowIf(_loadContext is null, this);
        _submissionArray[0] = globals;
        _submissionArray[1] = null;
        return _factory!(_submissionArray).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        AssemblyLoadContext? context = _loadContext;
        _loadContext = null;
        _factory = null;
        Array.Clear(_submissionArray);
        // Unloading is cooperative: callbacks still in flight can finish using
        // their old state, without changing a newly loaded binding's statics.
        context?.Unload();
    }
}

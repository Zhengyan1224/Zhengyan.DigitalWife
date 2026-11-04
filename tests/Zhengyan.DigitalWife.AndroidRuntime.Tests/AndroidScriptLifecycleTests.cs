using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidScriptLifecycleTests
{
    internal static void TestBindings()
    {
        const string source = """
            static class State
            {
                public static readonly List<int> Updates = new() { 7 };
            }
            if (IsUpdate) State.Updates.Add(1);
            return State.Updates.Sum();
            """;
        using var fixture = new AndroidRuntimeRegressionTests.ScriptFixture(source);
        byte[] image = AndroidScriptCompiler.Compile(Path.Combine(fixture.Root, "script.csx"));
        using AndroidScriptInstance first = new(image);
        using AndroidScriptInstance second = new(image);
        fixture.Globals.Update(0, true, null);
        Check(first.ExecuteResult(fixture.Globals) is 7 && second.ExecuteResult(fixture.Globals) is 7,
            "Each binding must run its readonly static initializer.");
        fixture.Globals.Update(0.1f, false, null);
        Check(first.ExecuteResult(fixture.Globals) is 8 && first.ExecuteResult(fixture.Globals) is 9,
            "State must persist across updates within one binding.");
        fixture.Globals.Update(0, true, null);
        Check(second.ExecuteResult(fixture.Globals) is 7, "Two entities using the same bytecode shared static state.");
        fixture.Globals.Update(0, false, new("gui", "button", "click", default));
        Check(first.ExecuteResult(fixture.Globals) is 9, "Event execution lost the binding's update state.");
    }

    internal static void TestStaleCallbacks()
    {
        const string source = """
            static class State { public static int Value; }
            if (IsStart) Entity.Speak("hello", () => State.Value += 100);
            if (IsUpdate) State.Value++;
            return State.Value;
            """;
        using var fixture = new AndroidRuntimeRegressionTests.ScriptFixture(source);
        byte[] image = AndroidScriptCompiler.Compile(Path.Combine(fixture.Root, "script.csx"));
        AndroidScriptInstance old = new(image);
        fixture.Globals.Update(0, true, null);
        old.ExecuteResult(fixture.Globals);
        Action oldCallback = fixture.PendingSpeech!;
        old.Dispose();
        old.Dispose();
        using AndroidScriptInstance current = new(image);
        Check(current.ExecuteResult(fixture.Globals) is 0, "New scene inherited the old scene's state.");
        oldCallback();
        fixture.Globals.Update(0.1f, false, null);
        Check(current.ExecuteResult(fixture.Globals) is 1, "An old callback modified a new scene's static fields.");
        try { old.ExecuteResult(fixture.Globals); }
        catch (ObjectDisposedException) { return; }
        throw new InvalidOperationException("A disposed script was still executable.");
    }

    internal static void TestUnload()
    {
        using var fixture = new AndroidRuntimeRegressionTests.ScriptFixture("return Entity.Name;");
        byte[] image = AndroidScriptCompiler.Compile(Path.Combine(fixture.Root, "script.csx"));
        WeakReference[] references = Enumerable.Range(0, 8).Select(_ => ExecuteAndUnload(image, fixture.Globals)).ToArray();
        for (int i = 0; i < 10 && references.Any(reference => reference.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Check(references.All(reference => !reference.IsAlive), "Repeated scene loads retained their script assemblies.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ExecuteAndUnload(byte[] image, AndroidScriptGlobals globals)
    {
        using AndroidScriptInstance script = new(image);
        script.ExecuteResult(globals);
        AssemblyLoadContext context = (AssemblyLoadContext)typeof(AndroidScriptInstance)
            .GetField("_loadContext", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(script)!;
        Check(context.IsCollectible, "Script context is permanently rooted.");
        return new WeakReference(context);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

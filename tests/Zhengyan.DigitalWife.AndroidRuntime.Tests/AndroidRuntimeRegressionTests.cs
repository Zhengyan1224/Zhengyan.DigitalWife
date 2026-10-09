using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Zhengyan.DigitalWife.GameEditor;
using Zhengyan.DigitalWife.GamePlayer;
using Zhengyan.DigitalWife.GamePlayer.Android;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Llm.OpenAI;

internal static class AndroidRuntimeRegressionTests
{
    public static int Run(string? filter = null)
    {
        (string Name, Action Test)[] tests =
        [
            ("Loading frames and failure preservation", TestLoading),
            ("Hosted loading progress layout without a desktop window", HostedLoadingScreenRegressionTests.TestProgressLayout),
            ("Hosted Vulkan loading frames without device recreation", HostedLoadingScreenRegressionTests.TestVulkanFrames),
            ("Vulkan asynchronous skinning and vertex visibility", VulkanSynchronizationRegressionTests.TestSkinningFrames),
            ("Vulkan large CPU uploads after Compute fallback", VulkanSynchronizationRegressionTests.TestCpuFallbackFrames),
            ("Vulkan PMX draw snapshots, reflection and depth across frame slots", VulkanDrawSnapshotTests.TestPmxPasses),
            ("Published script ABI, lambda and event fields", TestPublishedScript),
            ("Android in-memory script references include default imports", AndroidScriptMetadataTests.TestDefaultImports),
            ("TTS initialization retries after failure", AndroidTtsInitializationTests.TestRetry),
            ("TTS initialization cancellation is per caller", AndroidTtsInitializationTests.TestCancellation),
            ("TTS timeout and stale initialization completion", AndroidTtsInitializationTests.TestTimeout),
            ("TTS playback range drives mouth morphs and resets on stop", AndroidSpeechLipSyncTests.TestPlaybackMorphs),
            ("Dialogue bubbles follow model transforms, camera and resize", AndroidDialogueBubbleTests.TestFollowing),
            ("Dialogue bubble anchor modes, viewport and missing targets", AndroidDialogueBubbleTests.TestAnchorModesAndVisibility),
            ("Streamed multi-round script tools and main-thread callbacks", TestNativeTools),
            ("Native-tool rejection and text fallback", TestTextFallback),
            ("Tool round limit prevents extra execution", TestRoundLimit),
            ("HTTP cancellation and scene disposal", TestCancellation),
            ("Skills, memory paths and automatic registration", TestSkills),
            ("Skill command cancellation stops the child process", TestCommandCancellation),
            ("Editor precompile and encrypted dwgame round trip", TestPackage),
            ("Desktop and Android exports in one process", TestDesktopIsolation)
        ];
        if (filter is not null) tests = tests.Where(test => test.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        int failures = 0;
        foreach (var (name, test) in tests)
        {
            try { test(); Console.WriteLine($"PASS {name}"); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex}"); }
        }
        Console.WriteLine($"Android runtime regression tests: {tests.Length - failures}/{tests.Length} passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void TestLoading()
    {
        AndroidSceneLoading loading = new();
        List<string> actions = [];
        loading.Enqueue("model", () => actions.Add("model"));
        loading.Enqueue("audio", () => actions.Add("audio"));
        loading.Advance(); loading.Advance();
        Check(actions.Count == 0, "The loading screen needs frames before resource work.");
        loading.Advance();
        Check(actions.SequenceEqual(["model"]) && loading.Progress == 0.5f && !loading.IsReady, "Resources must advance independently.");
        loading.Advance();
        Check(actions.SequenceEqual(["model", "audio"]) && loading.Progress == 1.0f && !loading.IsReady, "100% must be presentable before completion.");
        loading.Advance();
        Check(loading.IsReady, "Scene did not finish loading.");

        AndroidSceneLoading failed = new();
        failed.Enqueue("bad model", () => throw new InvalidDataException("broken"));
        failed.Enqueue("must not execute", () => throw new Exception("Unexpected execution."));
        for (int i = 0; i < 8; i++) failed.Advance();
        Check(!failed.IsReady && failed.Error is InvalidDataException && failed.Progress == 0 && failed.Message.Contains("broken"), "A resource failure must not become loading_completed.");
    }

    private static void TestPublishedScript()
    {
        using ScriptFixture fixture = new();
        fixture.Run(isStart: true);
        Check(fixture.SpeechStarts == 1 && fixture.Status != "spoken", "Speech callback ran before playback completion.");
        fixture.PendingSpeech!();
        Check(fixture.Status == "spoken", "The compiled lambda did not reach the runtime API.");
        fixture.Globals.Update(0, false, new("loading", "load", "loading_progress", Vector2.Zero, "model", Progress: 0.25f));
        fixture.Execute();
        Check(fixture.Status == "loading_progress:0.25", "Published loading event fields are incorrect.");

        RuntimeLlmScriptEvent value = new("request", "delta", "world", "hello world", false, "", "custom_delta", null, null);
        fixture.Globals.Update(0, false, AndroidRuntimeEvent.FromLlm(fixture.Entity, value));
        Check(fixture.Globals.LlmDelta == "world" && fixture.Globals.LlmText == "hello world" && fixture.Globals.LlmCallbackName == "custom_delta" && fixture.Globals.LlmEventName == "delta", "Event names, callbacks and accumulated text must remain separate.");
        fixture.Globals.Update(0.1f, false, null);
        Check(fixture.Globals.IsUpdate && !fixture.Globals.IsLlmEvent && fixture.Globals.LlmText.Length == 0, "Event state leaked into the next frame.");
    }

    private static void TestNativeTools()
    {
        using ScriptFixture fixture = new();
        using FakeHandler handler = new(
            _ => Task.FromResult(ToolResponse("call_1", "echo", "{\"value\":\"one\"}", split: true)),
            _ => Task.FromResult(ToolResponse("call_2", "echo", "{\"value\":\"two\"}")),
            _ => Task.FromResult(TextResponse("hello ", "world")));
        List<RuntimeLlmScriptEvent> events = [];
        using RuntimeLlm llm = CreateLlm(fixture, handler, (entity, value) =>
        {
            Check(Environment.CurrentManagedThreadId == fixture.OwnerThread, "Callback escaped the render dispatcher.");
            events.Add(value);
            fixture.Globals.Update(0, false, AndroidRuntimeEvent.FromLlm(entity, value));
            fixture.Execute();
        });
        RuntimeLlmTool tool = new RuntimeLlmScriptTool("echo", "Echo value", "{\"type\":\"object\"}", "execute_echo").ToTool(fixture.Entity, fixture.Scene);
        string id = llm.StartChatWithTools(fixture.Entity, "test", [tool], requestId: "native_test",
            onDeltaCallback: "custom_delta", onCompletedCallback: "done", onToolCallCallback: "calling", onToolResultCallback: "result");
        PumpUntil(fixture.Dispatcher, () => events.Any(e => e.EventName is "completed" or "error"));
        Check(events.All(e => e.RequestId == id) && events.All(e => string.IsNullOrEmpty(e.Error)), "Request correlation or completion failed.");
        Check(fixture.ToolExecutions == 2 && fixture.ToolThreads.All(t => t == fixture.OwnerThread), "Named script tools must execute on the render thread.");
        Check(events.Last().AccumulatedText == "hello world" && events.Last().IsFinal, "Final accumulated text was lost.");
        Check(events.Any(e => e.Delta == "hello ") && events.Any(e => e.Delta == "world"), "Streaming deltas were collapsed.");
        Check(events.Count(e => e.EventName == "tool_call") == 2 && events.Count(e => e.EventName == "tool_result") == 2, "Tool notifications are missing.");
        JsonElement[] requests = handler.Requests.ToArray();
        Check(requests.Length == 3, "Multi-round tool exchange did not continue.");
        JsonElement resultMessage = requests[1].GetProperty("messages").EnumerateArray().Last();
        Check(resultMessage.GetProperty("role").GetString() == "tool" && resultMessage.GetProperty("tool_call_id").GetString() == "call_1" && resultMessage.GetProperty("content").GetString() == "one", "Tool result was not sent back with the original call ID.");
    }

    private static void TestTextFallback()
    {
        using ScriptFixture fixture = new();
        using FakeHandler handler = new(
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("tools are not supported") }),
            _ => Task.FromResult(TextResponse("<dw_tool_call>{\"name\":\"echo\",\"arguments\":{\"value\":\"fallback\"}}</dw_tool_call>")),
            _ => Task.FromResult(TextResponse("fallback complete")));
        using RuntimeLlm llm = CreateLlm(fixture, handler);
        int calls = 0;
        RuntimeLlmTool tool = new("echo", "echo", "{}", (string args) => { calls++; return "fallback"; });
        string answer = llm.ChatWithToolsAsync("test", [tool]).GetAwaiter().GetResult();
        Check(answer == "fallback complete" && calls == 1, "Text-protocol fallback did not run the tool.");
        Check(!handler.Requests.ToArray()[1].TryGetProperty("tools", out _), "Rejected native tools were sent again.");
    }

    private static void TestRoundLimit()
    {
        using ScriptFixture fixture = new();
        using FakeHandler handler = new(
            _ => Task.FromResult(ToolResponse("a", "echo", "{}")),
            _ => Task.FromResult(ToolResponse("b", "echo", "{}")));
        using RuntimeLlm llm = CreateLlm(fixture, handler);
        int calls = 0;
        RuntimeLlmTool tool = new("echo", "echo", "{}", (string args) => { calls++; return "ok"; });
        try { llm.ChatWithToolsAsync("test", [tool], maxToolRounds: 1).GetAwaiter().GetResult(); throw new Exception("Missing tool limit."); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("maxToolRounds")) { }
        Check(calls == 1, "The limit allowed an extra tool side effect.");
    }

    private static void TestCancellation()
    {
        using ScriptFixture fixture = new();
        int entered = 0, canceled = 0, callbacks = 0;
        using FakeHandler handler = new(async token =>
        {
            Interlocked.Increment(ref entered);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { Interlocked.Increment(ref canceled); throw; }
            return TextResponse("unreachable");
        });
        using RuntimeLlm llm = CreateLlm(fixture, handler, (_, _) => callbacks++);
        string id = llm.StartChat(fixture.Entity, "wait");
        PumpUntil(fixture.Dispatcher, () => Volatile.Read(ref entered) == 1);
        llm.CancelRequest(id);
        PumpUntil(fixture.Dispatcher, () => Volatile.Read(ref canceled) == 1);
        fixture.Dispatcher.Pump();
        Check(callbacks == 0, "Cancellation produced stale completion/error callbacks.");

        using FakeHandler pending = new(async token => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return TextResponse("unreachable"); });
        RuntimeLlm disposed = CreateLlm(fixture, pending, (_, _) => callbacks++);
        disposed.StartChat(fixture.Entity, "dispose immediately");
        disposed.Dispose();
        fixture.Dispatcher.Pump();
        Check(callbacks == 0, "Scene disposal did not suppress callbacks.");
    }

    private static void TestSkills()
    {
        using ScriptFixture fixture = new();
        string skill = Path.Combine(fixture.Root, "skills", "sample");
        Directory.CreateDirectory(skill);
        File.WriteAllText(Path.Combine(skill, "SKILL.md"), "---\nname: sample\ndescription: test skill\n---\nSkill body.");
        RuntimeLlmSkillTools catalog = new(fixture.Root, fixture.SaveRoot);
        string Invoke(string name, string args) => catalog.SkillTools.Concat(catalog.MemoryTools).Single(t => t.Name == name)
            .InvokeAsync(new("test", name, args), CancellationToken.None).GetAwaiter().GetResult();
        Check(Invoke("skill_list", "{}").Contains("sample"), "Skills were not listed.");
        Check(Invoke("skill_read", "{\"name\":\"sample\"}").Contains("Skill body."), "Skill text was not read.");
        Check(Invoke("memory_write", "{\"path\":\"notes.md\",\"content\":\"persistent memory\",\"append\":false}").Contains("\"ok\":true"), "Memory write failed.");
        Check(File.ReadAllText(Path.Combine(fixture.SaveRoot, "memory", "notes.md")) == "persistent memory", "Memory used a different root from Save.");
        Check(Invoke("memory_read", "{\"path\":\"notes.md\"}").Contains("persistent memory"), "Memory read failed.");
        Check(Invoke("skill_read", "{\"name\":\"../outside\"}").Contains("\"ok\":false"), "Skill path traversal was allowed.");
        Check(Invoke("memory_write", "{\"path\":\"../outside.md\",\"content\":\"x\"}").Contains("\"ok\":false"), "Memory path traversal was allowed.");
        using FakeHandler handler = new(_ => Task.FromResult(TextResponse("ok")));
        using RuntimeLlm llm = CreateLlm(fixture, handler, settings: new() { Enabled = true, EnableSkills = true, EnableMemory = true, Model = "test" });
        Check(llm.MemoryDirectory == catalog.MemoryDirectory && llm.SkillsDirectory == Path.Combine(fixture.Root, "skills"), "Reported skill/memory paths differ from actual storage.");
        llm.ChatAsync("hello").GetAwaiter().GetResult();
        string[] names = handler.Requests.Single().GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("function").GetProperty("name").GetString()!).ToArray();
        Check(names.Contains("skill_list") && names.Contains("memory_search"), "Project settings did not register built-in tools.");
    }

    private static void TestPackage()
    {
        using ScriptFixture fixture = new();
        GameProject project = new() { Name = "Script ABI fixture", Scene = fixture.CoreScene.Definition };
        project.Scene.LoadingScripts.Add(new() { Language = "csharp", Path = "script.csx", Enabled = true });
        project.Scene.Entities[0].Scripts.Add(new() { Language = "csharp", Path = "script.csx", Enabled = true });
        GameProjectStore.Save(fixture.Root, project);
        Check(AndroidProjectCompatibility.Analyze(fixture.Root, project).CanPublish, "Supported loading scripts must not reject Android publication.");
        AndroidScriptPrecompileResult compiled = AndroidCSharpScriptPrecompiler.Precompile(fixture.Root, project);
        Check(compiled.Entries.Count == 1, "The same script should be compiled once across loading and entity bindings.");
        GameProjectPackageBuildResult package = GameProjectPackage.Create(fixture.Root,
            new() { OutputPath = Path.Combine(fixture.Root, "result.dwgame"), Password = "test-only" });
        using GameProjectPackageSession extracted = GameProjectPackage.OpenOrExtract(package.OutputPath,
            new() { Password = "test-only", TempRootDirectory = Path.Combine(fixture.Root, "extract"), UsePersistentCache = false, SaveDirectory = fixture.SaveRoot });
        AndroidScriptPrecompileEntry entry = compiled.Entries.Single();
        byte[] image = File.ReadAllBytes(Path.Combine(extracted.ProjectDirectory, entry.Assembly));
        Check(Convert.ToHexString(SHA256.HashData(image)) == entry.Sha256, "Published assembly changed during packaging.");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(extracted.ProjectDirectory, "compiled", "android", "manifest.json")));
        Check(manifest.RootElement.GetProperty("globalsContract").GetString() == typeof(AndroidScriptGlobals).Assembly.GetName().Name, "Manifest does not identify the actual runtime ABI.");
        var factory = Factory(image);
        fixture.Globals.Update(0, false, new("loading", "l", "loading_completed", Vector2.Zero, Progress: 1));
        factory([fixture.Globals, null]).GetAwaiter().GetResult();
        Check(fixture.Status == "loading_completed:1.00", "Extracted DLL could not execute against Android globals.");
    }

    private static void TestCommandCancellation()
    {
        using ScriptFixture fixture = new();
        RuntimeLlmSkillTools catalog = new(fixture.Root, fixture.SaveRoot);
        RuntimeLlmTool command = catalog.SkillTools.Single(t => t.Name.Contains("command", StringComparison.Ordinal));
        string readyPath = Path.Combine(fixture.Root, "child-ready.txt");
        string assemblyPath = typeof(AndroidRuntimeRegressionTests).Assembly.Location;
        string arguments = JsonSerializer.Serialize(new { command = $"dotnet \"{assemblyPath}\" --sleep-child \"{readyPath}\"", timeoutSeconds = 30 });
        using CancellationTokenSource cancellation = new();
        Task<string> execution = command.InvokeAsync(new("child", command.Name, arguments), cancellation.Token);
        PumpUntil(fixture.Dispatcher, () => execution.IsCompleted || (File.Exists(readyPath) && new FileInfo(readyPath).Length > 0));
        if (execution.IsCompleted) throw new InvalidOperationException($"Child did not start: {execution.GetAwaiter().GetResult()}");
        using Process child = Process.GetProcessById(int.Parse(File.ReadAllText(readyPath)));
        _ = child.StartTime;
        try
        {
            cancellation.Cancel();
            try { execution.GetAwaiter().GetResult(); throw new Exception("Command ignored cancellation."); }
            catch (OperationCanceledException) { }
            Check(child.WaitForExit(2000), "Canceled skill left its child process running.");
        }
        finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
    }

    private static void TestDesktopIsolation()
    {
        using ScriptFixture fixture = new();
        GameProject project = new() { Scene = fixture.CoreScene.Definition };
        project.Scene.Entities[0].Scripts.Add(new() { Language = "csharp", Path = "script.csx" });
        GameProjectStore.Save(fixture.Root, project);
        AndroidCSharpScriptPrecompiler.Precompile(fixture.Root, project);
        Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Zhengyan.DigitalWife.GameProjects.dll"));
        DesktopScriptPrecompileResult desktop = DesktopCSharpScriptPrecompiler.Precompile(fixture.Root, project);
        Assembly compiled = Assembly.Load(File.ReadAllBytes(Path.Combine(fixture.Root, desktop.Entries.Single().Assembly)));
        Check(compiled.GetReferencedAssemblies().All(a => !a.Name!.Contains("Android")), "Desktop export referenced Android script types.");
        Check(AndroidScriptCompiler.Compile(Path.Combine(fixture.Root, "script.csx")).Length > 0, "Desktop assembly loading contaminated Android export.");
    }

    private static RuntimeLlm CreateLlm(ScriptFixture fixture, FakeHandler handler,
        Action<AndroidScriptEntity, RuntimeLlmScriptEvent>? dispatch = null, GameProjectLlmSettings? settings = null)
    {
        OpenAiCompatibleLlmClient client = new(new() { BaseUrl = "https://test.invalid", ApiKey = "test-only" },
            NullLogger<OpenAiCompatibleLlmClient>.Instance, new HttpClient(handler));
        return new(settings ?? new() { Enabled = true, Model = "test" }, fixture.Root, fixture.SaveRoot,
            fixture.Dispatcher, dispatch ?? ((_, _) => { }), client);
    }

    private static HttpResponseMessage TextResponse(params string[] deltas)
        => Sse(deltas.Select(delta => JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = delta } } } })));

    private static HttpResponseMessage ToolResponse(string id, string name, string arguments, bool split = false)
    {
        int cut = split ? arguments.Length / 2 : arguments.Length;
        List<string> chunks = [JsonSerializer.Serialize(new { choices = new[] { new { delta = new { tool_calls = new[] { new { index = 0, id, type = "function", function = new { name, arguments = arguments[..cut] } } } } } } })];
        if (split) chunks.Add(JsonSerializer.Serialize(new { choices = new[] { new { delta = new { tool_calls = new[] { new { index = 0, function = new { arguments = arguments[cut..] } } } } } } }));
        return Sse(chunks);
    }

    private static HttpResponseMessage Sse(IEnumerable<string> chunks) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(string.Concat(chunks.Select(c => $"data: {c}\n\n")) + "data: [DONE]\n\n", Encoding.UTF8, "text/event-stream")
    };

    private static Func<object?[], Task<object?>> Factory(byte[] image) => Assembly.Load(image).GetType("Script")!
        .GetMethod("<Factory>", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
        .CreateDelegate<Func<object?[], Task<object?>>>();

    private static void PumpUntil(MainThreadDispatcher dispatcher, Func<bool> condition)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Runtime callback did not arrive.");
            dispatcher.Pump();
            Thread.Sleep(1);
        }
        dispatcher.Pump();
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class FakeHandler(params Func<CancellationToken, Task<HttpResponseMessage>>[] responses) : HttpMessageHandler
    {
        private int _index;
        public ConcurrentQueue<JsonElement> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using JsonDocument json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Enqueue(json.RootElement.Clone());
            int index = Interlocked.Increment(ref _index) - 1;
            Check(index < responses.Length, "Unexpected extra HTTP request.");
            return await responses[index](cancellationToken);
        }
    }

    private sealed class ScriptFixture : IDisposable
    {
        public string Root { get; } = Path.GetFullPath(Path.Combine("temp", "android-runtime-tests", Guid.NewGuid().ToString("N")));
        public string SaveRoot => Path.Combine(Root, "saves");
        public MainThreadDispatcher Dispatcher { get; } = new();
        public int OwnerThread { get; } = Environment.CurrentManagedThreadId;
        public RuntimeScene CoreScene { get; }
        public AndroidScriptScene Scene { get; }
        public AndroidScriptEntity Entity { get; }
        public AndroidScriptGlobals Globals { get; }
        public int SpeechStarts { get; private set; }
        public Action? PendingSpeech { get; private set; }
        public int ToolExecutions { get; private set; }
        public List<int> ToolThreads { get; } = [];
        public string Status => Scene.GetGuiControl("status")!.Text;
        private readonly AndroidScriptServices _services;
        private readonly Func<object?[], Task<object?>> _factory;

        public ScriptFixture()
        {
            Directory.CreateDirectory(Root);
            CoreScene = new("scenes/main.scene.json", new() { Entities = [new() { Id = "entity", Name = "actor", Type = "empty" }], GuiControls = [new() { Name = "status" }] }, p => Path.Combine(Root, p));
            _services = new(CoreScene, Root, _ => { }, _ => true, _ => true, _ => true, _ => false, (_, _, _) => false,
                _ => null, () => [], _ => { }, new(), SaveRoot, Dispatcher, null!, null!, null!, null!);
            Scene = new(CoreScene, Root, _ => { }, () => 60, _services);
            Entity = new(CoreScene.Entities.Single(), _ => { }, (_, _) => { }, () => null,
                invokeLlmTool: (callback, call) => Dispatcher.InvokeAsync(() =>
                {
                    ToolExecutions++;
                    ToolThreads.Add(Environment.CurrentManagedThreadId);
                    Globals!.Update(0, false, new("llm", "execute", "tool_execute", Vector2.Zero, ToolCall: call, CallbackName: callback));
                    return Execute() as string;
                }),
                speak: (text, speaker, speed, volume, completed, callback) => { SpeechStarts++; PendingSpeech = completed; });
            Globals = new(Scene, Entity, new EmptyInput(), new(_ => true, _ => true, _ => true), 0, false, false, services: _services);
            string path = Path.Combine(Root, "script.csx");
            File.WriteAllText(path, """
                RuntimeLlmChatMessage message = new("user", "ABI test");
                if (IsStart) Entity.Speak("hello", () => Scene.GetGuiControl("status").SetValue("spoken"));
                if (IsLoadingEvent) Scene.GetGuiControl("status").SetValue(LoadingEventName + ":" + LoadingProgress.ToString("F2", CultureInfo.InvariantCulture));
                if (IsLlmEvent && LlmCallbackName == "execute_echo")
                {
                    using JsonDocument arguments = JsonDocument.Parse(LlmToolArgumentsJson);
                    return arguments.RootElement.GetProperty("value").GetString();
                }
                return null;
                """);
            _factory = Factory(AndroidScriptCompiler.Compile(path));
        }
        public object? Execute() => _factory([Globals, null]).GetAwaiter().GetResult();
        public void Run(bool isStart) { Globals.Update(0, isStart, null); Execute(); }
        public void Dispose() { _services.Dispose(); CoreScene.Dispose(); }
    }

    private sealed class EmptyInput : AndroidScriptInputApi
    {
        public override bool IsKeyDown(string key) => false;
        public override bool IsKeyPressed(string key) => false;
        public override bool IsKeyReleased(string key) => false;
        public override float MouseX => 0;
        public override float MouseY => 0;
        public override float MouseDeltaX => 0;
        public override float MouseDeltaY => 0;
        public override float ScrollX => 0;
        public override float ScrollY => 0;
        public override bool CursorVisible { get; set; }
        public override bool IsMouseButtonDown(string button) => false;
        public override bool IsMouseButtonPressed(string button) => false;
        public override bool IsMouseButtonReleased(string button) => false;
        public override bool HasGamepad => false;
        public override string GamepadName => "";
        public override float LeftStickX => 0;
        public override float LeftStickY => 0;
        public override float RightStickX => 0;
        public override float RightStickY => 0;
        public override float LeftTrigger => 0;
        public override float RightTrigger => 0;
        public override bool IsGamepadButtonDown(string button) => false;
        public override string ClipboardText => "";
        public override bool HasClipboardText => false;
        public override bool TrySetClipboardText(string text) => false;
        public override void SetClipboardText(string text) { }
    }
}

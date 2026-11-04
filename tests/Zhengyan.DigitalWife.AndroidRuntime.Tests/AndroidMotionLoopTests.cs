extern alias MmdDesktop;

using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Zhengyan.DigitalWife.GamePlayer.Android;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Mmd = MmdDesktop::Zhengyan.DigitalWife.Mmd;

internal static class AndroidMotionLoopTests
{
    internal static void RunProjectSceneScript(string inputPath)
    {
        using GameProjectPackageSession package = GameProjectPackage.OpenOrExtract(inputPath,
            new GameProjectPackageOpenOptions
            {
                TempRootDirectory = Path.GetFullPath(Path.Combine("temp", "scene-script-project", Guid.NewGuid().ToString("N")))
            });
        string root = package.ProjectDirectory;
        GameProject project = GameProjectStore.Load(root);
        string scenePath = project.Scenes.First(path => Path.GetFileName(path).StartsWith("next.scene", StringComparison.OrdinalIgnoreCase));
        GameProjectScene scene = GameProjectStore.LoadScene(root, scenePath);
        string scriptPath = scene.Entities.SelectMany(entity => entity.Scripts)
            .First(binding => Path.GetFileName(binding.Path) == "Body1.csx").Path;
        string sourcePath = GameProjectPath.ToAbsolute(root, scriptPath);
        byte[] sourceImage = AndroidScriptCompiler.Compile(sourcePath, AndroidScriptMetadata.GetReferences());
        string precompiledPath = Path.Combine(root, "compiled", "android", Path.ChangeExtension(scriptPath, ".dll"));
        List<(string Name, byte[] Image)> images = [("source", sourceImage)];
        if (File.Exists(precompiledPath)) images.Add(("precompiled", File.ReadAllBytes(precompiledPath)));
        using var services = new AndroidRuntimeRegressionTests.ScriptFixture("return null;",
            realtimeVoice: DispatchProxy.Create<IAndroidScriptRealtimeVoice, DisabledVoice>());
        foreach (var (name, image) in images)
        {
            for (int visit = 0; visit < 5; visit++)
            {
                using Fixture model = new(resolveAssetPath: path => GameProjectPath.ToAbsolute(root, path));
                using AndroidScriptInstance script = new(image);
                AndroidScriptGlobals globals = new(services.Scene, model.Entity, services.Globals.Input,
                    services.Globals.Audio, 0, true, false, services: services.Globals.Services);
                script.ExecuteResult(globals);
                model.ActivateLayers();
                Check(model.Model.MotionLayerCount == 2, $"{name} Body1 skipped motion setup on visit {visit + 1}.");
                globals.Update(1.0f / 30, false, null);
                script.ExecuteResult(globals);
                model.Tick(20);
                Check(model.Model.AnimationTimeSeconds > 0, $"{name} Body1 did not play standing motion.");
                Check(model.Model.GetMotionLayers().Select(layer => layer.Weight).SequenceEqual([1.0f, 0.0f]),
                    $"{name} Body1 standing/waiting weights were incorrect.");
            }
            Console.WriteLine($"PASS DemoGame01 next scene Body1 ({name}): 5 visits restored and advanced standing/waiting layers.");
        }
    }

    public class DisabledVoice : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name == "get_WakeWordEnabled" ? false
                : throw new InvalidOperationException("This animation regression must not start voice capture.");
    }

    internal static void TestSceneReloadScripts()
    {
        // Same initialization guard and layer setup as DemoGame01/Body1.csx.
        const string source = """
            static class VoiceState
            {
                public static bool MotionBlendReady;
                public static bool WakeWordMonitorStarted;
                public static int Updates;
            }
            if (IsStart && !VoiceState.MotionBlendReady)
            {
                Entity.SetMotionLayers(new[]
                {
                    new MotionLayerDefinition("stand.vmd", 1.0f, false),
                    new MotionLayerDefinition("wait.vmd", 0.0f, false)
                });
                Entity.LoopMotion = true;
                Entity.PlayMotion();
                VoiceState.MotionBlendReady = true;
                VoiceState.WakeWordMonitorStarted = true;
            }
            if (IsUpdate) VoiceState.Updates++;
            return VoiceState.Updates;
            """;
        using var services = new AndroidRuntimeRegressionTests.ScriptFixture(source);
        byte[] image = AndroidScriptCompiler.Compile(Path.Combine(services.Root, "script.csx"));
        // Reproduce the previous host: a cached factory is reused with a new entity.
        using (AndroidScriptInstance reused = new(image))
        {
            for (int visit = 0; visit < 2; visit++)
            {
                using Fixture model = new();
                AndroidScriptGlobals globals = new(services.Scene, model.Entity, services.Globals.Input,
                    services.Globals.Audio, 0, true, false, services: services.Globals.Services);
                reused.ExecuteResult(globals);
                model.ActivateLayers();
                Check(model.Model.MotionLayerCount == (visit == 0 ? 2 : 0),
                    "The regression fixture did not reproduce the cached-factory initialization bug.");
            }
        }
        for (int visit = 0; visit < 5; visit++)
        {
            using Fixture model = new();
            using AndroidScriptInstance script = new(image);
            AndroidScriptGlobals globals = new(services.Scene, model.Entity, services.Globals.Input,
                services.Globals.Audio, 0, true, false, services: services.Globals.Services);
            Check(script.ExecuteResult(globals) is 0, "A fresh scene inherited the previous visit's script state.");
            model.ActivateLayers();
            Check(model.Model.MotionLayerCount == 2, "Re-entering the scene skipped the standing/waiting layer initialization.");
            model.Tick(20);
            Check(model.Model.AnimationTimeSeconds > 0, "The restored motion did not advance.");
            Check(model.Model.GetMotionLayers().Select(layer => layer.Weight).SequenceEqual([1.0f, 0.0f]),
                "Standing/waiting blend weights changed during loading.");
            globals.Update(0.1f, false, null);
            Check(script.ExecuteResult(globals) is 1, "Updates did not retain the current scene's script instance.");
        }
    }

    internal static void TestScriptLayers()
    {
        using Fixture fixture = new();
        // DemoGame01/next.scene.json: the entity default is true, while Body1.csx
        // installs a standing layer and a zero-weight waiting layer with false.
        fixture.Entity.SetMotionLayers([new("stand.vmd", 1.0f, false), new("wait.vmd", 0.0f, false)]);
        fixture.ActivateLayers();
        fixture.Entity.LoopMotion = true;
        fixture.Entity.PlayMotion();
        fixture.Tick(1); // PlayMotion deliberately skips physics on its first frame.
        fixture.Tick(180);
        Check(fixture.Physics.ResetCount == 0, "Per-frame scene sync re-enabled physics resets on script layers.");
        Check(fixture.Physics.StepCount == 180, "Physics did not continue through motion loop boundaries.");
        Check(fixture.Model.GetMotionLayers().All(layer => !layer.ResetPhysicsOnLoop), "A script layer lost its false setting.");
        Check(!fixture.Entity.ResetPhysicsOnMotionLoop, "The script getter returned the stale scene default.");
    }

    internal static void TestMixedLayers()
    {
        using Fixture fixture = new(resetDefault: false);
        fixture.Entity.SetMotionLayers([new("stand.vmd", 1.0f, false), new("wait.vmd", 0.0f, true)]);
        fixture.ActivateLayers();
        fixture.Tick(40); // Standing layer has looped; waiting layer has not.
        Check(fixture.Physics.ResetCount == 0, "The non-resetting layer triggered a physics reset.");
        fixture.Tick(30); // Waiting layer loops at two seconds, even at zero weight.
        Check(fixture.Physics.ResetCount == 1, "The resetting layer's loop was suppressed by the entity default.");
        Check(fixture.Model.GetMotionLayers().Select(layer => layer.ResetPhysicsOnLoop).SequenceEqual([false, true]),
            "Scene sync flattened mixed per-layer settings.");
    }

    internal static void TestGlobalSetting()
    {
        using Fixture fixture = new(resetDefault: false);
        fixture.Entity.SetMotionLayers([new("stand.vmd")]);
        fixture.ActivateLayers();
        fixture.Tick(40);
        Check(fixture.Physics.ResetCount == 0, "An unspecified layer did not inherit the initial entity default.");
        fixture.Entity.ResetPhysicsOnMotionLoop = true;
        fixture.Tick(30);
        Check(fixture.Physics.ResetCount == 1 && fixture.Entity.ResetPhysicsOnMotionLoop,
            "An explicit global true setting did not enable loop resets.");
        fixture.Entity.ResetPhysicsOnMotionLoop = false;
        fixture.Tick(40);
        Check(fixture.Physics.ResetCount == 1 && !fixture.Entity.ResetPhysicsOnMotionLoop,
            "An explicit global false setting did not disable loop resets.");
        fixture.Entity.ResetPhysics();
        Check(fixture.Physics.ResetCount == 2, "Disabling loop resets also disabled an explicit physics reset.");
    }

    internal static void TestSlowFrames()
    {
        using Fixture fixture = new(timing: AnimationTimingMode.TimeSynchronized);
        fixture.Entity.SetMotionLayers([new("stand.vmd", 1, false)]);
        fixture.ActivateLayers();
        fixture.Physics.PlaybackTime = () => fixture.Model.AnimationTimeSeconds;
        // Include 60/30/20/10 FPS and a long scheduling stall. Physics must see
        // each intermediate target instead of one final target with a short dt.
        double elapsed = 0;
        foreach (double frame in new[] { 1.0 / 60, 1.0 / 30, 0.05, 0.1, 0.4, 0.7 })
        {
            int updates = fixture.Physics.SkinningCount;
            fixture.TickElapsed(frame);
            elapsed += frame;
            Check(fixture.Physics.SkinningCount == updates + 1, "Slow frame repeated final skinning/upload work.");
        }
        Check(Math.Abs(fixture.Physics.SimulatedSeconds - elapsed) < 1e-5, "Slow frames discarded physics time.");
        float previous = 0;
        foreach (var step in fixture.Physics.Steps)
        {
            float advanced = step.PlaybackTime - previous;
            if (advanced < 0) advanced += 1; // One-second stand animation loops.
            Check(Math.Abs(advanced - step.Elapsed) < 1e-5, "Animation and physics advanced by different amounts.");
            Check(step.Elapsed > 0 && step.Elapsed <= 1.0f / 30 + 1e-6f, "Unstable physics timestep.");
            previous = step.PlaybackTime;
        }
        Check(fixture.Physics.ResetCount == 0, "Slow-frame catch-up reset a non-resetting loop.");

        using Fixture dependent = new(timing: AnimationTimingMode.FrameRateDependent);
        dependent.Entity.SetMotionLayers([new("stand.vmd", 1, false)]);
        dependent.ActivateLayers();
        dependent.TickElapsed(0.2);
        Check(Math.Abs(dependent.Model.AnimationTimeSeconds - 1.0 / 30) < 1e-6
            && Math.Abs(dependent.Physics.SimulatedSeconds - 1.0 / 30) < 1e-6,
            "Frame-rate-dependent timing no longer preserves its deliberate slowdown.");
    }

    internal static void TestSlowFrameLoopReset()
    {
        using Fixture fixture = new(timing: AnimationTimingMode.TimeSynchronized);
        fixture.Entity.SetMotionLayers([new("stand.vmd", 1, true)]);
        fixture.ActivateLayers();
        fixture.TickElapsed(0.9);
        fixture.TickElapsed(0.2);
        Check(fixture.Physics.ResetCount == 1, "A loop reset was lost or repeated across catch-up steps.");
        Check(fixture.Physics.Steps.Count > 0 && fixture.Model.AnimationTimeSeconds > 0.09f,
            "Playback stopped after a loop in a slow frame.");
        fixture.Entity.PauseMotion();
        int steps = fixture.Physics.StepCount;
        fixture.TickElapsed(0.2);
        Check(fixture.Physics.StepCount == steps, "Paused motion continued simulating physics.");
    }

    internal static unsafe void RunProjectPhysics(string projectDirectory, string motionPath, bool compareSkinning = false)
    {
        // Read-only probe using the user's actual dance and character. Compare
        // 32 FPS against batched slow frames through the real Bullet model.
        // 1/32 is exact in both TimeSpan ticks and float, avoiding a one-substep
        // Bullet phase difference caused by rounding 1/30 to TimeSpan ticks.
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(Path.Combine(projectDirectory, "scenes"), "*.json"))
        {
            using JsonDocument scene = JsonDocument.Parse(File.ReadAllText(file));
            foreach (JsonElement entity in scene.RootElement.GetProperty("entities").EnumerateArray())
            {
                if (entity.TryGetProperty("assetPath", out var asset) && asset.GetString() is { } path
                    && path.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase)
                    && entity.TryGetProperty("scripts", out var scripts) && scripts.GetArrayLength() > 0)
                    paths.Add(Path.GetFullPath(Path.Combine(projectDirectory, path)));
            }
        }
        Check(paths.Count > 0, "The project has no scripted PMX character.");
        // Optional CPU-vs-Vulkan probe uses real compute outputs, read back for
        // comparison. It does not change the player's asynchronous render path.
        using var device = compareSkinning
            ? Veldrid.GraphicsDevice.CreateVulkan(new Veldrid.GraphicsDeviceOptions()) : null;
        VulkanRenderer? renderer = compareSkinning ? new() : null;
        if (renderer is not null)
            typeof(VulkanRenderer).GetField("_device", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, device);
        bool previousOpenCl = Mmd.Kernel.UseOpenCL;
        Mmd.Kernel.UseOpenCL = false;
        try
        {
            foreach (string path in paths)
            {
                using Fixture reference = new(timing: AnimationTimingMode.TimeSynchronized);
                using Fixture slow = new(timing: AnimationTimingMode.TimeSynchronized);
                Mmd.PmxModel referenceModel = Load(reference, false);
                Mmd.PmxModel slowModel = Load(slow, compareSkinning);
                float maximumError = 0;
                float maximumVertexError = 0;
                for (int frame = 0; frame < 90; frame++)
                {
                    int steps = new[] { 1, 2, 3, 6 }[frame % 4];
                    for (int i = 0; i < steps; i++) reference.TickElapsed(1.0 / 32);
                    slow.TickElapsed(steps / 32.0);
                    var referenceNodes = referenceModel.GetNodes();
                    var slowNodes = slowModel.GetNodes();
                    for (int i = 0; i < referenceNodes.Length; i++)
                    {
                        float error = Vector3.Distance(referenceNodes[i].Global.Translation, slowNodes[i].Global.Translation);
                        Check(float.IsFinite(error), "Physics produced a non-finite bone transform.");
                        maximumError = Math.Max(maximumError, error);
                    }
                    if (compareSkinning)
                    {
                        Check(slowModel.IsUsingVulkanCompute, "The Vulkan model silently fell back to CPU.");
                        for (int i = 0; i < referenceModel.GetVertexCount(); i++)
                        {
                            float error = Vector3.Distance(referenceModel.GetUpdatePositions()[i], slowModel.GetUpdatePositions()[i]);
                            Check(float.IsFinite(error), "Skinning produced a non-finite vertex.");
                            maximumVertexError = Math.Max(maximumVertexError, error);
                        }
                    }
                }
                Check(maximumError < 0.001f, $"Slow-frame physics diverged from 32 FPS: {maximumError}.");
                Console.WriteLine($"PASS native dance physics: {Path.GetFileName(path)}; max bone position error={maximumError:G6}");
                if (compareSkinning)
                {
                    Check(maximumVertexError < 0.002f, $"CPU/Vulkan vertex positions diverged: {maximumVertexError}.");
                    Console.WriteLine($"PASS CPU/Vulkan dance skinning: {Path.GetFileName(path)}; max vertex position error={maximumVertexError:G6}");
                }

                Mmd.PmxModel Load(Fixture fixture, bool useCompute)
                {
                    Mmd.PmxModel model = new();
                    if (useCompute)
                        model.SkinningComputeFactory = (vertices, bones) => new VulkanPmxSkinningCompute(renderer!, vertices, bones);
                    Check(model.Load(path, Path.GetDirectoryName(path)!), "PMX loading failed.");
                    model.InitializeAnimation();
                    fixture.Entity.SetMotionLayers([new(Path.GetFullPath(Path.Combine(projectDirectory, motionPath)), 1, false)]);
                    fixture.ActivateLayers(model);
                    return model;
                }
            }
        }
        finally { Mmd.Kernel.UseOpenCL = previousOpenCl; }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        public GameEntity Definition { get; }
        public PmxModelComponent Model { get; }
        public RecordingModel Physics { get; } = new();
        public AndroidScriptEntity Entity { get; }
        private long _frame;
        private double _elapsed;
        private readonly Game? _game;

        public Fixture(bool resetDefault = true, AnimationTimingMode? timing = null, Func<string, string>? resolveAssetPath = null)
        {
            string root = Path.GetFullPath(Path.Combine("temp", "android-motion-loop-tests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            foreach (string file in new[] { "unused.pmx", "stand.vmd", "wait.vmd" })
                File.WriteAllBytes(Path.Combine(root, file), []);
            Model = new(Path.Combine(root, "unused.pmx"));
            if (timing is { } mode)
            {
                _game = new TimingGame(mode);
            }
            Definition = new() { Id = "actor", Type = "pmx_model", EnablePhysics = true, LoopMotion = true, ResetPhysicsOnMotionLoop = resetDefault };
            RuntimeEntity coreEntity = new(Definition, _ => { Model.ResetPhysics(); return true; });
            Entity = new(coreEntity, _ => { }, (_, _) => { }, () => Model, resolveAssetPath ?? (path => Path.Combine(root, path)));
            AndroidPmxPlaybackSync.Apply(Definition, Model, initialize: true);
        }

        public void ActivateLayers(Mmd.PmxModel? nativeModel = null)
        {
            // Supply in-memory animation durations and a recording physics model
            // so the real component Update and Android synchronization run without
            // loading a graphics device, Bullet library or external game assets.
            Type componentType = typeof(PmxModelComponent);
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            componentType.GetField("_model", fields)!.SetValue(Model, (Mmd.MMDModel?)nativeModel ?? Physics);
            componentType.GetField("_loaded", fields)!.SetValue(Model, true);
            IList layers = (IList)componentType.GetField("_motionLayers", fields)!.GetValue(Model)!;
            Type layerType = componentType.GetNestedType("MotionLayerState", BindingFlags.NonPublic)!;
            IEnumerable configs = (IEnumerable)componentType.GetField("_initialMotionLayers", fields)!.GetValue(Model)!;
            foreach (object config in configs)
            {
                Type configType = config.GetType();
                Mmd.VmdAnimation animation = new();
                if (nativeModel is null)
                    typeof(Mmd.VmdAnimation).GetProperty(nameof(Mmd.VmdAnimation.MaxKeyTime))!.SetValue(animation, 30 * (layers.Count + 1));
                else
                    Check(animation.Load((string)configType.GetProperty("MotionPath")!.GetValue(config)!, nativeModel), "VMD loading failed.");
                layers.Add(Activator.CreateInstance(layerType,
                    configType.GetProperty("MotionPath")!.GetValue(config), animation,
                    configType.GetProperty("Weight")!.GetValue(config),
                    configType.GetProperty("ResetPhysicsOnLoop")!.GetValue(config))!);
            }
            // Attach only after installing layers, without loading a GPU.
            typeof(GameComponent).GetProperty(nameof(GameComponent.Game))!.SetValue(Model, _game);
        }

        public void Tick(int count)
        {
            for (int i = 0; i < count; i++) TickElapsed(1.0 / 30.0);
        }

        public void TickElapsed(double seconds)
        {
            AndroidPmxPlaybackSync.Apply(Definition, Model, initialize: false);
            _frame++;
            _elapsed += seconds;
            Model.Update(new(TimeSpan.FromSeconds(_elapsed), TimeSpan.FromSeconds(seconds), _frame));
        }

        public void Dispose()
        {
            Model.Dispose();
            Physics.Dispose();
            _game?.Dispose();
        }
    }

    private sealed class TimingGame(AnimationTimingMode mode) : Game(
        new GameOptions { AnimationTimingMode = mode, UseOpenCL = false, EnableAudio = false }, new VulkanRenderer(), default);

    private sealed unsafe class RecordingModel : Mmd.MMDModel
    {
        public int ResetCount { get; private set; }
        public int StepCount { get; private set; }
        public int SkinningCount { get; private set; }
        public double SimulatedSeconds { get; private set; }
        public Func<float>? PlaybackTime { get; set; }
        public List<(float Elapsed, float PlaybackTime)> Steps { get; } = [];
        public override void ResetPhysics() => ResetCount++;
        public override void UpdatePhysicsAnimation(float elapsed)
        {
            StepCount++;
            SimulatedSeconds += elapsed;
            Steps.Add((elapsed, PlaybackTime?.Invoke() ?? 0));
        }
        public override bool HasUvMorphs => false;
        public override int GetVertexCount() => 0;
        public override int GetIndexCount() => 0;
        public override Vector3* GetPositions() => null;
        public override Vector3* GetNormals() => null;
        public override Vector2* GetUVs() => null;
        public override Vector3* GetUpdatePositions() => null;
        public override Vector3* GetUpdateNormals() => null;
        public override Vector2* GetUpdateUVs() => null;
        public override uint* GetIndices() => null;
        public override Mmd.MMDNode[] GetNodes() => [];
        public override Mmd.MMDMorph[] GetMorphs() => [];
        public override Mmd.MMDIkSolver[] GetIkSolvers() => [];
        public override Mmd.MMDMaterial[] GetMaterials() => [];
        public override Mmd.MMDMesh[] GetMeshes() => [];
        public override Mmd.MMDNode? FindNode(Predicate<Mmd.MMDNode> predicate) => null;
        public override Mmd.MMDMorph? FindMorph(Predicate<Mmd.MMDMorph> predicate) => null;
        public override Mmd.MMDIkSolver? FindIkSolver(Predicate<Mmd.MMDIkSolver> predicate) => null;
        public override bool Load(string path, string mmdDataDir) => throw new NotSupportedException();
        public override void InitializeAnimation() { }
        public override void BeginAnimation() { }
        public override void EndAnimation() { }
        public override void UpdateMorphAnimation() { }
        public override void UpdateNodeAnimation(bool afterPhysicsAnim) { }
        public override void Update() => SkinningCount++;
        public override void Destroy() { }
        public override void Dispose() { }
    }
}

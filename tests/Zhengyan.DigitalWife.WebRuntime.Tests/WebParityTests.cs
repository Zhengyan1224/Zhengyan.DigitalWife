using System.Numerics;
using System.Text.Json;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GamePlayer.Web.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd;

internal static class WebParityTests
{
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        string data = Path.Combine(root, "artifacts/web-player/wwwroot/web-game");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var definition = JsonSerializer.Deserialize<GameProjectScene>(File.ReadAllText(Path.Combine(data, "scenes/main.scene.json")), options)!;
        var bodyDefinition = definition.Entities.First(e => e.Name == "Body");
        var clothesDefinition = definition.Entities.First(e => e.Name == "MaidOutfit");
        bodyDefinition.EnablePhysics = clothesDefinition.EnablePhysics = false;
        var bodyPmx = PmxParsing.ParsingByFile(Path.Combine(data, bodyDefinition.AssetPath))!;
        var clothesPmx = PmxParsing.ParsingByFile(Path.Combine(data, clothesDefinition.AssetPath))!;
        using var body = WebPmxAsset.Create(bodyDefinition, bodyPmx, []);
        using var clothes = WebPmxAsset.Create(clothesDefinition, clothesPmx, []);
        using var scene = new RuntimeScene("main", definition, p => p);
        var assets = new Dictionary<string, WebPmxAsset> { [clothes.Entity.Id] = clothes, [body.Entity.Id] = body };
        Check(WebPmxRelations.Apply(scene, assets).Contains(clothes.Entity.Id), "Clothes were not bound when exported before the body.");
        int matched = 0;
        for (int i = 0; i < clothes.Pose.BoneNames.Count; i++)
        {
            int j = body.Pose.BoneNames.ToList().IndexOf(clothes.Pose.BoneNames[i]);
            if (j < 0) continue;
            Check(clothes.Pose.GlobalPose[i] == body.Pose.GlobalPose[j], "Matching body and clothing bones have different poses."); matched++;
        }
        Check(matched > 30, "The real clothing fixture did not exercise bone-name mapping.");
        WebSpeechLipSync lip=new();lip.Configure(new(){Enabled=true},File.ReadAllText(Path.Combine(root,"assets/mmd/engine/Resources/SpeechLipSyncDictionaries/zh_kanadic.txt")),
            File.ReadAllText(Path.Combine(root,"assets/mmd/engine/Resources/SpeechLipSyncDictionaries/voweldic.txt")));
        Check(lip.Vowels("你好").SequenceEqual(new[]{"い","い","あ","お"}),"Chinese longest-match lip-sync dictionary does not produce native vowels.");
        lip.Begin(body,"你好",1);lip.Update(.02f);Check(body.Pose.TryGetManualMorphWeight("い",out float mouth) && mouth>.5f,"Speech did not open the model mouth.");
        lip.Stop();Check(!body.Pose.TryGetManualMorphWeight("い",out _),"Ending speech left a manual morph override on the animated model.");
        body.Update(0,.016f);WebPmxRelations.Apply(scene,assets);
        Check(WebPmxRelations.Apply(scene, assets).Count == 0, "An idle relation uploaded the full clothing mesh again.");
        scene.GetEntity(body.Entity.Id)!.Position = new(3, 2, 1);
        WebPmxRelations.Apply(scene, assets);
        Check(scene.GetEntity(clothes.Entity.Id)!.Position == new Vector3(3, 2, 1), "Relation component transforms were lost.");

        var water = scene.WaterSurfaces.Single();
        foreach (var particle in scene.ParticleSystems) particle.Definition.Particle.EnableWaterInteraction = false;
        water.Definition.Water.EnableInteraction = true;
        var collider = scene.AddEntity(new GameEntity { Id = "contact", Type = "helper", Transform = new() { Position = new(0, water.Position.Y, 0) },
            Colliders = [new() { Shape = "sphere", Position = Vector3Dto.Zero, Radius = .5f }] });
        WebSceneSimulation simulation = new();
        simulation.Update(scene, .016f);
        Check(simulation.GetRipples(water.Id).Count > 0, "A collider touching water did not emit a ripple.");
        int ripples = simulation.GetRipples(water.Id).Count;
        simulation.Update(scene, .016f);
        Check(simulation.GetRipples(water.Id).Count == ripples, "A stationary collider bypassed the contact throttle.");
        var mesh = WebSceneSimulation.CreateWaterMesh(water.Definition.Water);
        Check(mesh.Positions.Length / 3 == (Math.Clamp(water.Definition.Water.GerstnerMeshResolution, 8, 256) + 1) * (Math.Clamp(water.Definition.Water.GerstnerMeshResolution, 8, 256) + 1), "Water has no subdivided wave mesh.");
        var waves = new WaterSurfaceSettings();
        WebSceneSimulation.EvaluateWaves(waves, 1.2f, 2.4f, 0, out var a, out var normal);
        WebSceneSimulation.EvaluateWaves(waves, 1.2f, 2.4f, 1, out var b, out _);
        Check(a != b && MathF.Abs(normal.Length() - 1) < 1e-5f, "Gerstner geometry and normals do not animate.");
        Check(!WebSceneSimulation.TrySurfaceHeight(water, new(100000, -1, 100000), 0, out _), "Underwater extends outside the water bounds.");

        WebLoadedProject loaded = new(new(), definition, scene, new(), assets, new Dictionary<string, WebPmxMesh> { [water.Id] = mesh },
            new Dictionary<string, WebAudioAsset>(), new Dictionary<string, string>(), new());
        loaded.Simulation.Update(scene, .016f);
        var models = new[] { (water, mesh) };
        var frame = WebRenderFrame.FromScene(scene, models, 1280, 720).WithSceneEffects(loaded, models, 1280, 720);
        Check(frame.Cameras.Count == 2 && frame.Cameras[1].Viewport.SequenceEqual(new[] { 867, 21, 384, 207 }), "The second camera viewport differs from PC.");
        Check(frame.ParticleBatches.Sum(p => p.Vertices.Count) > 128, "Particle counts are still truncated to the prototype limit.");
        Check(frame.Particles.Count == 0, "Prototype particles were drawn on top of the real simulation.");
        Check(frame.RenderTextures.Count == 1 && frame.RenderTextures[0].Camera.Viewport.SequenceEqual(new[] {0,0,512,512}), "RenderTexture camera dimensions were lost.");
        var meshCollider = new WebMeshColliders();
        WebPmxMesh geometry = new([-5,-5,0,5,-5,0,5,5,0,-5,5,0],[],[],[0,1,2,0,2,3],new(-5,-5,0),new(5,5,0),"collider");
        var plane = scene.AddEntity(new() {Id="mesh-plane",Type="textured_plane",Transform=new(){Position=new(0,0,-5)}});
        var collision = meshCollider.Resolve(plane,new(){Shape="mesh",Size=new(1,1,1)},geometry)!.Value;
        Check(RuntimePhysics.TryRaycastMesh(new(new(0,0,0),-Vector3.UnitZ),collision,out float distance,out _) && Math.Abs(distance-5)<1e-4f,"Mesh collider geometry is not registered in world space.");
        scene.MainCamera.Position = new(0, water.Position.Y - 1, 0);
        frame = WebRenderFrame.FromScene(scene, models, 1280, 720).WithSceneEffects(loaded, models, 1280, 720);
        Check(frame.Cameras[0].Underwater is not null, "A submerged camera did not enable the underwater pass.");
        Directory.CreateDirectory(Path.Combine(root, "temp/web-debug"));
        File.WriteAllText(Path.Combine(root, "temp/web-debug/parity-frame.json"), JsonSerializer.Serialize(frame, options));
        Console.WriteLine($"PASS real PMX clothing relation ({matched} bones), idle uploads, wave mesh, contact ripples, bounded underwater, particles and second camera");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

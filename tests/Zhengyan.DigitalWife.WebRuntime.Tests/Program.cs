using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Zhengyan.DigitalWife.GamePlayer.Web.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd;

if (args.Contains("--parity"))
{
    WebParityTests.Run();
    return 0;
}

if (args.Contains("--script-bcl"))
{
    foreach(var type in new[]{typeof(DateTimeOffset),typeof(DateTime),typeof(TimeSpan),typeof(Guid),typeof(List<>),typeof(Dictionary<,>),typeof(Queue<>),typeof(HashSet<>),typeof(System.Text.StringBuilder),typeof(System.Linq.Enumerable)})
        Console.WriteLine($"{type.FullName}: {type.Assembly.GetName().Name}");
    return 0;
}

if (args.Contains("--encrypted-fixture"))
{
    string directory=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../temp/web-debug/encrypted-fixture-project"));
    Directory.CreateDirectory(Path.Combine(directory,"assets"));Directory.CreateDirectory(Path.Combine(directory,"scenes"));
    File.WriteAllText(Path.Combine(directory,"game.project.json"),"{\"defaultScene\":\"scenes/main.scene.json\",\"scenes\":[\"scenes/main.scene.json\"]}");
    File.WriteAllText(Path.Combine(directory,"scenes/main.scene.json"),"{\"name\":\"Encrypted fixture\"}");
    File.WriteAllBytes(Path.Combine(directory,"assets/random.bin"),RandomNumberGenerator.GetBytes(2200000));
    GameProjectPackage.Create(directory,new(){OutputPath=Path.Combine(directory,"../encrypted-fixture.dwgame"),Password="fixture-password"});
    Console.WriteLine("Created native multi-chunk AES-GCM fixture.");return 0;
}

if (args.Contains("--services")) { await WebScriptServiceTests.RunAsync(); return 0; }

if (args.Contains("--assets"))
{
    await WebAssetCacheTests.RunAsync();
    Console.WriteLine("Web asset cache regression tests passed.");
    return 0;
}

if (args.Contains("--surfaces"))
{
    VerifyModuleBuildVersion();
    await VerifyRenderSurfacesAsync();
    VerifyShadowCoverage();
    Console.WriteLine("Web surface regression tests passed.");
    return 0;
}

VerifyVertexUpdates();
await WebScriptServiceTests.RunAsync();
await WebAssetCacheTests.RunAsync();
VerifyModuleBuildVersion();
VerifyMaterialInterop();
VerifyIdlePhysicsAndPlayback();
VerifyShadowCoverage();
await VerifyRenderSurfacesAsync();
VerifyDanceScript();
VerifyGpuPlayback();
await VerifyBulletBridgeAsync();
await VerifyFrameInputAndStartupAsync();
await VerifyFrameInputAndStartupAsync(includeModels: true);
Console.WriteLine("Web runtime C# regression tests passed.");
return 0;

static void VerifyModuleBuildVersion()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    string hashes = typeof(WebRuntimeHost).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "WebRuntimeAssets").Value!;
    string[] files = Directory.GetFiles(Path.Combine(root, "src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js"), "*.js");
    Check(hashes.Length == files.Length * 64, "The assembly version is not linked to every JS module.");
    foreach (string file in files)
        Check(hashes.Contains(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))), StringComparison.OrdinalIgnoreCase), "A JS module hash was omitted from the WASM build version.");
    Check(Guid.TryParseExact(WebRuntimeModuleUrl.Version,"N",out _), "The runtime build ID is invalid.");
    Check(WebRuntimeModuleUrl.Resolve("web-audio.js").EndsWith("?v=" + WebRuntimeModuleUrl.Version), "C# audio and JS runtime URLs use different builds.");
    Console.WriteLine("PASS all JS module hashes contribute to the WASM build ID");
}

static async Task VerifyRenderSurfacesAsync()
{
    GameProjectScene definition = new()
    {
        Entities = [
            new() { Id = "mirror", Type = "textured_plane", Transform = new() { Position = new(0, 1.78f, 0) },
                Plane = new() { Width = 5, Height = 5, MirrorReflectionEnabled = true, MirrorReflectionSamples = 8 } },
            new() { Id = "water", Type = "water_surface", Water = new() { MirrorReflectionSamples = 2 } },
            new() { Id = "point", Type = "point_light", Transform = new() { Position = new(0, 0, 2) },
                PointLight = new() { CastShadows = true, Range = 200 } },
            new() { Id = "spot", Type = "spot_light", Transform = new() { Position = new(0, 0, 2) },
                SpotLight = new() { CastShadows = true, Range = 35.1f, OuterConeAngleDegrees = 32.21f } }
        ]
    };
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    Dictionary<string, byte[]> assets = new()
    {
        ["game.project.json"] = JsonSerializer.SerializeToUtf8Bytes(new GameProject { DefaultScene = "scenes/main.scene.json",
            Shadows = new() { DirectionalResolution = 4096, LocalLightResolution = 512, FilterSamples = 25, FilterRadius = 3 } }, options),
        ["scenes/main.scene.json"] = JsonSerializer.SerializeToUtf8Bytes(definition, options)
    };
    WebMemoryAssetStore store = new(new WebAssetManifest
    {
        Assets = assets.Select(item => new WebAssetEntry { Path = item.Key, Size = item.Value.Length }).ToList()
    });
    foreach (var item in assets) store.Add(item.Key, item.Value);
    WebLoadedProject loaded = await new WebProjectLoader(store).LoadAsync();
    using var scene = loaded.RuntimeScene;
    WebPmxMesh plane = loaded.SceneMeshes["mirror"], water = loaded.SceneMeshes["water"];
    Check(plane.Positions.Where((_, i) => i % 3 == 2).All(z => z == 0), "The unrotated mirror must lie in the desktop XY plane.");
    Check(plane.BoundsMin == new Vector3(-2.5f, -2.5f, 0) && plane.BoundsMax == new Vector3(2.5f, 2.5f, 0), "Vertical plane bounds are incorrect.");
    Check(plane.Materials.Single().DoubleSided && !plane.Materials.Single().EnableShadow, "The desktop plane is double-sided and does not cast PMX shadows.");
    Check(water.Positions.Where((_, i) => i % 3 == 1).All(y => y == 0), "Water was rotated with the vertical mirror.");
    var models = loaded.SceneMeshes.Select(item => (scene.GetEntity(item.Key)!, item.Value)).ToArray();
    WebRenderFrame frame = WebRenderFrame.FromScene(scene, models, 1280, 720, shadows: loaded.Project.Shadows);
    Check(frame.ShadowResolution == 4096 && frame.LocalShadowResolution == 512 && frame.ShadowFilterSamples == 25 && frame.ShadowFilterRadius == 3,
        "Global project shadow quality did not reach the Web frame.");
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    Directory.CreateDirectory(Path.Combine(root, "temp/web-debug"));
    File.WriteAllText(Path.Combine(root, "temp/web-debug/surface-frame.json"), JsonSerializer.Serialize(frame, options));
    Check(frame.Reflections.Count == 2 && frame.Meshes.Single(m => m.Id == "mirror").TexturedPlane, "Mirror rendering state did not reach the browser.");
    Check(frame.Reflections.Single(r => r.Id == "mirror").Samples == 8
        && frame.Reflections.Single(r => r.Id == "water").Samples == 2, "Per-surface MSAA was lost loading the Web project.");
    Check(frame.Meshes.Single(m => m.Id == "water").WaterSurface
        && frame.Meshes.Single(m => m.Id == "water").MirrorReflectionStrength > 0, "Water reflection state did not reach WebGL.");
    Check(frame.Reflections.Single(r => r.Id == "water").CameraPosition[1] == -scene.MainCamera.Position.Y, "The water camera was not reflected across its height.");
    Check(frame.LocalShadows.Count == 7 && frame.LocalShadows.Take(6).Select(s => s.Face).SequenceEqual(Enumerable.Range(0, 6)), "A point light requires six faces, followed by one spot tile.");
    Vector3[] faces = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
    for (int face = 0; face < 6; face++)
    {
        WebLocalShadow shadow = frame.LocalShadows[face];
        float[] m = shadow.ViewProjection;
        Matrix4x4 matrix = new(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);
        Vector4 clip = Vector4.Transform(new Vector4(new Vector3(0,0,2) + faces[face] * 2, 1), matrix);
        Check(clip.W > 0 && MathF.Abs(clip.X) < 1e-5f && MathF.Abs(clip.Y) < 1e-5f && MathF.Abs(clip.Z / clip.W) < 1, "Point shadow cube face or WebGL depth is incorrect.");
        Check(shadow.NearPlane == .25f && shadow.FarPlane == 200, "Local shadow clipping differs from PC.");
        Check(shadow.AtlasRect.All(v => v > 0 && v < 1), "Shadow atlas padding is invalid.");
    }
    Check(frame.Reflections.Single(r => r.Id == "mirror").CameraPosition[2] == -scene.MainCamera.Position.Z, "The mirror camera is not reflected across the surface.");
    Check(frame.Reflections.Single(r => r.Id == "mirror").ClipPlane[2] > 0, "Front-side reflection retained geometry from behind the mirror.");
    scene.MainCamera.Position = new(0,2,-8); scene.MainCamera.Target = new(0,1.2f,0);
    frame = WebRenderFrame.FromScene(scene, models, 1280,720);
    Check(frame.Reflections.Single(r => r.Id == "mirror").CameraPosition[2] == 8 && frame.Reflections.Single(r => r.Id == "mirror").ClipPlane[2] < 0, "Back-side mirror reflection or clipping failed.");
    var mirror = scene.GetEntity("mirror")!;
    mirror.RotationDegrees = new(25, 37, 12);
    frame = WebRenderFrame.FromScene(scene, models, 1280,720);
    var reflection = frame.Reflections.Single(r => r.Id == "mirror");
    Vector3 normal = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, mirror.TransformMatrix));
    Vector3 source = scene.MainCamera.Position, reflected = new(reflection.CameraPosition);
    Check(MathF.Abs(Vector3.Dot(normal, source - mirror.Position) + Vector3.Dot(normal, reflected - mirror.Position)) < 1e-4f, "Rotated mirror does not reflect the camera across its transformed plane.");
    mirror.Definition.Plane.Billboard = true;
    frame = WebRenderFrame.FromScene(scene, models, 1280,720);
    Check(frame.Meshes.Single(m => m.Id == "mirror").Model.Skip(12).Take(3).SequenceEqual(new[] { 0f,1.78f,0f }), "Billboard transform moved the mirror origin.");
    Console.WriteLine("PASS vertical/double-sided planes, horizontal water, rotated/back-side mirrors and seven local shadow tiles");
}

static void VerifyVertexUpdates()
{
    WebPmxMesh mesh = new(
        [1, 2, 3, 4, 5, 6], [0, 0, 1, 0, 1, 0], [0.1f, 0.2f, 0.3f, 0.4f],
        [0u, 1u, 0u], Vector3.Zero, Vector3.One, "test");
    WebPmxMeshUpdate first = mesh.CreateUpdate();
    Check(first.Vertices.Length == 2 * 32, "The vertex update has an incorrect binary stride.");
    float[] firstValues = MemoryMarshal.Cast<byte, float>(first.Vertices).ToArray();
    Check(firstValues.SequenceEqual(new float[] { 1, 2, 3, 0, 0, 1, .1f, .2f, 4, 5, 6, 0, 1, 0, .3f, .4f }), "Initial mesh data was interleaved incorrectly.");

    float[] posed = [10, 20, 30, 0, 0, 1, .2f, .25f, 40, 50, 60, 0, 1, 0, .4f, .6f];
    mesh.UpdateFromInterleaved(posed);
    WebPmxMeshUpdate updated = mesh.CreateUpdate();
    float[] updatedValues = MemoryMarshal.Cast<byte, float>(updated.Vertices).ToArray();
    Check(updatedValues[7] == .75f && updatedValues[15] == 1.0f - .6f, "Posed UVs were not flipped exactly once.");
    Check(mesh.Uvs[1] == updatedValues[7] && mesh.Positions[0] == updatedValues[0], "Binary and mesh-array values disagree.");
    Check(MemoryMarshal.Cast<byte, float>(first.Vertices).SequenceEqual(firstValues), "An update mutated bytes already queued for upload.");
    Check(!ReferenceEquals(first.Vertices, updated.Vertices), "Successive frame updates share a mutable byte buffer.");
    Console.WriteLine("PASS binary PMX vertex updates and UV orientation");
}

static void VerifyMaterialInterop()
{
    WebPmxMaterial material = new(0, 3, new Vector4(.8f,.7f,.6f,.5f), new Vector3(.2f,.3f,.4f),
        new Vector4(.1f,.2f,.3f,.4f), 1, false, true, true, true, null, "color")
    { Specular = new Vector3(.5f,.6f,.7f), SpecularPower = 4 };
    using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(material, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    Check(json.RootElement.GetProperty("diffuse").EnumerateArray().Select(v => v.GetSingle()).SequenceEqual(new[] { .8f,.7f,.6f,.5f }), "Diffuse color was lost in JS serialization.");
    Check(json.RootElement.GetProperty("ambient").EnumerateArray().Select(v => v.GetSingle()).SequenceEqual(new[] { .2f,.3f,.4f }), "Ambient color was lost in JS serialization.");
    Check(json.RootElement.GetProperty("edgeColor").GetArrayLength() == 4 && json.RootElement.GetProperty("specular").GetArrayLength() == 3, "Edge or specular vectors were lost in JS serialization.");
    Console.WriteLine("PASS PMX material vectors survive field-free browser JSON serialization");
}

static void VerifyIdlePhysicsAndPlayback()
{
    string data = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../assets/mmd/samples/GameData"));
    PmxParsing pmx = PmxParsing.ParsingByFile(Path.Combine(data, "Character/Body/标准身体.pmx"))
        ?? throw new InvalidDataException("Character fixture failed to parse.");
    VmdParsing motion = VmdParsing.ParsingByFile(Path.Combine(data, "Motion/Dance/Lamb/Lamb.vmd"))
        ?? throw new InvalidDataException("Motion fixture failed to parse.");
    WebPmxMesh materialMesh = WebPmxMesh.FromParsing(pmx);
    Check(materialMesh.Materials.Select(material => material.EnableShadow).SequenceEqual(
        pmx.Materials.Select(material => material.DrawMode.HasFlag(PmxDrawModeFlags.CastSelfShadow))), "Directional shadow casters do not match PC material flags.");
    GameEntity definition = new() { EnablePhysics = true, IsPlaying = true };
    using (WebPmxAsset idle = WebPmxAsset.Create(definition, pmx, []))
    {
        float[] rest = idle.Mesh.Positions.ToArray();
        for (int i = 0; i < 180; i++) Check(!idle.Update(i / 30.0, 1.0f / 30), "A model with no motion layer was solved or uploaded.");
        Check(idle.PhysicsBackend == "disabled" && rest.SequenceEqual(idle.Mesh.Positions), "Gravity deformed the idle character.");
    }
    definition.IsPlaying = false;
    using WebPmxAsset animated = WebPmxAsset.Create(definition, pmx, [(motion, 1)]);
    float[] frameZero = animated.Mesh.Positions.ToArray();
    Check(!animated.Update(100, 1.0f / 30), "An initially paused motion was updated.");
    animated.PlayMotion(restart: false);
    Check(animated.Update(101, 1.0f / 30) && animated.PhysicsBackend == "disabled", "The first play frame must skip physics.");
    Check(animated.Pose.GetMotionLayerFrame(0) == 0, "Starting playback advanced across idle wall time.");
    animated.Update(101.1, 1.0f / 30);
    Check(animated.PhysicsBackend != "disabled", "Physics did not start during motion playback.");
    float playingFrame = animated.Pose.GetMotionLayerFrame(0);
    animated.PlayMotion(restart: false);
    animated.Update(101.2, 1.0f / 30);
    Check(animated.Pose.GetMotionLayerFrame(0) > playingFrame && animated.PhysicsBackend != "disabled", "Calling Play on an active motion interrupted its clock or physics.");
    float pausedFrame = animated.Pose.GetMotionLayerFrame(0);
    float[] pausedVertices = animated.Mesh.Positions.ToArray();
    animated.PauseMotion();
    Check(!animated.Update(200, 1.0f / 30) && pausedVertices.SequenceEqual(animated.Mesh.Positions), "Pausing changed the frozen pose.");
    animated.PlayMotion(restart: false);
    animated.Update(201, 1.0f / 30);
    Check(animated.Pose.GetMotionLayerFrame(0) == pausedFrame, "Resuming skipped the paused interval.");
    animated.StopMotion();
    Check(animated.Update(202, 1.0f / 30), "Stopping failed to upload the frame-zero pose.");
    Check(animated.PhysicsBackend == "disabled" && frameZero.SequenceEqual(animated.Mesh.Positions), "Stopping failed to restore the initial pose without gravity.");
    Check(!animated.Update(203, 1.0f / 30), "A stopped pose was repeatedly uploaded.");
    Console.WriteLine("PASS real character idle physics, first play frame, pause, resume and stop");
}

static void VerifyShadowCoverage()
{
    GameProjectScene definition = new();
    definition.Lighting.ShadowColor = new Vector4Dto { W = .35f };
    GameEntity actor = new()
    {
        Id = "far-model", Type = "pmx_model",
        Transform = new() { Position = Vector3Dto.FromVector3(new Vector3(1000, 40, -700)), Scale = Vector3Dto.FromVector3(new Vector3(2, 3, 4)) }
    };
    definition.Entities.Add(actor);
    using Zhengyan.DigitalWife.GamePlayer.Runtime.RuntimeScene scene = new("test.scene.json", definition, path => path);
    WebPmxMesh mesh = new([], [], [], [], new Vector3(-5, 0, -5), new Vector3(5, 30, 5), "bounds");
    WebRenderFrame frame = WebRenderFrame.FromScene(scene, [(scene.GetEntity(actor.Id)!, mesh)], 1280, 720);
    Check(frame.ShadowStrength == .35f, "Scene shadow opacity was lost.");
    float[] a = frame.LightViewProjection;
    Matrix4x4 light = new(a[0],a[1],a[2],a[3], a[4],a[5],a[6],a[7], a[8],a[9],a[10],a[11], a[12],a[13],a[14],a[15]);
    foreach (float x in new[] { -5f, 5f })
    foreach (float y in new[] { 0f, 30f })
    foreach (float z in new[] { -5f, 5f })
    {
        Vector3 world = Vector3.Transform(new Vector3(x, y, z), scene.GetEntity(actor.Id)!.TransformMatrix);
        Vector4 clip = Vector4.Transform(new Vector4(world, 1), light);
        Check(MathF.Abs(clip.X / clip.W) <= 1 && MathF.Abs(clip.Y / clip.W) <= 1 && MathF.Abs(clip.Z / clip.W) <= 1,
            "A transformed model corner was outside the directional shadow map.");
    }
    Console.WriteLine("PASS directional shadow coverage includes translated and scaled model bounds");
}

static void VerifyDanceScript()
{
    string data = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/web-player/wwwroot/web-game"));
    Assembly assembly = Assembly.Load(File.ReadAllBytes(Path.Combine(data, "compiled/web/scripts/MaidOutfit.dll")));
    Type type = assembly.GetTypes().First(t => typeof(Zhengyan.DigitalWife.GamePlayer.Runtime.IWebGameScript).IsAssignableFrom(t));
    var module = (Zhengyan.DigitalWife.GamePlayer.Runtime.IWebGameScript)Activator.CreateInstance(type)!;
    int speech = 0;
    GameProjectScene definition = new() { Entities = [new GameEntity { Id = "actor", Type = "pmx_model" }] };
    using var scene = new Zhengyan.DigitalWife.GamePlayer.Runtime.RuntimeScene("test", definition, p => p,
        speechRequest: (_, request) => { if (!string.IsNullOrEmpty(request.Text) && request.Completion is not null) speech++; });
    var context = new Zhengyan.DigitalWife.GamePlayer.Runtime.WebScriptContext(scene, scene.GetEntity("actor"));
    module.OnEvent(context, new("gui", ControlName: "button_dance", EventName: "clicked"));
    Check(speech == 1, "The actual exported GUI script did not emit a speech request and callback.");
    Console.WriteLine("PASS actual exported GUI script speech event and callback");
}

static void VerifyGpuPlayback()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    string data = Path.Combine(root,"assets/mmd/samples/GameData");
    PmxParsing pmx = PmxParsing.ParsingByFile(Path.Combine(data,"Character/Body/标准身体.pmx"))!;
    VmdParsing motion = VmdParsing.ParsingByFile(Path.Combine(data,"Motion/Dance/Lamb/Lamb.vmd"))!;
    GameEntity definition = new() { IsPlaying = true, EnablePhysics = true };
    using WebPmxAsset gpu = WebPmxAsset.Create(definition,pmx,[(motion,1)],preferGpuSkinning:true);
    using PmxPoseEvaluator full = new(pmx,[(motion,1)],true,definition.PhysicsGravity,definition.ResetPhysicsOnMotionLoop);
    Check(gpu.GpuSkinning,"The real BDEF character did not select GPU skinning.");
    int[] selected = pmx.Vertices.Select((v,i)=>(v,i)).GroupBy(item=>item.v.WeightType).SelectMany(group=>group.Take(24).Select(item=>item.i))
        .Concat(pmx.Morphs.SelectMany(m=>m.PositionMorphs).Select(m=>m.VertexIndex).Distinct().Take(96)).Distinct().ToArray();
    float[] bindings = MemoryMarshal.Cast<byte,float>(gpu.Mesh.Skinning!.Bindings).ToArray();
    float[] vertexOutput = new float[pmx.Vertices.Length * 8];
    List<object> frames = [];
    // Compare full and dependency-based IK solving before involving the GPU.
    for (int frame = 0; frame <= 45; frame++)
    {
        full.PhysicsSimulationEnabled = frame > 0;
        full.Update(frame/30.0,1,true,vertexOutput,1.0f/30);
        gpu.Update(frame/30.0,1.0f/30);
        float matrixError = 0;
        ReadOnlySpan<float> expected = MemoryMarshal.Cast<Matrix4x4,float>(full.SkinTransforms);
        ReadOnlySpan<float> actual = MemoryMarshal.Cast<Matrix4x4,float>(gpu.Pose.SkinTransforms);
        for (int i=0;i<expected.Length;i++) matrixError = MathF.Max(matrixError,MathF.Abs(expected[i]-actual[i]));
        Check(float.IsFinite(matrixError) && matrixError < .0001f,$"Partial IK changed the pose at frame {frame}: {matrixError}");
        WebPmxMeshUpdate update = gpu.Mesh.CreateUpdate();
        Check(update.Vertices.Length == 0 && update.Bones?.Length == pmx.Bones.Length * 64,"Animated GPU poses uploaded full skinned vertices.");
        if (frame is 0 or 15 or 45)
        {
            float[] morphs = selected.SelectMany((index,i) => { Vector3 offset = gpu.Pose.VertexMorphOffsets[index]; return new[] { (float)i,offset.X,offset.Y,offset.Z }; }).ToArray();
            float[] expectedVertices = selected.SelectMany(index=>vertexOutput.AsSpan(index*8,6).ToArray()).ToArray();
            frames.Add(new { bones = update.Bones, morphs = MemoryMarshal.AsBytes(morphs.AsSpan()).ToArray(), expected = expectedVertices });
        }
    }
    object fixture = new {
        positions = selected.SelectMany(index=>gpu.Mesh.Positions.AsSpan(index*3,3).ToArray()).ToArray(),
        normals = selected.SelectMany(index=>gpu.Mesh.Normals.AsSpan(index*3,3).ToArray()).ToArray(),
        uvs = selected.SelectMany(index=>gpu.Mesh.Uvs.AsSpan(index*2,2).ToArray()).ToArray(), indices = new[] {0,1,2},
        skinning = new { boneCount = pmx.Bones.Length, bindings = MemoryMarshal.AsBytes(selected.SelectMany(index=>bindings.AsSpan(index*8,8).ToArray()).ToArray().AsSpan()).ToArray(), bones = gpu.Mesh.Skinning.Bones },
        frames
    };
    Directory.CreateDirectory(Path.Combine(root,"temp/web-debug"));
    File.WriteAllText(Path.Combine(root,"temp/web-debug/gpu-skinning-fixture.json"),JsonSerializer.Serialize(fixture));
    Console.WriteLine("PASS real character GPU packets and partial IK match full CPU solving across 46 animation frames");
}

static async Task VerifyBulletBridgeAsync()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    string data = Path.Combine(root,"assets/mmd/samples/GameData");
    PmxParsing pmx = PmxParsing.ParsingByFile(Path.Combine(data,"Character/Body/标准身体.pmx"))!;
    VmdParsing motion = VmdParsing.ParsingByFile(Path.Combine(data,"Motion/Dance/Lamb/Lamb.vmd"))!;
    RecordingBulletModule module = new();
    WebPmxBulletPhysics physics = await WebPmxBulletPhysics.CreateAsync(module,pmx,new(0,-98,0));
    GameEntity entity = new() { IsPlaying=true,EnablePhysics=true };
    using (WebPmxAsset asset = WebPmxAsset.Create(entity,pmx,[(motion,1)],preferGpuSkinning:true,physicsBridge:physics))
    {
        for (int frame=0;frame<360;frame++) asset.Update(frame/60.0,1.0f/60);
        Check(asset.PhysicsBackend=="Bullet WASM","The Web asset used the translation fallback instead of Bullet.");
        Check(module.Frames.Count==359 && module.Resets==1,"First-frame/reset handling did not reach the Bullet world once.");
        int before=module.Frames.Count;
        asset.PauseMotion(); asset.Update(20,1.0f/60);
        Check(module.Frames.Count==before,"Paused animation advanced Bullet.");
        asset.PlayMotion(false); asset.Update(21,1.0f/60); asset.Update(21.2,.2f);
        Check(module.Frames.Count-before>=6 && module.Elapsed.All(delta=>delta<=1.0f/30+.000001f),"Slow-frame motion/physics targets were not substepped together.");
        asset.StopMotion(); asset.Update(22,1.0f/60);
        Check(asset.PhysicsBackend=="disabled","Stopping left Bullet enabled.");
        File.WriteAllText(Path.Combine(root,"temp/web-debug/pmx-bullet-definition.json"),module.Definition);
        File.WriteAllBytes(Path.Combine(root,"temp/web-debug/pmx-bullet-targets.bin"),MemoryMarshal.AsBytes(module.Frames.Take(359).SelectMany(frame=>frame).ToArray().AsSpan()).ToArray());
    }
    Check(module.Destroyed==1,"Shared cached motion poses disposed the Bullet world more than once.");
    Console.WriteLine("PASS Bullet bridge first-frame, pause, stop, substeps, world lifetime and real PMX fixture");
}

static async Task VerifyFrameInputAndStartupAsync(bool includeModels = false)
{
    Dictionary<string, byte[]> assets = new()
    {
        ["game.project.json"] = Encoding.UTF8.GetBytes("""
            {"defaultScene":"scenes/main.scene.json","scenes":["scenes/main.scene.json"],"window":{"width":1280,"height":720}}
            """),
        ["scenes/main.scene.json"] = Encoding.UTF8.GetBytes("""
            {"name":"Input test","camera":{"position":{"x":0,"y":2,"z":8},"target":{"x":0,"y":1,"z":0},"controlMode":"editor","mouseSensitivity":0.15},"entities":[]}
            """)
    };
    if (includeModels)
    {
        // Four vertices and one root bone; keep the fixture self-contained.
        byte[] pmx = Convert.FromBase64String("UE1YIAAAAEAIAQAEBAQEBAQPAAAAV2ViIE1pbmltYWwgUE1YDwAAAFdlYiBNaW5pbWFsIFBNWA8AAABTdGFnZSAzIGZpeHR1cmUPAAAAU3RhZ2UgMyBmaXh0dXJlBAAAAAAAgL8AAIC/AAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAAIA/AACAPwAAgL8AAAAAAAAAAAAAAAAAAIA/AACAPwAAAAAAAAAAAAAAgD8AAIA/AACAPwAAAAAAAAAAAAAAAAAAgD8AAIA/AACAPwAAAAAAAACAPwAAgL8AAIA/AAAAAAAAAAAAAAAAAACAPwAAAAAAAIA/AAAAAAAAAIA/BgAAAAAAAAABAAAAAgAAAAAAAAACAAAAAwAAAAAAAAABAAAADAAAAFdlYiBNYXRlcmlhbAwAAABXZWIgTWF0ZXJpYWwAAIA/AACAPwAAgD8AAIA/zcxMPs3MTD7NzEw+AAAAQc3MTD7NzEw+zcxMPgAAAAAAAAAAAAAAAAAAAIA/AACAP///////////AAEAAAAAAAYAAAABAAAABAAAAHJvb3QEAAAAcm9vdAAAAAAAAAAAAAAAAP////8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==");
        assets["assets/first.pmx"] = pmx;
        assets["assets/second.pmx"] = pmx;
        assets["scenes/main.scene.json"] = Encoding.UTF8.GetBytes("""
            {"name":"Input test","camera":{"position":{"x":0,"y":2,"z":8},"target":{"x":0,"y":1,"z":0},"controlMode":"editor","mouseSensitivity":0.15},"entities":[{"id":"first","name":"First","type":"pmx_model","assetPath":"assets/first.pmx","enablePhysics":false},{"id":"second","name":"Second","type":"pmx_model","assetPath":"assets/second.pmx","enablePhysics":false}]}
            """);
    }
    WebAssetManifest manifest = new()
    {
        Assets = assets.Select(asset => new WebAssetEntry
        {
            Path = asset.Key, Kind = "json", Size = asset.Value.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(asset.Value)).ToLowerInvariant()
        }).ToList()
    };
    WebMemoryAssetStore store = new(manifest);
    foreach ((string assetPath, byte[] bytes) in assets) store.Add(assetPath, bytes);
    FakeBrowser browser = new();
    await using WebRuntimeHost host = new(browser, new WebProjectLoader(store), new WebAudioService(browser), new WebSpeechService(browser));
    await host.StartAsync(default(ElementReference), default(ElementReference));
    Check(host.Snapshot.IsRunning && host.Snapshot.RendererReady, $"Runtime failed to start: {host.Snapshot.LastError}");
    Check(browser.StartupFrameAttempted && host.Snapshot.FrameCount == 0, "Scene updates ran before mesh/overlay initialization completed.");
    Console.WriteLine("PASS startup does not advance a partially loaded scene");
    if (includeModels)
    {
        Check(host.Snapshot.PmxEntityCount == 2, "The scene did not load both model assets.");
        Check(browser.MeshUploadArgumentCount == 1, "The model array was expanded into separate JavaScript arguments.");
        Check(browser.UploadedMeshes is { Length: 2 }, "JavaScript did not receive both models in its meshes argument.");
        Check(browser.UploadedMeshes!.Select(mesh => mesh.Id).SequenceEqual(new[] { "first", "second" }), "Model IDs were lost at the JavaScript boundary.");
        Check(browser.LastFrame!.Meshes.Count == 2, "The render frame did not include both models.");
        Console.WriteLine("PASS all PMX models are uploaded in a single JavaScript argument");
    }

    await host.OnAnimationFrameWithInput(1000, null);
    float[] original = browser.LastFrame!.ViewProjection.ToArray();
    await host.OnAnimationFrameWithInput(1016, new WebPointerFrameInput(40, 10, 0, 0, 0, 50, 20, 1));
    float[] moved = browser.LastFrame!.ViewProjection.ToArray();
    Check(!original.SequenceEqual(moved), "Frame-batched mouse input did not reach the camera.");
    await host.OnAnimationFrameWithInput(1032, null);
    Check(moved.Zip(browser.LastFrame!.ViewProjection).All(pair => MathF.Abs(pair.First - pair.Second) < 1e-5f), "A gesture was consumed more than once.");
    Check(host.Snapshot.FrameCount == 3, "The frame counter did not advance.");
    await host.OnCanvasResized(874, 491, 1);
    Check(browser.LastFrame!.Cameras[0].Viewport.SequenceEqual(new[] {0,0,874,491}), "Resizing left the browser with an old camera viewport.");
    Console.WriteLine("PASS frame-batched camera input is applied once");
    if(includeModels)
    {
        var loaded=(WebLoadedProject)typeof(WebRuntimeHost).GetField("_loadedProject",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(host)!;
        foreach(var asset in loaded.PmxAssets.Values)asset.ResetPhysics();
        await host.OnAnimationFrameWithInput(1048,null);
        Check(browser.MeshUpdateArgumentCount==1 && browser.UpdatedMeshes?.Length==2,"Animated body/clothes updates were expanded into separate JavaScript arguments.");
        Console.WriteLine("PASS all changed body/clothes poses cross JavaScript as one update array");
    }

    await host.OnBrowserWarning("Unsupported optional texture");
    Check(host.Snapshot.IsRunning && host.Snapshot.RendererReady, "A texture warning disabled the renderer.");
    Check(host.Snapshot.LastError == "Unsupported optional texture", "The texture warning was lost.");
    Console.WriteLine("PASS texture warnings preserve the running scene");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class FakeBrowser : IJSRuntime, IJSObjectReference
{
    private WebRuntimeHost? _host;
    public bool StartupFrameAttempted { get; private set; }
    public WebRenderFrame? LastFrame { get; private set; }
    public int MeshUploadArgumentCount { get; private set; }
    public WebPmxMeshInstance[]? UploadedMeshes { get; private set; }
    public int MeshUpdateArgumentCount { get; private set; }
    public WebPmxMeshUpdateInstance[]? UpdatedMeshes { get; private set; }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (identifier == "start")
        {
            _host = ((DotNetObjectReference<WebRuntimeHost>)args![2]!).Value;
            await _host.OnCanvasResized(1280, 720, 1);
        }
        if (identifier == "setOverlayState" && !StartupFrameAttempted)
        {
            StartupFrameAttempted = true;
            await _host!.OnAnimationFrameWithInput(0, new WebPointerFrameInput(100, 100, 0, 0, 0, 0, 0, 1));
        }
        if (identifier == "setFrameState") LastFrame = (WebRenderFrame)args![0]!;
        if (identifier == "loadPmxMeshes")
        {
            MeshUploadArgumentCount = args!.Length;
            UploadedMeshes = args.Length == 1 ? args[0] as WebPmxMeshInstance[] : null;
        }
        if(identifier=="updatePmxMeshes"){MeshUpdateArgumentCount=args!.Length;UpdatedMeshes=args.Length==1?args[0] as WebPmxMeshUpdateInstance[]:null;}
        object? result = identifier switch
        {
            "import" => this,
            "enableWebGl2" => new WebGl2Info { Renderer = "Regression renderer" },
            "detectWebGpu" => new WebGpuCapability(false, "", "", "", 0, 0, "Test"),
            _ => null
        };
        return result is TValue value ? value : default!;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class RecordingBulletModule : IJSInProcessObjectReference
{
    public string Definition { get; private set; } = "";
    public List<float[]> Frames { get; } = [];
    public List<float> Elapsed { get; } = [];
    public int Resets { get; private set; }
    public int Destroyed { get; private set; }
    public TValue Invoke<TValue>(string identifier,params object?[]? args)
    {
        object? result=null;
        if(identifier=="createWorld") { Definition=JsonSerializer.Serialize(args![0]); result=1; }
        if(identifier=="stepWorld")
        {
            Frames.Add(MemoryMarshal.Cast<byte,float>((byte[])args![1]!).ToArray());
            Elapsed.Add((float)args[2]!); if((bool)args[3]!) Resets++;
            result=Array.Empty<float>();
        }
        if(identifier=="destroyWorld") Destroyed++;
        return result is TValue value ? value : default!;
    }
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier,object?[]? args) => ValueTask.FromResult(Invoke<TValue>(identifier,args));
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier,CancellationToken cancellationToken,object?[]? args) => InvokeAsync<TValue>(identifier,args);
    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

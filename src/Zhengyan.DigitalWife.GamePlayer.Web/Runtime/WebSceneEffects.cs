using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed record WebWater(string Id, float Time, WaterSurfaceSettings Settings, IReadOnlyList<WebRipple> Ripples);
public sealed record WebSkybox(string TextureUri, float[] Tint, float Exposure);
public sealed record WebUnderwater(WaterSurfaceSettings Settings, float SurfaceDepth, float Near, float Far, bool Orthographic);
public sealed record WebCameraFrame(string Id, int[] Viewport, float[] ViewProjection, float[] SkyInverseViewProjection,
    float[] CameraPosition, IReadOnlyList<WebPlanarReflection> Reflections, WebUnderwater? Underwater, IReadOnlyList<WebRenderMesh> Meshes)
{
    public float[] View { get; init; } = [];
}
public sealed record WebRenderTextureFrame(string Id, string Name, string RefreshMode, float RefreshIntervalSeconds,
    float[] ClearColor, WebCameraFrame Camera);
public sealed record WebDebugLine(float[] Start, float[] End, float[] Color);

public sealed partial record WebRenderFrame
{
    public float Time { get; init; }
    public int[] CanvasSize { get; init; } = [];
    public WebSkybox? Sky { get; init; }
    public IReadOnlyList<WebWater> Waters { get; init; } = [];
    public IReadOnlyList<WebCameraFrame> Cameras { get; init; } = [];
    public IReadOnlyList<WebParticleBatch> ParticleBatches { get; init; } = [];
    public IReadOnlyList<WebRenderTextureFrame> RenderTextures { get; init; } = [];
    public IReadOnlyList<WebDebugLine> DebugLines { get; init; } = [];

    public WebRenderFrame WithSceneEffects(WebLoadedProject project, (RuntimeEntity Entity, WebPmxMesh Mesh)[] models, int width, int height)
    {
        RuntimeScene scene = project.RuntimeScene;
        string Texture(string? path, string fallback = "")
        {
            if (string.IsNullOrWhiteSpace(path)) return fallback;
            if (path.StartsWith("rt:", StringComparison.OrdinalIgnoreCase)) return path;
            if (project.TextureUris.TryGetValue(path, out string? uri)) return uri;
            if (path.StartsWith("app:", StringComparison.OrdinalIgnoreCase)) return "./engine/" + path[4..].Replace('\\', '/').Replace("Resources/", "");
            if (!path.Contains('/') && !path.Contains('\\')) return "./engine/Particles/" + path;
            return fallback;
        }
        WebCameraFrame Capture(RuntimeCamera camera, RuntimeViewport rect)
        {
            float aspect = rect.Width / (float)rect.Height;
            Matrix4x4 view = camera.CreateView(), projection = CameraProjection(camera, aspect);
            Matrix4x4 skyView = view; skyView.M41 = skyView.M42 = skyView.M43 = 0;
            Matrix4x4.Invert(skyView * projection, out var skyInverse);
            WebUnderwater? underwater = null; float closest = float.MaxValue;
            foreach (var water in scene.WaterSurfaces)
                if (water.Definition.Water.UnderwaterEffectEnabled
                    && WebSceneSimulation.TrySurfaceHeight(water, camera.Position, project.Simulation.Time, out float surface)
                    && camera.Position.Y < surface + .03f && surface - camera.Position.Y < closest)
                {
                    closest = Math.Max(.001f, surface - camera.Position.Y);
                    underwater = new(water.Definition.Water, closest, Math.Max(.001f, camera.Settings.NearClipPlane),
                        camera.Settings.FarClipPlane, camera.ProjectionMode == "orthographic");
                }
            return new WebCameraFrame(camera.Id, [rect.X, rect.Y, rect.Width, rect.Height], ToGlArray(view * projection),
                ToGlArray(skyInverse), ToVector(camera.Position), CreateReflections(models, camera, aspect), underwater,
                Meshes.Select(mesh => mesh with { Model = ToGlArray(CreateRenderTransform(scene.GetEntity(mesh.Id)!, camera)),
                    Materials = project.PmxAssets.TryGetValue(mesh.Id, out var asset) ? asset.Mesh.Materials.Select((material, i) =>
                    {
                        var state = asset.Pose.GetMaterialState(i);
                        return material with { Diffuse = state.Diffuse, Ambient = state.Ambient, Specular = state.Specular,
                            SpecularPower = state.SpecularPower, EdgeColor = state.EdgeColor, EdgeSize = state.EdgeSize,
                            TextureMultiply = ToVector4(state.TextureMultiply), TextureAdd = ToVector4(state.TextureAdd),
                            ToonMultiply = ToVector4(state.ToonTextureMultiply), ToonAdd = ToVector4(state.ToonTextureAdd),
                            SphereMultiply = ToVector4(state.SphereTextureMultiply), SphereAdd = ToVector4(state.SphereTextureAdd) };
                    }).ToArray() : null }).ToArray()) { View = ToGlArray(view) };
        }
        var cameras = scene.RenderCameras.Select(camera => Capture(camera,
            camera.ResolveViewport(Math.Max(width, 1), Math.Max(height, 1), project.Project.Window.Width, project.Project.Window.Height))).ToArray();
        return this with
        {
            Time = project.Simulation.Time,
            CanvasSize = [width, height],
            Sky = scene.Definition.Skybox.Enabled ? new(Texture(scene.Definition.Skybox.TexturePath, "./engine/Skybox/autumn_field_puresky.jpg"),
                ToVector(scene.Definition.Skybox.Tint), Math.Max(0, scene.Definition.Skybox.Exposure)) : null,
            Waters = scene.WaterSurfaces.Select(w => new WebWater(w.Id, project.Simulation.Time, w.Definition.Water, project.Simulation.GetRipples(w.Id))).ToArray(),
            Cameras = cameras,
            RenderTextures = scene.Definition.RenderTextures.Where(r => r.Enabled).Select(r =>
                new WebRenderTextureFrame(r.Id, r.Name, r.RefreshMode, Math.Max(.001f, r.RefreshIntervalSeconds), ToVector4(r.ClearColor),
                    Capture(scene.GetCamera(r.Camera) ?? scene.MainCamera, new(0, 0, Math.Clamp(r.Width, 1, 8192), Math.Clamp(r.Height, 1, 8192))))).ToArray(),
            ParticleBatches = project.Simulation.GetParticles(scene, path => Texture(path)),
            Particles = [],
            DebugLines = scene.Debug.Snapshot().Select(line => new WebDebugLine(ToVector(line.Start), ToVector(line.End), ToVector4(line.Color))).ToArray(),
            Features = Features with { PostProcess = cameras.Any(c => c.Underwater is not null) }
        };
    }

    private static Matrix4x4 CameraProjection(RuntimeCamera camera, float aspect)
        => camera.ProjectionMode == "orthographic" ? ToGlProjection(camera.CreateProjection(aspect)) : camera.CreateProjection(aspect);
}

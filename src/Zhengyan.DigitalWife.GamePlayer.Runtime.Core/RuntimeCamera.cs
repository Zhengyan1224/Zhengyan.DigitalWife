using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public readonly record struct RuntimeViewport(int X, int Y, int Width, int Height);

public sealed partial class RuntimeCamera
{
    internal RuntimeCamera(SceneCameraSettings definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (CameraControlPolicy.NormalizeMode(Settings.ControlMode) == "top_down") Settings.Distance = Settings.Height;
    }

    public SceneCameraSettings Definition { get; }
    internal RuntimeScenePhysics? Physics { get; set; }
    internal RuntimeScene? SceneOwner { get; set; }
    private float _screenX, _screenY, _screenWidth=1280, _screenHeight=720;
    public IReadOnlyList<string> CameraNames => SceneOwner?.Cameras.Select(c=>c.Name).ToArray() ?? [];
    public IReadOnlyList<string> RenderTextureNames => SceneOwner?.Definition.RenderTextures.Select(r=>r.Name).ToArray() ?? [];
    public string RenderTexture(string name) => name.StartsWith("rt:",StringComparison.OrdinalIgnoreCase)?name:"rt:"+name;
    public void SetMainCamera(string name){var selected=SceneOwner?.GetCamera(name);if(selected is not null)foreach(var camera in SceneOwner!.Cameras)camera.Definition.IsMain=ReferenceEquals(camera,selected);}
    public void BindRenderTextureCamera(string name,string cameraName){var rt=SceneOwner?.Definition.RenderTextures.FirstOrDefault(r=>r.Name==name || r.Id==name);if(rt is not null)rt.Camera=cameraName;}
    public bool RaycastEntity(RuntimeRay ray, out RuntimeRaycastHit hit, float maxDistance)
    {
        hit = default;
        return Physics?.Raycast(ray, out hit, maxDistance) == true;
    }

    public string Id => Definition.Id;

    public string Name => Definition.Name;

    public bool Enabled => Definition.Enabled;

    public bool IsMain => Definition.IsMain;

    public CameraSettings Settings => Definition.Camera;

    public Vector3 Position
    {
        get => Settings.Position.ToVector3();
        set => Settings.Position = Vector3Dto.FromVector3(RequireFinite(value, nameof(value)));
    }

    public Vector3 Target
    {
        get => Settings.Target.ToVector3();
        set => Settings.Target = Vector3Dto.FromVector3(RequireFinite(value, nameof(value)));
    }

    public string ProjectionMode
    {
        get => Settings.ProjectionMode;
        set => Settings.ProjectionMode = string.Equals(value, "orthographic", StringComparison.OrdinalIgnoreCase)
            ? "orthographic"
            : "perspective";
    }

    public float FieldOfView
    {
        get => Settings.Fov;
        set => Settings.Fov = Math.Clamp(value, 1.0f, 179.0f);
    }

    public string VmdPath => Settings.Vmd.Path;
    public bool VmdIsPlaying => Settings.Vmd.IsPlaying;
    public float VmdFrame => Settings.Vmd.Frame;

    public void SetVmd(string path, bool loop = true, float playbackSpeed = 1.0f, bool play = true)
    {
        Settings.Vmd.Path = path ?? string.Empty;
        Settings.Vmd.Loop = loop;
        Settings.Vmd.PlaybackSpeed = Math.Max(playbackSpeed, 0.0f);
        Settings.Vmd.Frame = 0.0f;
        Settings.Vmd.IsPlaying = play;
    }

    public void PlayVmd(bool restart = false)
    {
        if (restart) Settings.Vmd.Frame = 0.0f;
        Settings.Vmd.IsPlaying = true;
    }

    public void PauseVmd() => Settings.Vmd.IsPlaying = false;

    public void SeekVmd(float frame) => Settings.Vmd.Frame = Math.Max(frame, 0.0f);

    public RuntimeRay ScreenPointToRay(float screenX, float screenY, float viewportWidth = 0, float viewportHeight = 0)
    {
        if(viewportWidth<=0 || viewportHeight<=0){screenX-=_screenX;screenY-=_screenY;viewportWidth=_screenWidth;viewportHeight=_screenHeight;}
        float width = Math.Max(viewportWidth, 1.0f);
        float height = Math.Max(viewportHeight, 1.0f);
        float x = screenX / width * 2.0f - 1.0f;
        float y = 1.0f - screenY / height * 2.0f;
        Matrix4x4 viewProjection = CreateView() * CreateProjection(width / height);
        if (!Matrix4x4.Invert(viewProjection, out Matrix4x4 inverse))
        {
            return new RuntimeRay(Position, Vector3.Normalize(Target - Position));
        }

        Vector3 near = TransformClipPoint(new Vector3(x, y, ProjectionMode=="orthographic" ? 0 : -1), inverse);
        Vector3 far = TransformClipPoint(new Vector3(x, y, 1.0f), inverse);
        return new RuntimeRay(near, far - near).Normalized;
    }

    public Matrix4x4 CreateView()
    {
        Vector3 position = Settings.Position.ToVector3();
        Vector3 target = Settings.Target.ToVector3();
        Vector3 up = Settings.VmdHasUp ? Settings.VmdUp.ToVector3() : Vector3.UnitY;
        return CreateStableView(position, target, up);
    }

    public static Matrix4x4 CreateStableView(Vector3 position, Vector3 target, Vector3 up)
    {
        if (Vector3.DistanceSquared(position, target) < 1e-8f) target = position - Vector3.UnitZ;
        if (!IsFinite(up) || up.LengthSquared() < 1e-8f) up = Vector3.UnitY;
        Vector3 forward = Vector3.Normalize(target - position);
        if (Vector3.Cross(forward, Vector3.Normalize(up)).LengthSquared() < 1e-8f)
            up = MathF.Abs(forward.Z) < .9f ? Vector3.UnitZ : Vector3.UnitX;
        return Matrix4x4.CreateLookAt(position, target, Vector3.Normalize(up));
    }

    public Matrix4x4 CreateProjection(float aspect)
    {
        float near = Math.Max(Settings.NearClipPlane, 0.001f);
        float far = Math.Max(Settings.FarClipPlane, near + 0.001f);
        if (string.Equals(Settings.ProjectionMode, "orthographic", StringComparison.OrdinalIgnoreCase))
        {
            float height = Math.Max(Settings.OrthographicSize * 2.0f, 0.001f);
            return Matrix4x4.CreateOrthographic(height * Math.Max(aspect, 0.001f), height, near, far);
        }

        float fov = Math.Clamp(Settings.Fov, 1.0f, 179.0f) * MathF.PI / 180.0f;
        float y = 1.0f / MathF.Tan(fov * 0.5f);
        float x = y / Math.Max(aspect, 0.001f);
        return new Matrix4x4(
            x, 0.0f, 0.0f, 0.0f,
            0.0f, y, 0.0f, 0.0f,
            0.0f, 0.0f, (far + near) / (near - far), -1.0f,
            0.0f, 0.0f, (2.0f * far * near) / (near - far), 0.0f);
    }

    public RuntimeViewport ResolveViewport(int actualWidth, int actualHeight, int referenceWidth, int referenceHeight)
    {
        _screenX=_screenY=0;_screenWidth=Math.Max(1,actualWidth);_screenHeight=Math.Max(1,actualHeight);
        if (!Definition.Viewport.Enabled)
        {
            return new RuntimeViewport(0, 0, Math.Max(actualWidth, 1), Math.Max(actualHeight, 1));
        }

        float scaleX = IsRelative(Definition.Viewport.LayoutMode)
            ? Math.Max(actualWidth, 1) / (float)Math.Max(referenceWidth, 1)
            : 1.0f;
        float scaleY = IsRelative(Definition.Viewport.LayoutMode)
            ? Math.Max(actualHeight, 1) / (float)Math.Max(referenceHeight, 1)
            : 1.0f;
        int x = Math.Clamp((int)MathF.Round(Definition.Viewport.X * scaleX), 0, Math.Max(actualWidth - 1, 0));
        int yTop = Math.Clamp((int)MathF.Round(Definition.Viewport.Y * scaleY), 0, Math.Max(actualHeight - 1, 0));
        int width = Math.Clamp((int)MathF.Round(Definition.Viewport.Width * scaleX), 1, Math.Max(actualWidth - x, 1));
        int height = Math.Clamp((int)MathF.Round(Definition.Viewport.Height * scaleY), 1, Math.Max(actualHeight - yTop, 1));
        _screenX=x;_screenY=yTop;_screenWidth=width;_screenHeight=height;
        return new RuntimeViewport(x, actualHeight - yTop - height, width, height);
    }

    private string? _lastControlMode;
    internal void UpdateControl(RuntimeScene scene, float deltaSeconds, RuntimeCameraInput input)
    {
        string mode = CameraControlPolicy.NormalizeMode(Settings.ControlMode);
        if (_lastControlMode != mode)
        {
            _yawDegrees = MathF.Atan2(Forward.Z, Forward.X) * 180 / MathF.PI;
            _pitchDegrees = MathF.Asin(Math.Clamp(Forward.Y, -1, 1)) * 180 / MathF.PI;
            _lastControlMode = mode;
        }
        if (input.CursorLockSetter is not null)
        {
            if (_lockOnNextInput || mode == "fps_control") { input.LockCursor(); _lockOnNextInput=false; }
            if (_unlockOnNextInput) { input.UnlockCursor(); _unlockOnNextInput=false; }
        }
        bool editor = mode is "editor" or "rts";
        CameraGesture mouse = default;
        if (input.MouseInputAvailable)
        {
            if (input.MouseMotions.Count > 0)
            {
                foreach (var motion in input.MouseMotions)
                {
                    var value=CameraInputPolicy.Mouse(new(motion.X,motion.Y), (motion.Buttons&1)!=0, (motion.Buttons&2)!=0,
                        (motion.Buttons&4)!=0,motion.Alt,editor,motion.Locked,Settings.EnableMouseLook,
                        mode=="fps_control"?false:Settings.RequireRightMouseForMouseLook);
                    mouse=new(mouse.Look+value.Look,mouse.Pan+value.Pan,mouse.Zoom+value.Zoom);
                }
            }
            else mouse=CameraInputPolicy.Mouse(input.PointerDelta,input.PrimaryButtonDown,input.SecondaryButtonDown,
                input.MiddleButtonDown,input.IsAltDown,editor,input.IsCursorLocked,Settings.EnableMouseLook,
                mode=="fps_control"?false:Settings.RequireRightMouseForMouseLook);
        }
        var result=CameraControlPolicy.Update(new()
        {
            Mode=mode,Distance=Distance,Height=HeightOffset,ShoulderOffset=ShoulderOffset,Smoothing=Smoothing,
            MoveSpeed=MoveSpeed,MouseSensitivity=MouseSensitivity,OrbitSensitivity=_orbitSensitivity,
            PanSensitivity=_panSensitivity,ZoomSensitivity=_zoomSensitivity,SafeRadius=Settings.SafeRadius,
            AutoOrbitSpeed=Settings.AutoOrbitSpeed,FollowOffset=_cinematicOffset,Fov=Fov,
            ViewportHeight=Height,FarClip=FarClipPlane
        },Position,Target,_yawDegrees,_pitchDegrees,mouse,new(input.LookDelta,input.PanDelta,input.ZoomDelta),
            input.WheelDelta,input.IsKeyDown,deltaSeconds,scene.GetEntity(TargetEntity)?.Position,
            (scene.GetEntity(SubjectEntity)??scene.GetEntity(TargetEntity))?.Position);
        Position=result.Position;Target=result.Target;_yawDegrees=result.Yaw;_pitchDegrees=result.Pitch;Distance=result.Distance;
    }

    private static bool IsRelative(string? value)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_');
        return normalized is "relative" or "scaled" or "scale";
    }

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static Vector3 TransformClipPoint(Vector3 point, Matrix4x4 inverse)
    {
        Vector4 transformed = Vector4.Transform(new Vector4(point, 1.0f), inverse);
        return MathF.Abs(transformed.W) < 1e-6f
            ? new Vector3(transformed.X, transformed.Y, transformed.Z)
            : new Vector3(transformed.X, transformed.Y, transformed.Z) / transformed.W;
    }

    private static Vector3 RequireFinite(Vector3 value, string parameterName)
    {
        if (!IsFinite(value)) throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }
}

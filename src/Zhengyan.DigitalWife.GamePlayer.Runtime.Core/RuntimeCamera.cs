using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public readonly record struct RuntimeViewport(int X, int Y, int Width, int Height);

public sealed class RuntimeCamera
{
    internal RuntimeCamera(SceneCameraSettings definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public SceneCameraSettings Definition { get; }

    public string Id => Definition.Id;

    public string Name => Definition.Name;

    public bool Enabled => Definition.Enabled;

    public bool IsMain => Definition.IsMain;

    public CameraSettings Settings => Definition.Camera;

    public Matrix4x4 CreateView()
    {
        Vector3 position = Settings.Position.ToVector3();
        Vector3 target = Settings.Target.ToVector3();
        if (Vector3.DistanceSquared(position, target) < 1e-8f) target = position - Vector3.UnitZ;
        Vector3 up = Settings.VmdHasUp ? Settings.VmdUp.ToVector3() : Vector3.UnitY;
        if (!IsFinite(up) || up.LengthSquared() < 1e-8f) up = Vector3.UnitY;
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
        return new RuntimeViewport(x, actualHeight - yTop - height, width, height);
    }

    internal void UpdateControl(RuntimeScene scene, float deltaSeconds)
    {
        string mode = (Settings.ControlMode ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_');
        if (mode is "vmd" or "editor" or "custom" or "free" or "free_fly") return;

        RuntimeEntity? targetEntity = scene.GetEntity(Settings.TargetEntity);
        RuntimeEntity? subjectEntity = scene.GetEntity(Settings.SubjectEntity) ?? targetEntity;
        Vector3 focus = targetEntity?.Position ?? Settings.Target.ToVector3();
        focus.Y += Settings.Height;

        if (mode is "first_person" or "firstperson")
        {
            Vector3 position = (subjectEntity?.Position ?? focus) + Vector3.UnitY * Settings.Height;
            Vector3 forward = subjectEntity is null ? -Vector3.UnitZ : DirectionFromEuler(subjectEntity.RotationDegrees);
            Settings.Position = Vector3Dto.FromVector3(position);
            Settings.Target = Vector3Dto.FromVector3(position + forward);
            return;
        }

        if (mode is "auto_orbit" or "orbit")
        {
            Vector3 offset = Settings.Position.ToVector3() - focus;
            if (offset.LengthSquared() < 1e-6f) offset = new Vector3(0.0f, 0.0f, Math.Max(Settings.Distance, 0.01f));
            float radians = Settings.AutoOrbitSpeed * MathF.PI / 180.0f * Math.Max(deltaSeconds, 0.0f);
            offset = Vector3.Transform(offset, Quaternion.CreateFromAxisAngle(Vector3.UnitY, radians));
            Settings.Position = Vector3Dto.FromVector3(focus + offset);
            Settings.Target = Vector3Dto.FromVector3(focus);
            return;
        }

        if (mode is "follow" or "third_person" or "thirdperson")
        {
            Vector3 forward = subjectEntity is null ? -Vector3.UnitZ : DirectionFromEuler(subjectEntity.RotationDegrees);
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            if (!IsFinite(right) || right.LengthSquared() < 1e-8f) right = Vector3.UnitX;
            Vector3 desired = focus - forward * Math.Max(Settings.Distance, 0.01f) + right * Settings.ShoulderOffset;
            float blend = Settings.Smoothing <= 0.0f
                ? 1.0f
                : 1.0f - MathF.Exp(-Settings.Smoothing * Math.Max(deltaSeconds, 0.0f));
            Settings.Position = Vector3Dto.FromVector3(Vector3.Lerp(Settings.Position.ToVector3(), desired, blend));
            Settings.Target = Vector3Dto.FromVector3(focus);
        }
    }

    private static Vector3 DirectionFromEuler(Vector3 degrees)
    {
        Vector3 radians = degrees * (MathF.PI / 180.0f);
        return Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ,
            Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z)));
    }

    private static bool IsRelative(string? value)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_');
        return normalized is "relative" or "scaled" or "scale";
    }

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

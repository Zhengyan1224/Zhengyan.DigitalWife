using System.Numerics;

namespace Zhengyan.DigitalWife.GameProjects;

public sealed record CameraControlOptions
{
    public string Mode { get; init; } = "editor";
    public float Distance { get; init; } = 5;
    public float Height { get; init; } = 1.5f;
    public float ShoulderOffset { get; init; }
    public float Smoothing { get; init; } = 12;
    public float MoveSpeed { get; init; } = 5;
    public float MouseSensitivity { get; init; } = .15f;
    public float OrbitSensitivity { get; init; } = .2f;
    public float PanSensitivity { get; init; } = 1;
    public float ZoomSensitivity { get; init; } = 1;
    public float KeyboardPanSpeed { get; init; } = 4;
    public float SafeRadius { get; init; } = .25f;
    public float AutoOrbitSpeed { get; init; } = 30;
    public Vector3 FollowOffset { get; init; } = new(0, 1.8f, 5);
    public float Fov { get; init; } = 60;
    public float ViewportHeight { get; init; } = 720;
    public float FarClip { get; init; } = 1000;
}

public readonly record struct CameraControlResult(Vector3 Position, Vector3 Target, float Yaw, float Pitch, float Distance);

/// <summary>One camera controller for PC, Android and Web. Fixed/custom/VMD leave input to the owner.</summary>
public static class CameraControlPolicy
{
    public static string NormalizeMode(string? mode) => (mode ?? "").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_') switch
    {
        "thirdperson" or "third_person" or "third_person_follow" or "tp" => "tps",
        "firstperson" or "first_person" or "fp" => "fps",
        "fpscontrol" or "first_person_control" or "firstpersoncontrol" or "locked_fps" or "fps_locked" or "first_person_locked" => "fps_control",
        "lockon" or "hard_lock" => "lock_on",
        "3dsmax" or "3ds_max" or "3dmax" or "editor_orbit" or "max" or "orbit" or "" => "editor",
        "fly" or "flycam" or "free" => "free_fly",
        "strategy" => "rts", "topdown" or "ortho_top" => "top_down",
        "side" or "sidescroller" or "side_scroll" => "side_scroller",
        "cinematic" or "smooth_follow" or "follow" => "cinematic_follow",
        "orbital" or "auto_orbit" => "orbital_follow",
        "motion" or "vmd_motion" => "vmd", "static" => "fixed",
        "script" or "scripted" => "custom", var value => value
    };

    public static CameraControlResult Update(CameraControlOptions options, Vector3 position, Vector3 target,
        float yaw, float pitch, CameraGesture mouse, CameraGesture touch, float scroll,
        Func<string, bool> key, float dt, Vector3? targetEntity = null, Vector3? subjectEntity = null)
    {
        string mode = NormalizeMode(options.Mode);
        float distance = options.Distance;
        if (mode is "fixed" or "custom" or "vmd") return new(position, target, yaw, pitch, distance);
        dt = Math.Max(0, dt);
        if (mode == "editor")
        {
            Dolly(ref position, target, scroll * options.ZoomSensitivity);
            if (mouse.Look != Vector2.Zero)
            {
                var front = Normalize(target - position, -Vector3.UnitZ);
                float cameraYaw = MathF.Atan2(front.Z, front.X) * 180 / MathF.PI + mouse.Look.X * options.OrbitSensitivity;
                float cameraPitch = Math.Clamp(MathF.Asin(Math.Clamp(front.Y, -1, 1)) * 180 / MathF.PI
                    - mouse.Look.Y * options.OrbitSensitivity, -89, 89);
                position = target - Forward(cameraYaw, cameraPitch) * Math.Max(.1f, Vector3.Distance(position, target));
            }
            Pan(ref position, ref target, mouse.Pan * options.PanSensitivity, options);
            Dolly(ref position, target, mouse.Zoom * options.ZoomSensitivity);
            float zoom = options.KeyboardPanSpeed * dt * options.ZoomSensitivity;
            float pan = options.KeyboardPanSpeed * dt * 10 * options.PanSensitivity;
            if (key("W")) Dolly(ref position, target, zoom);
            if (key("S")) Dolly(ref position, target, -zoom);
            if (key("A")) Pan(ref position, ref target, new(pan, 0), options);
            if (key("D")) Pan(ref position, ref target, new(-pan, 0), options);
            if (key("Q")) Pan(ref position, ref target, new(0, -pan), options);
            if (key("E")) Pan(ref position, ref target, new(0, pan), options);
            if (touch != default)
                (position, target) = CameraInputPolicy.ApplyTouch(position, target, touch, options.MouseSensitivity, options.PanSensitivity, options.FarClip);
            var direction = Normalize(target - position, -Vector3.UnitZ);
            return new(position, target, MathF.Atan2(direction.Z, direction.X) * 180 / MathF.PI,
                MathF.Asin(Math.Clamp(direction.Y, -1, 1)) * 180 / MathF.PI, distance);
        }

        bool lookMode = mode is "fps" or "fps_control" or "tps" or "shoulder" or "free_fly" or "rts";
        if (lookMode)
        {
            yaw += (mouse.Look.X + touch.Look.X) * options.MouseSensitivity;
            pitch = Math.Clamp(pitch - (mouse.Look.Y + touch.Look.Y) * options.MouseSensitivity, -85, 85);
        }
        // A spreading pinch reduces distance, just as a forward wheel tick does.
        float zoomInput = scroll - touch.Zoom;
        if (mode is "tps" or "shoulder" or "rts" or "top_down" or "isometric" or "side_scroller" or "orbital_follow")
            distance = Math.Max(.1f, distance - zoomInput * options.ZoomSensitivity);
        Vector3 forward = Forward(yaw, pitch);
        Vector3 right = Normalize(Vector3.Cross(forward, Vector3.UnitY), Vector3.UnitX);
        Vector3 focus = (targetEntity ?? target) + Vector3.UnitY * options.Height;
        Vector3 desiredPosition, desiredTarget;
        switch (mode)
        {
            case "fps": case "fps_control":
                if (targetEntity is null) return new(position, target, yaw, pitch, distance);
                desiredPosition = targetEntity.Value + Vector3.UnitY * options.Height;
                desiredTarget = desiredPosition + forward;
                break;
            case "tps": case "shoulder":
                if (targetEntity is null) return new(position, target, yaw, pitch, distance);
                desiredPosition = focus - forward * distance + right * options.ShoulderOffset;
                desiredTarget = focus;
                break;
            case "free_fly":
                Vector3 motion = Vector3.Zero;
                if (key("W")) motion += forward; if (key("S")) motion -= forward;
                if (key("A")) motion -= right; if (key("D")) motion += right;
                if (key("Q")) motion -= Vector3.UnitY; if (key("E")) motion += Vector3.UnitY;
                // Android free-flight gestures translate in the camera plane and
                // move along the viewing direction when pinching.
                motion += right * touch.Pan.X - Normalize(Vector3.Cross(right, forward), Vector3.UnitY) * touch.Pan.Y
                    - forward * touch.Zoom;
                desiredPosition = position + motion * options.MoveSpeed * dt;
                desiredTarget = desiredPosition + forward;
                break;
            case "rts":
                var flat = Normalize(new(forward.X, 0, forward.Z), -Vector3.UnitZ);
                Vector3 movement = Vector3.Zero;
                if (key("W")) movement += flat; if (key("S")) movement -= flat;
                if (key("A")) movement -= right; if (key("D")) movement += right;
                desiredTarget = target + movement * options.MoveSpeed * dt;
                desiredTarget += (-right * (mouse.Pan.X + touch.Pan.X) + flat * (mouse.Pan.Y + touch.Pan.Y)) * distance * .0025f * options.PanSensitivity;
                desiredPosition = desiredTarget - forward * distance;
                break;
            case "top_down":
                desiredTarget = targetEntity ?? target;
                desiredPosition = desiredTarget + new Vector3(0, distance, .001f);
                break;
            case "isometric":
                desiredTarget = targetEntity ?? target;
                desiredPosition = desiredTarget - Vector3.Normalize(new Vector3(-1, -.65f, -1)) * distance + Vector3.UnitY * options.Height;
                break;
            case "side_scroller":
                if (targetEntity is null) return new(position, target, yaw, pitch, distance);
                desiredTarget = focus; desiredPosition = focus + Vector3.UnitZ * distance;
                break;
            case "cinematic_follow":
                if (targetEntity is null) return new(position, target, yaw, pitch, distance);
                desiredTarget = focus; desiredPosition = targetEntity.Value + options.FollowOffset;
                break;
            case "orbital_follow":
                if (targetEntity is null) return new(position, target, yaw, pitch, distance);
                yaw += options.AutoOrbitSpeed * dt;
                desiredTarget = focus; desiredPosition = focus - Forward(yaw, pitch) * distance;
                break;
            case "lock_on":
                if (subjectEntity is null || targetEntity is null) return new(position, target, yaw, pitch, distance);
                var subjectFocus = subjectEntity.Value + Vector3.UnitY * options.Height;
                var direction = focus - subjectFocus;
                direction = Normalize(new(direction.X, 0, direction.Z), -Vector3.UnitZ);
                desiredTarget = Vector3.Lerp(focus, subjectFocus, Math.Clamp(options.SafeRadius, 0, .45f));
                desiredPosition = subjectFocus - direction * distance
                    + Normalize(Vector3.Cross(direction, Vector3.UnitY), Vector3.UnitX) * options.ShoulderOffset + Vector3.UnitY * options.Height * .35f;
                break;
            default:
                return Update(options with { Mode = "editor" }, position, target, yaw, pitch, mouse, touch, scroll, key, dt, targetEntity, subjectEntity);
        }
        float blend = options.Smoothing <= 0 ? 1 : 1 - MathF.Exp(-options.Smoothing * dt);
        return new(Vector3.Lerp(position, desiredPosition, blend), Vector3.Lerp(target, desiredTarget, blend), yaw, pitch, distance);
    }

    private static Vector3 Forward(float yaw, float pitch)
    {
        yaw *= MathF.PI / 180; pitch *= MathF.PI / 180;
        return new(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Sin(yaw));
    }
    private static Vector3 Normalize(Vector3 value, Vector3 fallback) => value.LengthSquared() < 1e-8f ? fallback : Vector3.Normalize(value);
    private static void Dolly(ref Vector3 position, Vector3 target, float delta)
    {
        float distance = Math.Max(.1f, Vector3.Distance(position, target));
        position = target - Normalize(target - position, -Vector3.UnitZ) * Math.Max(.1f, distance - delta * Math.Max(distance * .15f, .05f));
    }
    private static void Pan(ref Vector3 position, ref Vector3 target, Vector2 delta, CameraControlOptions options)
    {
        Vector3 front = Normalize(target - position, -Vector3.UnitZ);
        Vector3 right = Normalize(Vector3.Cross(front, Vector3.UnitY), Vector3.UnitX);
        Vector3 up = Normalize(Vector3.Cross(right, front), Vector3.UnitY);
        float scale = 2 * MathF.Tan(options.Fov * MathF.PI / 360) * Math.Max(.1f, Vector3.Distance(position, target)) / Math.Max(1, options.ViewportHeight);
        var offset = (-right * delta.X + up * delta.Y) * scale;
        position += offset; target += offset;
    }
}

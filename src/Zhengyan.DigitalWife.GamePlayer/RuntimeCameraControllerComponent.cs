using System.Numerics;
using Silk.NET.Input;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GamePlayer;

internal sealed class RuntimeCameraControllerComponent(
    OrbitCamera camera,
    Func<string, RuntimeEntity?> getEntity) : GameComponent
{
    private readonly OrbitCamera _camera = camera;
    private readonly Func<string, RuntimeEntity?> _getEntity = getEntity;
    private Vector3 _fixedPosition;
    private Vector3 _fixedTarget;
    private bool _dragFirstMove = true;
    private Vector2 _lastMousePosition;
    private float _yawDegrees = -90.0f;
    private float _pitchDegrees = 15.0f;
    private Vector3 _followOffset = new(0.0f, 1.8f, 5.0f);
    private float _autoOrbitSpeedDegrees = 30.0f;

    public string Mode { get; private set; } = "editor";

    public string TargetEntity { get; private set; } = string.Empty;

    public string SubjectEntity { get; private set; } = string.Empty;

    public float Distance { get; set; } = 5.0f;

    public float Height { get; set; } = 1.5f;

    public float ShoulderOffset { get; set; }

    public float Smoothing { get; set; } = 12.0f;

    public float MoveSpeed { get; set; } = 5.0f;

    public float MouseSensitivity { get; set; } = 0.15f;

    public float OrbitSensitivity { get; set; } = 0.2f;

    public float PanSensitivity { get; set; } = 1.0f;

    public float ZoomSensitivity { get; set; } = 1.0f;

    public float KeyboardPanSpeed { get; set; } = 4.0f;

    public float SafeRadius { get; set; } = 0.25f;

    public bool EnableMouseLook { get; set; } = true;

    public bool RequireRightMouseForMouseLook { get; set; } = true;

    public Func<bool>? CanProcessMouseDrag { get; set; }

    public float YawDegrees => _yawDegrees;

    public float PitchDegrees => _pitchDegrees;

    public Vector3 FollowOffset
    {
        get => _followOffset;
        set => _followOffset = value;
    }

    public float AutoOrbitSpeedDegrees
    {
        get => _autoOrbitSpeedDegrees;
        set => _autoOrbitSpeedDegrees = value;
    }

    public void SetMode(string mode)
    {
        string normalized = NormalizeMode(mode);
        Mode = normalized;
        SyncAnglesFromCamera();
        if (normalized == "fixed")
        {
            _fixedPosition = _camera.Position;
            _fixedTarget = _camera.Target;
        }
    }

    public void SetTarget(string entityIdOrName)
    {
        TargetEntity = entityIdOrName ?? string.Empty;
    }

    public void SetSubject(string entityIdOrName)
    {
        SubjectEntity = entityIdOrName ?? string.Empty;
    }

    public void SetMouseLook(bool enabled, bool requireRightMouse = true)
    {
        EnableMouseLook = enabled;
        RequireRightMouseForMouseLook = requireRightMouse;
    }

    public void SetAngles(float yawDegrees, float pitchDegrees)
    {
        _yawDegrees = yawDegrees;
        _pitchDegrees = Math.Clamp(pitchDegrees, -85.0f, 85.0f);
    }

    public void Configure(
        float? distance = null,
        float? height = null,
        float? shoulderOffset = null,
        float? smoothing = null,
        float? moveSpeed = null,
        float? mouseSensitivity = null,
        float? safeRadius = null,
        float? autoOrbitSpeed = null)
    {
        if (distance.HasValue)
        {
            Distance = Math.Max(0.01f, distance.Value);
        }

        if (height.HasValue)
        {
            Height = height.Value;
        }

        if (shoulderOffset.HasValue)
        {
            ShoulderOffset = shoulderOffset.Value;
        }

        if (smoothing.HasValue)
        {
            Smoothing = Math.Max(0.0f, smoothing.Value);
        }

        if (moveSpeed.HasValue)
        {
            MoveSpeed = Math.Max(0.0f, moveSpeed.Value);
        }

        if (mouseSensitivity.HasValue)
        {
            MouseSensitivity = Math.Max(0.0f, mouseSensitivity.Value);
        }

        if (safeRadius.HasValue)
        {
            SafeRadius = Math.Clamp(safeRadius.Value, 0.0f, 0.45f);
        }

        if (autoOrbitSpeed.HasValue)
        {
            AutoOrbitSpeedDegrees = autoOrbitSpeed.Value;
        }
    }

    public void ThirdPerson(string target, float distance, float height, float shoulderOffset, float smoothing)
    {
        SetMode("tps");
        SetTarget(target);
        Configure(distance, height, shoulderOffset, smoothing);
    }

    public void Shoulder(string target, float distance, float height, float shoulderOffset, float smoothing)
    {
        SetMode("shoulder");
        SetTarget(target);
        Configure(distance, height, shoulderOffset, smoothing);
    }

    public void LockOn(string subject, string target, float distance, float height, float smoothing, float safeRadius)
    {
        SetMode("lock_on");
        SetSubject(subject);
        SetTarget(target);
        Configure(distance: distance, height: height, shoulderOffset: 0.0f, smoothing: smoothing, safeRadius: safeRadius);
    }

    public void FirstPerson(string target, float eyeHeight, float smoothing)
    {
        SetMode("fps");
        SetTarget(target);
        Configure(distance: 0.01f, height: eyeHeight, shoulderOffset: 0.0f, smoothing: smoothing);
    }

    public void FreeFly(float moveSpeed, float mouseSensitivity)
    {
        SetMode("free_fly");
        Configure(moveSpeed: moveSpeed, mouseSensitivity: mouseSensitivity);
    }

    public void EditorOrbit(float orbitSensitivity, float panSensitivity, float zoomSensitivity)
    {
        SetMode("editor");
        OrbitSensitivity = Math.Max(0.0f, orbitSensitivity);
        PanSensitivity = Math.Max(0.0f, panSensitivity);
        ZoomSensitivity = Math.Max(0.0f, zoomSensitivity);
    }

    public void TopDown(string target, float height, float smoothing)
    {
        SetMode("top_down");
        SetTarget(target);
        Configure(distance: Math.Max(0.01f, height), height: height, shoulderOffset: 0.0f, smoothing: smoothing);
    }

    public void Rts(float height, float pitch, float moveSpeed)
    {
        SetMode("rts");
        _pitchDegrees = -Math.Clamp(Math.Abs(pitch), 10.0f, 89.0f);
        Configure(distance: height, height: height, moveSpeed: moveSpeed);
    }

    public void Isometric(string target, float distance, float height, float smoothing)
    {
        SetMode("isometric");
        SetTarget(target);
        Configure(distance, height, shoulderOffset: 0.0f, smoothing);
    }

    public void SideScroller(string target, float distance, float height, float smoothing)
    {
        SetMode("side_scroller");
        SetTarget(target);
        Configure(distance, height, shoulderOffset: 0.0f, smoothing);
    }

    public void CinematicFollow(string target, Vector3 offset, float lookHeight, float smoothing)
    {
        SetMode("cinematic_follow");
        SetTarget(target);
        FollowOffset = offset;
        Configure(height: lookHeight, smoothing: smoothing);
    }

    public void OrbitalFollow(string target, float distance, float height, float yawSpeed, float smoothing)
    {
        SetMode("orbital_follow");
        SetTarget(target);
        Configure(distance: distance, height: height, shoulderOffset: 0.0f, smoothing: smoothing, autoOrbitSpeed: yawSpeed);
    }

    public void Fixed(Vector3 position, Vector3 target)
    {
        SetMode("fixed");
        _fixedPosition = position;
        _fixedTarget = target;
        _camera.SetLookAt(_fixedPosition, _fixedTarget);
    }

    public void Custom()
    {
        SetMode("custom");
    }

    public override void Update(GameTime gameTime)
    {
        if (Game is null)
        {
            return;
        }

        _camera.Width = Math.Max(Game.GraphicsDevice.BackBufferSize.X, 1);
        _camera.Height = Math.Max(Game.GraphicsDevice.BackBufferSize.Y, 1);

        float dt = Math.Max(0.0f, (float)gameTime.ElapsedSeconds);
        switch (Mode)
        {
            case "custom":
                _dragFirstMove = true;
                return;
            case "fixed":
                ApplyLookAt(_fixedPosition, _fixedTarget, dt);
                return;
            case "fps":
            case "first_person":
                UpdateFirstPerson(dt);
                return;
            case "tps":
            case "third_person":
            case "shoulder":
                UpdateThirdPerson(dt);
                return;
            case "lock_on":
            case "lockon":
                UpdateLockOn(dt);
                return;
            case "free_fly":
            case "fly":
                UpdateFreeFly(dt);
                return;
            case "top_down":
                UpdateTopDown(dt);
                return;
            case "rts":
                UpdateRts(dt);
                return;
            case "isometric":
                UpdateIsometric(dt);
                return;
            case "side_scroller":
            case "side":
                UpdateSideScroller(dt);
                return;
            case "follow":
            case "cinematic_follow":
                UpdateCinematicFollow(dt);
                return;
            case "orbital_follow":
            case "orbital":
                UpdateOrbitalFollow(dt);
                return;
            case "editor":
            case "orbit":
            case "max":
            default:
                UpdateEditorOrbit(dt);
                return;
        }
    }

    private void UpdateThirdPerson(float dt)
    {
        RuntimeEntity? target = ResolveTarget();
        if (target is null)
        {
            return;
        }

        UpdateMouseLook(allowPitch: true);
        ApplyZoom();
        Vector3 focus = target.Position + new Vector3(0.0f, Height, 0.0f);
        Vector3 forward = CreateForward(_yawDegrees, _pitchDegrees);
        Vector3 right = SafeNormalize(Vector3.Cross(forward, Vector3.UnitY), Vector3.UnitX);
        Vector3 desiredPosition = focus - (forward * Math.Max(Distance, 0.01f)) + (right * ShoulderOffset);
        ApplyLookAt(desiredPosition, focus, dt);
    }

    private void UpdateLockOn(float dt)
    {
        RuntimeEntity? subject = ResolveSubject() ?? ResolveTarget();
        RuntimeEntity? target = ResolveTarget();
        if (subject is null || target is null)
        {
            return;
        }

        Vector3 subjectFocus = subject.Position + new Vector3(0.0f, Height, 0.0f);
        Vector3 targetFocus = target.Position + new Vector3(0.0f, Height, 0.0f);
        Vector3 direction = targetFocus - subjectFocus;
        if (direction.LengthSquared() < 0.0001f)
        {
            direction = _camera.Front;
        }

        direction = SafeNormalize(new Vector3(direction.X, 0.0f, direction.Z), -Vector3.UnitZ);
        if (direction.LengthSquared() < 0.0001f)
        {
            direction = -Vector3.UnitZ;
        }

        Vector3 right = SafeNormalize(Vector3.Cross(direction, Vector3.UnitY), Vector3.UnitX);
        Vector3 desiredTarget = Vector3.Lerp(targetFocus, subjectFocus, Math.Clamp(SafeRadius, 0.0f, 0.45f));
        Vector3 desiredPosition = subjectFocus - (direction * Math.Max(Distance, 0.01f))
            + (right * ShoulderOffset)
            + new Vector3(0.0f, Height * 0.35f, 0.0f);
        ApplyLookAt(desiredPosition, desiredTarget, dt);
    }

    private void UpdateFirstPerson(float dt)
    {
        RuntimeEntity? target = ResolveTarget();
        if (target is null)
        {
            return;
        }

        UpdateMouseLook(allowPitch: true);
        Vector3 position = target.Position + new Vector3(0.0f, Height, 0.0f);
        Vector3 forward = CreateForward(_yawDegrees, _pitchDegrees);
        ApplyLookAt(position, position + forward, dt);
    }

    private void UpdateFreeFly(float dt)
    {
        UpdateMouseLook(allowPitch: true);
        Vector3 forward = CreateForward(_yawDegrees, _pitchDegrees);
        Vector3 right = SafeNormalize(Vector3.Cross(forward, Vector3.UnitY), Vector3.UnitX);
        Vector3 up = Vector3.UnitY;
        Vector3 position = _camera.Position;
        float step = MoveSpeed * dt;

        if (Game!.Input.IsKeyDown(Key.W)) position += forward * step;
        if (Game.Input.IsKeyDown(Key.S)) position -= forward * step;
        if (Game.Input.IsKeyDown(Key.A)) position -= right * step;
        if (Game.Input.IsKeyDown(Key.D)) position += right * step;
        if (Game.Input.IsKeyDown(Key.E)) position += up * step;
        if (Game.Input.IsKeyDown(Key.Q)) position -= up * step;

        ApplyLookAt(position, position + forward, dt);
    }

    private void UpdateTopDown(float dt)
    {
        RuntimeEntity? target = ResolveTarget();
        Vector3 focus = target?.Position ?? _camera.Target;
        Vector3 desiredPosition = focus + new Vector3(0.0f, Math.Max(Distance, 0.01f), 0.001f);
        ApplyLookAt(desiredPosition, focus, dt);
    }

    private void UpdateRts(float dt)
    {
        Vector3 forward = CreateForward(_yawDegrees, _pitchDegrees);
        Vector3 right = SafeNormalize(Vector3.Cross(forward, Vector3.UnitY), Vector3.UnitX);
        Vector3 flatForward = SafeNormalize(new Vector3(forward.X, 0.0f, forward.Z), -Vector3.UnitZ);
        Vector3 target = _camera.Target;
        float step = MoveSpeed * dt;

        if (Game!.Input.IsKeyDown(Key.W)) target += flatForward * step;
        if (Game.Input.IsKeyDown(Key.S)) target -= flatForward * step;
        if (Game.Input.IsKeyDown(Key.A)) target -= right * step;
        if (Game.Input.IsKeyDown(Key.D)) target += right * step;
        if (Game.Input.ScrollDelta.Y != 0.0f)
        {
            Distance = Math.Max(1.0f, Distance - (Game.Input.ScrollDelta.Y * ZoomSensitivity));
        }

        Vector3 desiredPosition = target - (forward * Math.Max(Distance, 0.01f));
        ApplyLookAt(desiredPosition, target, dt);
    }

    private void UpdateIsometric(float dt)
    {
        RuntimeEntity? target = ResolveTarget();
        Vector3 focus = target?.Position ?? _camera.Target;
        Vector3 direction = Vector3.Normalize(new Vector3(-1.0f, -0.65f, -1.0f));
        Vector3 desiredPosition = focus - (direction * Math.Max(Distance, 0.01f)) + new Vector3(0.0f, Height, 0.0f);
        ApplyLookAt(desiredPosition, focus, dt);
    }

    private void UpdateSideScroller(float dt)
    {
        RuntimeEntity? target = ResolveTarget();
        if (target is null)
        {
            return;
        }

        Vector3 focus = target.Position + new Vector3(0.0f, Height, 0.0f);
        Vector3 desiredPosition = focus + new Vector3(0.0f, 0.0f, Math.Max(Distance, 0.01f));
        ApplyLookAt(desiredPosition, focus, dt);
    }

    private void UpdateCinematicFollow(float dt)
    {
        RuntimeEntity? target = ResolveTarget();
        if (target is null)
        {
            return;
        }

        Vector3 focus = target.Position + new Vector3(0.0f, Height, 0.0f);
        Vector3 desiredPosition = target.Position + FollowOffset;
        ApplyLookAt(desiredPosition, focus, dt);
    }

    private void UpdateOrbitalFollow(float dt)
    {
        RuntimeEntity? target = ResolveTarget();
        if (target is null)
        {
            return;
        }

        _yawDegrees += AutoOrbitSpeedDegrees * dt;
        Vector3 focus = target.Position + new Vector3(0.0f, Height, 0.0f);
        Vector3 forward = CreateForward(_yawDegrees, _pitchDegrees);
        Vector3 desiredPosition = focus - (forward * Math.Max(Distance, 0.01f));
        ApplyLookAt(desiredPosition, focus, dt);
    }

    private void UpdateEditorOrbit(float dt)
    {
        if (Game is null)
        {
            return;
        }

        if (Game.Input.ScrollDelta.Y != 0.0f)
        {
            _camera.Dolly(Game.Input.ScrollDelta.Y * ZoomSensitivity);
        }

        bool canProcessMouseDrag = CanProcessMouseDrag?.Invoke() != false;
        CameraDragMode dragMode = canProcessMouseDrag ? ResolveEditorDragMode() : CameraDragMode.None;
        if (dragMode != CameraDragMode.None && !IsEditorDragButtonStillDown(dragMode))
        {
            dragMode = CameraDragMode.None;
        }

        if (dragMode == CameraDragMode.None)
        {
            _dragFirstMove = true;
        }
        else
        {
            Vector2 current = Game.Input.MousePosition;
            if (_dragFirstMove)
            {
                _lastMousePosition = current;
                _dragFirstMove = false;
            }
            else
            {
                float deltaX = current.X - _lastMousePosition.X;
                float deltaY = current.Y - _lastMousePosition.Y;
                _lastMousePosition = current;

                switch (dragMode)
                {
                    case CameraDragMode.Orbit:
                        _camera.Orbit(deltaX * OrbitSensitivity, -deltaY * OrbitSensitivity);
                        SyncAnglesFromCamera();
                        break;
                    case CameraDragMode.Pan:
                        _camera.Pan(deltaX * PanSensitivity, deltaY * PanSensitivity);
                        break;
                    case CameraDragMode.Dolly:
                        _camera.Dolly((-deltaY * 0.05f) * ZoomSensitivity);
                        break;
                }
            }
        }

        float keyboardPan = KeyboardPanSpeed * dt * 10.0f * PanSensitivity;
        float keyboardZoom = KeyboardPanSpeed * dt * ZoomSensitivity;
        if (Game.Input.IsKeyDown(Key.W))
        {
            _camera.Dolly(keyboardZoom);
        }

        if (Game.Input.IsKeyDown(Key.S))
        {
            _camera.Dolly(-keyboardZoom);
        }

        if (Game.Input.IsKeyDown(Key.A))
        {
            _camera.Pan(keyboardPan, 0.0f);
        }

        if (Game.Input.IsKeyDown(Key.D))
        {
            _camera.Pan(-keyboardPan, 0.0f);
        }

        if (Game.Input.IsKeyDown(Key.Q))
        {
            _camera.Pan(0.0f, -keyboardPan);
        }

        if (Game.Input.IsKeyDown(Key.E))
        {
            _camera.Pan(0.0f, keyboardPan);
        }
    }

    private RuntimeEntity? ResolveTarget()
    {
        return string.IsNullOrWhiteSpace(TargetEntity) ? null : _getEntity(TargetEntity);
    }

    private RuntimeEntity? ResolveSubject()
    {
        return string.IsNullOrWhiteSpace(SubjectEntity) ? null : _getEntity(SubjectEntity);
    }

    private void ApplyLookAt(Vector3 desiredPosition, Vector3 desiredTarget, float dt)
    {
        float factor = Smoothing <= 0.0f ? 1.0f : 1.0f - MathF.Exp(-Smoothing * dt);
        Vector3 position = Vector3.Lerp(_camera.Position, desiredPosition, factor);
        Vector3 target = Vector3.Lerp(_camera.Target, desiredTarget, factor);
        _camera.SetLookAt(position, target);
    }

    private void UpdateMouseLook(bool allowPitch)
    {
        if (Game is null || !EnableMouseLook || CanProcessMouseDrag?.Invoke() == false)
        {
            _dragFirstMove = true;
            return;
        }

        if (RequireRightMouseForMouseLook && !IsMouseButtonEffectivelyDown(MouseButton.Right))
        {
            _dragFirstMove = true;
            return;
        }

        Vector2 current = Game.Input.MousePosition;
        if (_dragFirstMove)
        {
            _lastMousePosition = current;
            _dragFirstMove = false;
            return;
        }

        Vector2 delta = current - _lastMousePosition;
        _lastMousePosition = current;
        _yawDegrees += delta.X * MouseSensitivity;
        if (allowPitch)
        {
            _pitchDegrees = Math.Clamp(_pitchDegrees - (delta.Y * MouseSensitivity), -85.0f, 85.0f);
        }
    }

    private void ApplyZoom()
    {
        if (Game is not null && Game.Input.ScrollDelta.Y != 0.0f)
        {
            Distance = Math.Max(0.1f, Distance - (Game.Input.ScrollDelta.Y * ZoomSensitivity));
        }
    }

    private void SyncAnglesFromCamera()
    {
        _yawDegrees = _camera.Yaw;
        _pitchDegrees = _camera.Pitch;
    }

    private CameraDragMode ResolveEditorDragMode()
    {
        if (Game is null)
        {
            return CameraDragMode.None;
        }

        bool altPressed = Game.Input.IsAltDown;
        if (altPressed && IsMouseButtonEffectivelyDown(MouseButton.Right))
        {
            return CameraDragMode.Dolly;
        }

        if (IsMouseButtonEffectivelyDown(MouseButton.Right))
        {
            return CameraDragMode.Orbit;
        }

        if (altPressed && (IsMouseButtonEffectivelyDown(MouseButton.Middle) || IsMouseButtonEffectivelyDown(MouseButton.Left)))
        {
            return CameraDragMode.Orbit;
        }

        return IsMouseButtonEffectivelyDown(MouseButton.Middle) ? CameraDragMode.Pan : CameraDragMode.None;
    }

    private bool IsEditorDragButtonStillDown(CameraDragMode dragMode)
    {
        if (Game is null)
        {
            return false;
        }

        bool altPressed = Game.Input.IsAltDown;
        return dragMode switch
        {
            CameraDragMode.Dolly => IsMouseButtonEffectivelyDown(MouseButton.Right),
            CameraDragMode.Pan => IsMouseButtonEffectivelyDown(MouseButton.Middle),
            CameraDragMode.Orbit => IsMouseButtonEffectivelyDown(MouseButton.Right)
                || (altPressed && (IsMouseButtonEffectivelyDown(MouseButton.Middle) || IsMouseButtonEffectivelyDown(MouseButton.Left))),
            _ => false
        };
    }

    private bool IsMouseButtonEffectivelyDown(MouseButton button)
    {
        if (Game is null || !Game.Input.IsMouseButtonDown(button))
        {
            return false;
        }

        return !DesktopSpritePlatform.TryGetGlobalMouseButtonState(Game.Window, button, out bool globalDown) || globalDown;
    }

    private static Vector3 CreateForward(float yawDegrees, float pitchDegrees)
    {
        float yaw = MathF.PI / 180.0f * yawDegrees;
        float pitch = MathF.PI / 180.0f * pitchDegrees;
        Vector3 forward = new(
            MathF.Cos(pitch) * MathF.Cos(yaw),
            MathF.Sin(pitch),
            MathF.Cos(pitch) * MathF.Sin(yaw));
        return Vector3.Normalize(forward);
    }

    private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
    {
        return value.LengthSquared() < 0.000001f
            ? fallback
            : Vector3.Normalize(value);
    }

    private static string NormalizeMode(string mode)
    {
        string normalized = (mode ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized switch
        {
            "thirdperson" or "third_person" or "third_person_follow" or "tp" => "tps",
            "firstperson" or "first_person" or "fp" => "fps",
            "lockon" or "hard_lock" => "lock_on",
            "3dsmax" or "3ds_max" or "3dmax" or "editor_orbit" or "max" or "orbit" => "editor",
            "fly" or "flycam" or "free" => "free_fly",
            "strategy" => "rts",
            "topdown" => "top_down",
            "side" or "side_scroller" or "sidescroller" or "side_scroll" => "side_scroller",
            "ortho_top" => "top_down",
            "cinematic" or "cinematic_follow" or "smooth_follow" => "cinematic_follow",
            "orbital" or "orbital_follow" or "auto_orbit" => "orbital_follow",
            "static" => "fixed",
            "script" or "scripted" => "custom",
            "" => "editor",
            _ => normalized
        };
    }

    private enum CameraDragMode
    {
        None,
        Orbit,
        Pan,
        Dolly
    }
}

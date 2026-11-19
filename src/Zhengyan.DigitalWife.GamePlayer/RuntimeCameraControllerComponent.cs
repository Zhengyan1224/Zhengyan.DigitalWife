using System.Numerics;
using Silk.NET.Input;
using Zhengyan.DigitalWife.GameProjects;
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
    private bool _cursorLockedByController;

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

    public bool LockCursorInFpsControl { get; set; } = true;

    public bool UseRawMouseInputForFpsControl { get; set; }

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
        string previous = Mode;
        Mode = normalized;
        SyncAnglesFromCamera();
        if (!string.Equals(previous, normalized, StringComparison.Ordinal))
        {
            _dragFirstMove = true;
            ApplyCursorLockForCurrentMode();
        }

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

    public void ApplySettings(CameraSettings settings)
    {
        Configure(settings.Distance, settings.Height, settings.ShoulderOffset, settings.Smoothing,
            settings.MoveSpeed, settings.MouseSensitivity, settings.SafeRadius, settings.AutoOrbitSpeed);
        SetMouseLook(settings.EnableMouseLook, settings.RequireRightMouseForMouseLook);
        string mode = NormalizeMode(settings.ControlMode);
        switch (mode)
        {
            case "fps_control":
                FpsControl(settings.TargetEntity, settings.Height, settings.Smoothing, settings.MouseSensitivity);
                break;
            case "fps":
                FirstPerson(settings.TargetEntity, settings.Height, settings.Smoothing);
                break;
            case "tps":
            case "third_person":
                ThirdPerson(settings.TargetEntity, settings.Distance, settings.Height, settings.ShoulderOffset, settings.Smoothing);
                break;
            case "shoulder":
                Shoulder(settings.TargetEntity, settings.Distance, settings.Height, settings.ShoulderOffset, settings.Smoothing);
                break;
            case "lock_on":
            case "lockon":
                LockOn(settings.SubjectEntity, settings.TargetEntity, settings.Distance, settings.Height, settings.Smoothing, settings.SafeRadius);
                break;
            case "free_fly":
            case "fly":
                FreeFly(settings.MoveSpeed, settings.MouseSensitivity);
                SetMouseLook(settings.EnableMouseLook, settings.RequireRightMouseForMouseLook);
                break;
            case "top_down":
                TopDown(settings.TargetEntity, settings.Height, settings.Smoothing);
                break;
            case "rts":
                SetMode("rts");
                break;
            case "isometric":
                Isometric(settings.TargetEntity, settings.Distance, settings.Height, settings.Smoothing);
                break;
            case "side_scroller":
                SideScroller(settings.TargetEntity, settings.Distance, settings.Height, settings.Smoothing);
                break;
            case "cinematic_follow":
                CinematicFollow(settings.TargetEntity, FollowOffset, settings.Height, settings.Smoothing);
                break;
            case "orbital_follow":
                OrbitalFollow(settings.TargetEntity, settings.Distance, settings.Height, settings.AutoOrbitSpeed, settings.Smoothing);
                break;
            case "fixed":
                SetMode("fixed");
                break;
            case "custom":
                Custom();
                break;
            case "vmd":
                SetMode("vmd");
                break;
            case "editor":
            default:
                EditorOrbit(OrbitSensitivity, PanSensitivity, ZoomSensitivity);
                break;
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

    public void FpsControl(string target, float eyeHeight, float smoothing, float mouseSensitivity)
    {
        SetMode("fps_control");
        SetTarget(target);
        Configure(distance: 0.01f, height: eyeHeight, shoulderOffset: 0.0f, smoothing: smoothing, mouseSensitivity: mouseSensitivity);
        SetMouseLook(enabled: true, requireRightMouse: false);
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

    private readonly HashSet<int> _guiTouches = [];
    private readonly CameraTouchGestureTracker _touchGestures = new();
    public Func<Vector2, bool>? IsTouchOverGui { get; set; }

    public override void Update(GameTime gameTime)
    {
        if (Game is null) return;
        _camera.Width = Math.Max(Game.GraphicsDevice.BackBufferSize.X, 1);
        _camera.Height = Math.Max(Game.GraphicsDevice.BackBufferSize.Y, 1);
        ApplyCursorLockForCurrentMode();
        if (Mode is "custom" or "vmd") { _dragFirstMove = true; return; }
        if (Mode == "fixed") { ApplyLookAt(_fixedPosition, _fixedTarget, (float)gameTime.ElapsedSeconds); return; }

        foreach (var contact in Game.Input.TouchTransitions)
            if (contact.Phase == Zhengyan.DigitalWife.Mmd.Game.Input.TouchPhase.Started && IsTouchOverGui?.Invoke(new(contact.X, contact.Y)) == true)
                _guiTouches.Add(contact.Id);
        var contacts = Game.Input.Touches.Where(t => t.IsActive && !_guiTouches.Contains(t.Id))
            .Select(t => new CameraTouchContact(t.Id, new(t.X, t.Y), new(t.DeltaX, t.DeltaY),
                t.Phase == Zhengyan.DigitalWife.Mmd.Game.Input.TouchPhase.Started)).ToArray();
        _guiTouches.RemoveWhere(id => !Game.Input.Touches.Any(t => t.Id == id && t.IsActive));
        var touch = _touchGestures.Update(contacts);
        bool editor = Mode is "editor" or "rts";
        var current = GetEffectiveMousePosition();
        var delta = Game.Input.IsCursorLocked ? Game.Input.MouseDelta : current - _lastMousePosition;
        bool left = IsMouseButtonEffectivelyDown(MouseButton.Left);
        bool right = IsMouseButtonEffectivelyDown(MouseButton.Right);
        bool middle = IsMouseButtonEffectivelyDown(MouseButton.Middle);
        var mouse = CameraInputPolicy.Mouse(delta, left, right, middle,
            Game.Input.IsAltDown, editor, Game.Input.IsCursorLocked, EnableMouseLook,
            Mode == "fps_control" ? false : RequireRightMouseForMouseLook);
        // The first sampled press establishes an anchor, including a press after a window drag.
        bool dragging = CameraInputPolicy.Mouse(Vector2.One, left, right, middle, Game.Input.IsAltDown,
            editor, Game.Input.IsCursorLocked, EnableMouseLook,
            Mode == "fps_control" ? false : RequireRightMouseForMouseLook) != default;
        if (!dragging || CanProcessMouseDrag?.Invoke() == false) { mouse = default; _dragFirstMove = true; }
        else if (_dragFirstMove) { mouse = default; _dragFirstMove = false; }
        _lastMousePosition = current;
        if (CanProcessMouseDrag?.Invoke() == false) touch = default;
        var result = CameraControlPolicy.Update(new()
        {
            Mode=Mode, Distance=Distance, Height=Height, ShoulderOffset=ShoulderOffset, Smoothing=Smoothing,
            MoveSpeed=MoveSpeed, MouseSensitivity=MouseSensitivity, OrbitSensitivity=OrbitSensitivity,
            PanSensitivity=PanSensitivity, ZoomSensitivity=ZoomSensitivity, KeyboardPanSpeed=KeyboardPanSpeed,
            SafeRadius=SafeRadius, AutoOrbitSpeed=AutoOrbitSpeedDegrees, FollowOffset=FollowOffset,
            Fov=_camera.Fov, ViewportHeight=_camera.Height, FarClip=_camera.FarClipPlane
        }, _camera.Position, _camera.Target, _yawDegrees, _pitchDegrees, mouse, touch, GetEffectiveScrollDeltaY(),
            key => Enum.TryParse<Key>(key, out var parsed) && Game.Input.IsKeyDown(parsed), (float)gameTime.ElapsedSeconds,
            ResolveTarget()?.Position, (ResolveSubject() ?? ResolveTarget())?.Position);
        _camera.SetLookAt(result.Position, result.Target);
        _yawDegrees=result.Yaw; _pitchDegrees=result.Pitch; Distance=result.Distance;
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

    private void SyncAnglesFromCamera()
    {
        _yawDegrees = _camera.Yaw;
        _pitchDegrees = _camera.Pitch;
    }

    private bool IsMouseButtonEffectivelyDown(MouseButton button)
    {
        if (Game is null)
        {
            return false;
        }

        return DesktopSpritePlatform.TryGetGlobalMouseButtonState(Game.Window, button, out bool globalDown)
            ? globalDown
            : Game.Input.IsMouseButtonDown(button);
    }

    private Vector2 GetEffectiveMousePosition()
    {
        if (Game is null)
        {
            return Vector2.Zero;
        }

        if (DesktopSpritePlatform.TryGetGlobalCursorPosition(Game.Window, out Vector2 globalPosition))
        {
            return globalPosition;
        }

        return Game.Input.MousePosition;
    }

    private float GetEffectiveScrollDeltaY()
    {
        if (Game is null)
        {
            return 0.0f;
        }

        float scrollY = Game.Input.ScrollDelta.Y;
        float globalScrollY = DesktopSpritePlatform.ConsumeGlobalScrollDeltaY(Game.Window);
        return scrollY != 0.0f ? scrollY : globalScrollY;
    }

    private void ApplyCursorLockForCurrentMode()
    {
        if (Game is null)
        {
            return;
        }

        bool shouldLock = Mode == "fps_control" && LockCursorInFpsControl && ResolveTarget() is not null;
        if (shouldLock)
        {
            bool locked = Game.Input.TrySetCursorLocked(true, UseRawMouseInputForFpsControl);
            _cursorLockedByController = locked || _cursorLockedByController;
            return;
        }

        if (_cursorLockedByController)
        {
            _ = Game.Input.TrySetCursorLocked(false);
            _cursorLockedByController = false;
        }
    }

    private static string NormalizeMode(string mode) => CameraControlPolicy.NormalizeMode(mode);

    public override void Dispose()
    {
        if (_cursorLockedByController && Game is not null)
        {
            _ = Game.Input.TrySetCursorLocked(false);
            _cursorLockedByController = false;
        }

        base.Dispose();
    }

}

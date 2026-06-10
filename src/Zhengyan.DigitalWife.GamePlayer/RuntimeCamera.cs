using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GamePlayer;

public readonly record struct RuntimeRay(Vector3 Origin, Vector3 Direction)
{
    public Vector3 GetPoint(float distance) => Origin + (Direction * distance);

    public bool TryIntersectPlaneY(float y, out Vector3 point)
    {
        point = default;
        if (MathF.Abs(Direction.Y) < 0.00001f)
        {
            return false;
        }

        float distance = (y - Origin.Y) / Direction.Y;
        if (distance < 0.0f)
        {
            return false;
        }

        point = GetPoint(distance);
        return true;
    }

    public bool TryIntersectSphere(Vector3 center, float radius, out float distance)
    {
        distance = 0.0f;
        Vector3 offset = Origin - center;
        float a = Vector3.Dot(Direction, Direction);
        float b = 2.0f * Vector3.Dot(offset, Direction);
        float c = Vector3.Dot(offset, offset) - (radius * radius);
        float discriminant = (b * b) - (4.0f * a * c);
        if (discriminant < 0.0f)
        {
            return false;
        }

        float sqrt = MathF.Sqrt(discriminant);
        float near = (-b - sqrt) / (2.0f * a);
        float far = (-b + sqrt) / (2.0f * a);
        distance = near >= 0.0f ? near : far;
        return distance >= 0.0f;
    }
}

public sealed class RuntimeCamera
{
    private readonly OrbitCamera _camera;
    private readonly RuntimeCameraControllerComponent _controller;
    private readonly Func<IEnumerable<RuntimeEntity>> _getEntities;
    private readonly GameProjectScene _scene;
    private readonly SceneRenderTextureManager? _renderTextureManager;

    internal RuntimeCamera(
        OrbitCamera camera,
        RuntimeCameraControllerComponent controller,
        Func<IEnumerable<RuntimeEntity>> getEntities,
        GameProjectScene scene,
        SceneRenderTextureManager? renderTextureManager)
    {
        _camera = camera;
        _controller = controller;
        _getEntities = getEntities;
        _scene = scene;
        _renderTextureManager = renderTextureManager;
    }

    public Vector3 Position => _camera.Position;

    public Vector3 Target => _camera.Target;

    public Vector3 Forward => _camera.Front;

    public Vector3 Up => _camera.Up;

    public Vector3 Right => _camera.Right;

    public int Width => _camera.Width;

    public int Height => _camera.Height;

    public string ControlMode => _controller.Mode;

    public string TargetEntity
    {
        get => _controller.TargetEntity;
        set => _controller.SetTarget(value);
    }

    public string SubjectEntity
    {
        get => _controller.SubjectEntity;
        set => _controller.SetSubject(value);
    }

    public float Distance
    {
        get => _controller.Distance;
        set => _controller.Distance = Math.Max(0.01f, value);
    }

    public float HeightOffset
    {
        get => _controller.Height;
        set => _controller.Height = value;
    }

    public float ShoulderOffset
    {
        get => _controller.ShoulderOffset;
        set => _controller.ShoulderOffset = value;
    }

    public float Smoothing
    {
        get => _controller.Smoothing;
        set => _controller.Smoothing = Math.Max(0.0f, value);
    }

    public float MoveSpeed
    {
        get => _controller.MoveSpeed;
        set => _controller.MoveSpeed = Math.Max(0.0f, value);
    }

    public float MouseSensitivity
    {
        get => _controller.MouseSensitivity;
        set => _controller.MouseSensitivity = Math.Max(0.0f, value);
    }

    public float Fov
    {
        get => _camera.Fov;
        set => _camera.Fov = value;
    }

    public float OrthographicSize
    {
        get => _camera.OrthographicSize;
        set => _camera.OrthographicSize = value;
    }

    public float NearClipPlane
    {
        get => _camera.NearClipPlane;
        set => _camera.NearClipPlane = value;
    }

    public float FarClipPlane
    {
        get => _camera.FarClipPlane;
        set => _camera.FarClipPlane = value;
    }

    public string ProjectionMode
    {
        get => _camera.ProjectionMode == CameraProjectionMode.Orthographic ? "orthographic" : "perspective";
        set => _camera.ProjectionMode = NormalizeProjectionMode(value) == "orthographic"
            ? CameraProjectionMode.Orthographic
            : CameraProjectionMode.Perspective;
    }

    public string MainCamera
    {
        get => _scene.MainCamera;
        set => SetMainCamera(value);
    }

    public IReadOnlyList<string> CameraNames => _scene.Cameras.Select(camera => camera.Name).ToArray();

    public IReadOnlyList<string> RenderTextureNames => _scene.RenderTextures.Select(renderTexture => renderTexture.Name).ToArray();

    public string RenderTexture(string renderTextureName) => ToRenderTextureReference(renderTextureName);

    public void SetMainCamera(string cameraName)
    {
        SceneCameraSettings? camera = FindSceneCamera(cameraName);
        if (camera is null)
        {
            return;
        }

        foreach (SceneCameraSettings item in _scene.Cameras)
        {
            item.IsMain = ReferenceEquals(item, camera);
        }

        _scene.MainCamera = camera.Name;
        _scene.Camera = camera.Camera;
        ApplyCameraSettings(_camera, camera.Camera);
        _renderTextureManager?.SyncCameras(_camera);
    }

    public void SetCameraLookAt(string cameraName, float positionX, float positionY, float positionZ, float targetX, float targetY, float targetZ)
    {
        SceneCameraSettings? camera = FindSceneCamera(cameraName);
        if (camera is null)
        {
            return;
        }

        camera.Camera.Position = new Vector3Dto(positionX, positionY, positionZ);
        camera.Camera.Target = new Vector3Dto(targetX, targetY, targetZ);
        _renderTextureManager?.SyncCameras(_camera);
        if (camera.IsMain)
        {
            ApplyCameraSettings(_camera, camera.Camera);
        }
    }

    public void SetCameraViewport(string cameraName, float x, float y, float width, float height, string layoutMode = "relative")
    {
        SceneCameraSettings? camera = FindSceneCamera(cameraName);
        if (camera is null)
        {
            return;
        }

        camera.Viewport.Enabled = true;
        camera.Viewport.LayoutMode = LayoutResolver.NormalizeLayoutMode(layoutMode);
        camera.Viewport.X = Math.Max(0.0f, x);
        camera.Viewport.Y = Math.Max(0.0f, y);
        camera.Viewport.Width = Math.Max(1.0f, width);
        camera.Viewport.Height = Math.Max(1.0f, height);
        _renderTextureManager?.SyncCameras(_camera);
    }

    public void EnableCameraViewport(string cameraName, bool enabled)
    {
        SceneCameraSettings? camera = FindSceneCamera(cameraName);
        if (camera is not null)
        {
            camera.Viewport.Enabled = enabled;
        }
    }

    public void BindRenderTextureCamera(string renderTextureName, string cameraName)
    {
        RenderTextureSettings? renderTexture = _scene.RenderTextures.FirstOrDefault(item =>
            string.Equals(item.Name, renderTextureName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Id, renderTextureName, StringComparison.OrdinalIgnoreCase));
        if (renderTexture is not null)
        {
            renderTexture.Camera = cameraName;
        }
    }

    public void SetLookAt(float positionX, float positionY, float positionZ, float targetX, float targetY, float targetZ)
    {
        _camera.SetLookAt(new Vector3(positionX, positionY, positionZ), new Vector3(targetX, targetY, targetZ));
    }

    public void SetLookAt(Vector3 position, Vector3 target)
    {
        _camera.SetLookAt(position, target);
    }

    public void SetControlMode(string mode)
    {
        _controller.SetMode(mode);
    }

    public void SetMode(string mode)
    {
        SetControlMode(mode);
    }

    public void ConfigureControl(
        float? distance = null,
        float? height = null,
        float? shoulderOffset = null,
        float? smoothing = null,
        float? moveSpeed = null,
        float? mouseSensitivity = null,
        float? safeRadius = null,
        float? autoOrbitSpeed = null)
    {
        _controller.Configure(distance, height, shoulderOffset, smoothing, moveSpeed, mouseSensitivity, safeRadius, autoOrbitSpeed);
    }

    public void SetYawPitch(float yawDegrees, float pitchDegrees)
    {
        _controller.SetAngles(yawDegrees, pitchDegrees);
    }

    public void SetMouseLook(bool enabled, bool requireRightMouse = true)
    {
        _controller.SetMouseLook(enabled, requireRightMouse);
    }

    public void UseCustomMode()
    {
        _controller.Custom();
    }

    public void UseEditorOrbitMode(float orbitSensitivity = 0.2f, float panSensitivity = 1.0f, float zoomSensitivity = 1.0f)
    {
        _controller.EditorOrbit(orbitSensitivity, panSensitivity, zoomSensitivity);
    }

    public void UseMaxEditorMode(float orbitSensitivity = 0.2f, float panSensitivity = 1.0f, float zoomSensitivity = 1.0f)
    {
        UseEditorOrbitMode(orbitSensitivity, panSensitivity, zoomSensitivity);
    }

    public void UseThirdPersonMode(string target, float distance = 5.0f, float height = 1.5f, float shoulderOffset = 0.0f, float smoothing = 12.0f)
    {
        _controller.ThirdPerson(target, distance, height, shoulderOffset, smoothing);
    }

    public void UseTpsMode(string target, float distance = 5.0f, float height = 1.5f, float shoulderOffset = 0.0f, float smoothing = 12.0f)
    {
        UseThirdPersonMode(target, distance, height, shoulderOffset, smoothing);
    }

    public void UseShoulderMode(string target, float distance = 4.0f, float height = 1.6f, float shoulderOffset = 0.55f, float smoothing = 12.0f)
    {
        _controller.Shoulder(target, distance, height, shoulderOffset, smoothing);
    }

    public void UseLockOnMode(
        string subject,
        string target,
        float distance = 5.0f,
        float height = 1.6f,
        float smoothing = 12.0f,
        float safeRadius = 0.25f,
        float shoulderOffset = 0.0f)
    {
        _controller.LockOn(subject, target, distance, height, smoothing, safeRadius);
        _controller.ShoulderOffset = shoulderOffset;
    }

    public void UseFirstPersonMode(string target, float eyeHeight = 1.65f, float smoothing = 18.0f)
    {
        _controller.FirstPerson(target, eyeHeight, smoothing);
    }

    public void UseFpsMode(string target, float eyeHeight = 1.65f, float smoothing = 18.0f)
    {
        UseFirstPersonMode(target, eyeHeight, smoothing);
    }

    public void UseFreeFlyMode(float moveSpeed = 5.0f, float mouseSensitivity = 0.15f)
    {
        _controller.FreeFly(moveSpeed, mouseSensitivity);
    }

    public void UseRtsMode(float height = 12.0f, float pitch = 55.0f, float moveSpeed = 8.0f)
    {
        _controller.Rts(height, pitch, moveSpeed);
    }

    public void UseTopDownMode(string target = "", float height = 12.0f, float smoothing = 12.0f)
    {
        _controller.TopDown(target, height, smoothing);
    }

    public void UseIsometricMode(string target = "", float distance = 12.0f, float height = 0.0f, float smoothing = 12.0f)
    {
        _controller.Isometric(target, distance, height, smoothing);
    }

    public void UseSideScrollerMode(string target, float distance = 10.0f, float height = 1.5f, float smoothing = 12.0f)
    {
        _controller.SideScroller(target, distance, height, smoothing);
    }

    public void UseFixedMode(float positionX, float positionY, float positionZ, float targetX, float targetY, float targetZ)
    {
        _controller.Fixed(new Vector3(positionX, positionY, positionZ), new Vector3(targetX, targetY, targetZ));
    }

    public void UseFixedMode(Vector3 position, Vector3 target)
    {
        _controller.Fixed(position, target);
    }

    public void UseCinematicFollowMode(
        string target,
        float offsetX = 0.0f,
        float offsetY = 1.8f,
        float offsetZ = 5.0f,
        float lookHeight = 1.5f,
        float smoothing = 8.0f)
    {
        _controller.CinematicFollow(target, new Vector3(offsetX, offsetY, offsetZ), lookHeight, smoothing);
    }

    public void UseOrbitalFollowMode(string target, float distance = 6.0f, float height = 1.5f, float yawSpeed = 30.0f, float smoothing = 12.0f)
    {
        _controller.OrbitalFollow(target, distance, height, yawSpeed, smoothing);
    }

    public void Orbit(float deltaYawDegrees, float deltaPitchDegrees)
    {
        _camera.Orbit(deltaYawDegrees, deltaPitchDegrees);
    }

    public void Pan(float deltaPixelsX, float deltaPixelsY)
    {
        _camera.Pan(deltaPixelsX, deltaPixelsY);
    }

    public void Dolly(float delta)
    {
        _camera.Dolly(delta);
    }

    public RuntimeRay ScreenPointToRay(float screenX, float screenY)
    {
        CameraRay ray = _camera.ScreenPointToRay(screenX, screenY);
        return new RuntimeRay(ray.Origin, ray.Direction);
    }

    public RuntimeRay ViewportPointToRay(float viewportX, float viewportY)
    {
        CameraRay ray = _camera.ViewportPointToRay(viewportX, viewportY);
        return new RuntimeRay(ray.Origin, ray.Direction);
    }

    public RuntimeRay MousePointToRay(RuntimeInput input)
    {
        return ScreenPointToRay(input.MouseX, input.MouseY);
    }

    public RuntimeEntity? PickEntity(float screenX, float screenY, float radius = 0.5f)
    {
        return RaycastEntity(ScreenPointToRay(screenX, screenY), out RuntimeRaycastHit hit, radius)
            ? hit.Entity
            : null;
    }

    public bool RaycastEntity(RuntimeRay ray, out RuntimeRaycastHit hit, float fallbackRadius = 0.5f)
    {
        hit = default;
        RuntimeEntity? bestColliderEntity = null;
        RuntimeCollider bestCollider = default;
        Vector3 bestColliderPoint = default;
        float bestColliderDistance = float.MaxValue;
        RuntimeEntity? bestFallbackEntity = null;
        Vector3 bestFallbackPoint = default;
        float bestFallbackDistance = float.MaxValue;
        float safeRadius = Math.Max(fallbackRadius, 0.001f);

        foreach (RuntimeEntity entity in _getEntities())
        {
            if (RuntimePhysics.TryRaycastEntity(
                entity,
                ray,
                out RuntimeCollider collider,
                out float distance,
                out Vector3 point))
            {
                if (distance < bestColliderDistance)
                {
                    bestColliderEntity = entity;
                    bestCollider = collider;
                    bestColliderDistance = distance;
                    bestColliderPoint = point;
                }
            }

            if (!entity.CollisionEnabled && CanUseFallbackRaycast(entity))
            {
                float scaledRadius = safeRadius * MathF.Max(MathF.Max(entity.Scale.X, entity.Scale.Y), entity.Scale.Z);
                if (ray.TryIntersectSphere(entity.Position, scaledRadius, out float fallbackDistance)
                    && fallbackDistance < bestFallbackDistance)
                {
                    bestFallbackEntity = entity;
                    bestFallbackDistance = fallbackDistance;
                    bestFallbackPoint = ray.GetPoint(fallbackDistance);
                }
            }
        }

        if (bestColliderEntity is not null)
        {
            hit = new RuntimeRaycastHit(
                bestColliderEntity,
                bestCollider.Id,
                bestCollider.Name,
                bestCollider.Shape,
                bestColliderDistance,
                bestColliderPoint);
            return true;
        }

        if (bestFallbackEntity is null)
        {
            return false;
        }

        hit = new RuntimeRaycastHit(bestFallbackEntity, string.Empty, string.Empty, "fallback_sphere", bestFallbackDistance, bestFallbackPoint);
        return true;
    }

    private static bool CanUseFallbackRaycast(RuntimeEntity entity)
    {
        string normalizedType = (entity.Type ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalizedType is "pmx_model";
    }

    private static string NormalizeProjectionMode(string projectionMode)
    {
        string normalized = (projectionMode ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized is "orthographic" or "ortho"
            ? "orthographic"
            : "perspective";
    }

    private SceneCameraSettings? FindSceneCamera(string cameraName)
    {
        if (string.IsNullOrWhiteSpace(cameraName))
        {
            return null;
        }

        return _scene.Cameras.FirstOrDefault(item =>
            string.Equals(item.Name, cameraName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Id, cameraName, StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyCameraSettings(OrbitCamera target, CameraSettings settings)
    {
        target.SetLookAt(settings.Position.ToVector3(), settings.Target.ToVector3());
        target.ProjectionMode = NormalizeProjectionMode(settings.ProjectionMode) == "orthographic"
            ? CameraProjectionMode.Orthographic
            : CameraProjectionMode.Perspective;
        target.Fov = settings.Fov;
        target.OrthographicSize = settings.OrthographicSize;
        target.NearClipPlane = settings.NearClipPlane;
        target.FarClipPlane = settings.FarClipPlane;
    }

    private static string ToRenderTextureReference(string renderTextureName)
    {
        string trimmed = (renderTextureName ?? string.Empty).Trim();
        return trimmed.StartsWith("rt:", StringComparison.OrdinalIgnoreCase) ? trimmed : $"rt:{trimmed}";
    }
}

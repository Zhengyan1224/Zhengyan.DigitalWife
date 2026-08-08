using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Helpers;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public enum CameraProjectionMode
{
    Perspective = 0,
    Orthographic = 1
}

public readonly record struct CameraRay(Vector3 Origin, Vector3 Direction);

public class OrbitCamera
{
    private const float MinOrbitDistance = 0.1f;

    private Vector3 _position = Vector3.Zero;
    private Vector3 _target = Vector3.Zero;
    private Vector3 _front = -Vector3.UnitZ;
    private Vector3 _up = Vector3.UnitY;
    private Vector3 _right = Vector3.UnitX;
    private float _pitch;
    private float _yaw = -MathHelper.PiOver2;
    private float _fov = MathHelper.PiOver2;
    private float _orthographicSize = 5.0f;
    private float _nearClipPlane = 0.1f;
    private float _farClipPlane = 1000.0f;

    internal Matrix4x4? ProjectionOverride { get; set; }

    public int Width { get; set; } = 1;

    public int Height { get; set; } = 1;

    public Vector3 Position => _position;

    public Vector3 Target => _target;

    public Vector3 Front => _front;

    public Vector3 Up => _up;

    public Vector3 Right => _right;

    public float Pitch => MathHelper.RadiansToDegrees(_pitch);

    public float Yaw => MathHelper.RadiansToDegrees(_yaw);

    public float Fov
    {
        get => MathHelper.RadiansToDegrees(_fov);
        set => _fov = MathHelper.DegreesToRadians(MathHelper.Clamp(value, 1f, 90f));
    }

    public CameraProjectionMode ProjectionMode { get; set; } = CameraProjectionMode.Perspective;

    public float OrthographicSize
    {
        get => _orthographicSize;
        set => _orthographicSize = MathHelper.Clamp(value, 0.01f, 10000.0f);
    }

    public float NearClipPlane
    {
        get => _nearClipPlane;
        set => _nearClipPlane = MathF.Max(0.001f, value);
    }

    public float FarClipPlane
    {
        get => _farClipPlane;
        set => _farClipPlane = MathF.Max(NearClipPlane + 0.001f, value);
    }

    public float DistanceToTarget => MathF.Max(Vector3.Distance(_position, _target), MinOrbitDistance);

    public Matrix4x4 View => Matrix4x4.CreateLookAt(_position, _target, _up);

    public Matrix4x4 Projection
    {
        get
        {
            if (ProjectionOverride is Matrix4x4 projectionOverride)
            {
                return projectionOverride;
            }

            float aspect = (float)Math.Max(Width, 1) / Math.Max(Height, 1);
            return ProjectionMode == CameraProjectionMode.Orthographic
                ? Matrix4x4.CreateOrthographic(OrthographicSize * 2.0f * aspect, OrthographicSize * 2.0f, NearClipPlane, FarClipPlane)
                : Matrix4x4.CreatePerspectiveFieldOfView(_fov, aspect, NearClipPlane, FarClipPlane);
        }
    }

    public void SetLookAt(Vector3 newPosition, Vector3 newTarget)
    {
        SetLookAtCore(newPosition, newTarget, preferredUp: null);
    }

    public void SetLookAt(Vector3 newPosition, Vector3 newTarget, Vector3 preferredUp)
    {
        SetLookAtCore(newPosition, newTarget, preferredUp);
    }

    private void SetLookAtCore(Vector3 newPosition, Vector3 newTarget, Vector3? preferredUp)
    {
        _position = newPosition;
        _target = newTarget;

        if (Vector3.DistanceSquared(_position, _target) < MinOrbitDistance * MinOrbitDistance)
        {
            _target = _position + _front * MinOrbitDistance;
        }

        UpdateVectorsFromLookAt(preferredUp);
    }

    public void Orbit(float deltaYawDegrees, float deltaPitchDegrees)
    {
        SetOrbitAngles(Yaw + deltaYawDegrees, Pitch + deltaPitchDegrees);
    }

    public void Pan(float deltaPixelsX, float deltaPixelsY)
    {
        float viewportHeight = MathF.Max(Height, 1);
        float distance = DistanceToTarget;
        float worldUnitsPerPixel = 2.0f * MathF.Tan(_fov * 0.5f) * distance / viewportHeight;

        Vector3 offset = (-_right * deltaPixelsX + _up * deltaPixelsY) * worldUnitsPerPixel;
        _position += offset;
        _target += offset;
    }

    public void Dolly(float delta)
    {
        float distance = DistanceToTarget;
        float step = MathF.Max(distance * 0.15f, 0.05f);
        float newDistance = MathF.Max(MinOrbitDistance, distance - (delta * step));
        _position = _target - (_front * newDistance);
    }

    public CameraRay ScreenPointToRay(float screenX, float screenY)
    {
        float viewportX = screenX / Math.Max(Width, 1);
        float viewportY = screenY / Math.Max(Height, 1);
        return ViewportPointToRay(viewportX, viewportY);
    }

    public CameraRay ViewportPointToRay(float viewportX, float viewportY)
    {
        float ndcX = (viewportX * 2.0f) - 1.0f;
        float ndcY = 1.0f - (viewportY * 2.0f);
        float aspect = (float)Math.Max(Width, 1) / Math.Max(Height, 1);

        if (ProjectionMode == CameraProjectionMode.Orthographic)
        {
            Vector3 origin = _position
                + (_right * ndcX * OrthographicSize * aspect)
                + (_up * ndcY * OrthographicSize);
            return new CameraRay(origin, _front);
        }

        float tanHalfFov = MathF.Tan(_fov * 0.5f);
        Vector3 direction = Vector3.Normalize(_front
            + (_right * ndcX * aspect * tanHalfFov)
            + (_up * ndcY * tanHalfFov));
        return new CameraRay(_position, direction);
    }

    private void SetOrbitAngles(float yawDegrees, float pitchDegrees)
    {
        float distance = DistanceToTarget;

        _yaw = MathHelper.DegreesToRadians(yawDegrees);
        _pitch = MathHelper.DegreesToRadians(MathHelper.Clamp(pitchDegrees, -89f, 89f));

        UpdateVectorsFromAngles();
        _position = _target - (_front * distance);
    }

    private void UpdateVectorsFromLookAt(Vector3? preferredUp = null)
    {
        Vector3 direction = Vector3.Normalize(_target - _position);
        _front = direction;

        _yaw = MathF.Atan2(_front.Z, _front.X);
        _pitch = MathF.Asin(_front.Y);

        if (preferredUp is { } up && up.LengthSquared() > 0.0001f)
        {
            Vector3 normalizedUp = Vector3.Normalize(up);
            Vector3 right = Vector3.Cross(_front, normalizedUp);
            if (right.LengthSquared() > 0.0001f)
            {
                _right = Vector3.Normalize(right);
                _up = Vector3.Normalize(Vector3.Cross(_right, _front));
                return;
            }
        }

        UpdateRightAndUp();
    }

    private void UpdateVectorsFromAngles()
    {
        _front.X = MathF.Cos(_pitch) * MathF.Cos(_yaw);
        _front.Y = MathF.Sin(_pitch);
        _front.Z = MathF.Cos(_pitch) * MathF.Sin(_yaw);
        _front = Vector3.Normalize(_front);

        UpdateRightAndUp();
    }

    private void UpdateRightAndUp()
    {
        _right = Vector3.Normalize(Vector3.Cross(_front, Vector3.UnitY));
        _up = Vector3.Normalize(Vector3.Cross(_right, _front));
    }
}


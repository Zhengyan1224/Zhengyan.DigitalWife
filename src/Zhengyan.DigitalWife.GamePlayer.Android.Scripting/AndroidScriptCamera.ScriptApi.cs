using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

public sealed partial class AndroidScriptCamera
{
    public void BindRenderTextureCamera(System.String renderTextureName, System.String cameraName) => _scene.MainCamera.BindRenderTextureCamera(renderTextureName, cameraName);
    public System.Collections.Generic.IReadOnlyList<System.String> CameraNames { get => _scene.MainCamera.CameraNames; }
    public System.Single CameraVmdFrame(System.String cameraName) => _scene.MainCamera.CameraVmdFrame(cameraName);
    public System.Boolean CameraVmdIsPlaying(System.String cameraName) => _scene.MainCamera.CameraVmdIsPlaying(cameraName);
    public System.String CameraVmdPath(System.String cameraName) => _scene.MainCamera.CameraVmdPath(cameraName);
    public void ConfigureControl(System.Nullable<System.Single> distance = null, System.Nullable<System.Single> height = null, System.Nullable<System.Single> shoulderOffset = null, System.Nullable<System.Single> smoothing = null, System.Nullable<System.Single> moveSpeed = null, System.Nullable<System.Single> mouseSensitivity = null, System.Nullable<System.Single> safeRadius = null, System.Nullable<System.Single> autoOrbitSpeed = null) => _scene.MainCamera.ConfigureControl(distance, height, shoulderOffset, smoothing, moveSpeed, mouseSensitivity, safeRadius, autoOrbitSpeed);
    public System.String ControlMode { get => _scene.MainCamera.ControlMode; }
    public System.Single Distance { get => _scene.MainCamera.Distance; set => _scene.MainCamera.Distance = value; }
    public void Dolly(System.Single delta) => _scene.MainCamera.Dolly(delta);
    public void EnableCameraViewport(System.String cameraName, System.Boolean enabled) => _scene.MainCamera.EnableCameraViewport(cameraName, enabled);
    public System.Single FarClipPlane { get => _scene.MainCamera.FarClipPlane; set => _scene.MainCamera.FarClipPlane = value; }
    public System.Numerics.Vector3 Forward { get => _scene.MainCamera.Forward; }
    public System.Single Fov { get => _scene.MainCamera.Fov; set => _scene.MainCamera.Fov = value; }
    public System.Int32 Height { get => _scene.MainCamera.Height; }
    public System.Single HeightOffset { get => _scene.MainCamera.HeightOffset; set => _scene.MainCamera.HeightOffset = value; }
    public System.String MainCamera { get => _scene.MainCamera.MainCamera; set => _scene.MainCamera.MainCamera = value; }
    public System.Single MainCameraVmdFrame { get => _scene.MainCamera.MainCameraVmdFrame; }
    public System.Boolean MainCameraVmdIsPlaying { get => _scene.MainCamera.MainCameraVmdIsPlaying; }
    public System.String MainCameraVmdPath { get => _scene.MainCamera.MainCameraVmdPath; }
    public System.Single MouseSensitivity { get => _scene.MainCamera.MouseSensitivity; set => _scene.MainCamera.MouseSensitivity = value; }
    public System.Single MoveSpeed { get => _scene.MainCamera.MoveSpeed; set => _scene.MainCamera.MoveSpeed = value; }
    public System.Single NearClipPlane { get => _scene.MainCamera.NearClipPlane; set => _scene.MainCamera.NearClipPlane = value; }
    public void Orbit(System.Single deltaYawDegrees, System.Single deltaPitchDegrees) => _scene.MainCamera.Orbit(deltaYawDegrees, deltaPitchDegrees);
    public System.Single OrthographicSize { get => _scene.MainCamera.OrthographicSize; set => _scene.MainCamera.OrthographicSize = value; }
    public void Pan(System.Single deltaPixelsX, System.Single deltaPixelsY) => _scene.MainCamera.Pan(deltaPixelsX, deltaPixelsY);
    public RuntimeEntity? PickEntity(System.Single screenX, System.Single screenY, System.Single radius = 0.5f) => _scene.MainCamera.PickEntity(screenX, screenY, radius);
    public System.Numerics.Vector3 Position { get => _scene.MainCamera.Position; }
    public System.String ProjectionMode { get => _scene.MainCamera.ProjectionMode; set => _scene.MainCamera.ProjectionMode = value; }
    public System.String RenderTexture(System.String renderTextureName) => _scene.MainCamera.RenderTexture(renderTextureName);
    public System.Collections.Generic.IReadOnlyList<System.String> RenderTextureNames { get => _scene.MainCamera.RenderTextureNames; }
    public System.Numerics.Vector3 Right { get => _scene.MainCamera.Right; }
    public void SetCameraLookAt(System.String cameraName, System.Single positionX, System.Single positionY, System.Single positionZ, System.Single targetX, System.Single targetY, System.Single targetZ) => _scene.MainCamera.SetCameraLookAt(cameraName, positionX, positionY, positionZ, targetX, targetY, targetZ);
    public void SetCameraViewport(System.String cameraName, System.Single x, System.Single y, System.Single width, System.Single height, System.String layoutMode = "relative") => _scene.MainCamera.SetCameraViewport(cameraName, x, y, width, height, layoutMode);
    public void SetCameraVmdLoop(System.String cameraName, System.Boolean loop) => _scene.MainCamera.SetCameraVmdLoop(cameraName, loop);
    public void SetCameraVmdPlaybackSpeed(System.String cameraName, System.Single playbackSpeed) => _scene.MainCamera.SetCameraVmdPlaybackSpeed(cameraName, playbackSpeed);
    public void SetControlMode(System.String mode) => _scene.MainCamera.SetControlMode(mode);
    public void SetLookAt(System.Single positionX, System.Single positionY, System.Single positionZ, System.Single targetX, System.Single targetY, System.Single targetZ) => _scene.MainCamera.SetLookAt(positionX, positionY, positionZ, targetX, targetY, targetZ);
    public void SetLookAt(System.Numerics.Vector3 position, System.Numerics.Vector3 target) => _scene.MainCamera.SetLookAt(position, target);
    public void SetMainCamera(System.String cameraName) => _scene.MainCamera.SetMainCamera(cameraName);
    public void SetMode(System.String mode) => _scene.MainCamera.SetMode(mode);
    public void SetMouseLook(System.Boolean enabled, System.Boolean requireRightMouse = true) => _scene.MainCamera.SetMouseLook(enabled, requireRightMouse);
    public void SetYawPitch(System.Single yawDegrees, System.Single pitchDegrees) => _scene.MainCamera.SetYawPitch(yawDegrees, pitchDegrees);
    public System.Single ShoulderOffset { get => _scene.MainCamera.ShoulderOffset; set => _scene.MainCamera.ShoulderOffset = value; }
    public System.Single Smoothing { get => _scene.MainCamera.Smoothing; set => _scene.MainCamera.Smoothing = value; }
    public System.String SubjectEntity { get => _scene.MainCamera.SubjectEntity; set => _scene.MainCamera.SubjectEntity = value; }
    public System.Numerics.Vector3 Target { get => _scene.MainCamera.Target; }
    public System.String TargetEntity { get => _scene.MainCamera.TargetEntity; set => _scene.MainCamera.TargetEntity = value; }
    public System.Numerics.Vector3 Up { get => _scene.MainCamera.Up; }
    public void UseCinematicFollowMode(System.String target, System.Single offsetX = 0f, System.Single offsetY = 1.8f, System.Single offsetZ = 5f, System.Single lookHeight = 1.5f, System.Single smoothing = 8f) => _scene.MainCamera.UseCinematicFollowMode(target, offsetX, offsetY, offsetZ, lookHeight, smoothing);
    public void UseCustomMode() => _scene.MainCamera.UseCustomMode();
    public void UseEditorOrbitMode(System.Single orbitSensitivity = 0.2f, System.Single panSensitivity = 1f, System.Single zoomSensitivity = 1f) => _scene.MainCamera.UseEditorOrbitMode(orbitSensitivity, panSensitivity, zoomSensitivity);
    public void UseFirstPersonMode(System.String target, System.Single eyeHeight = 1.65f, System.Single smoothing = 18f) => _scene.MainCamera.UseFirstPersonMode(target, eyeHeight, smoothing);
    public void UseFixedMode(System.Single positionX, System.Single positionY, System.Single positionZ, System.Single targetX, System.Single targetY, System.Single targetZ) => _scene.MainCamera.UseFixedMode(positionX, positionY, positionZ, targetX, targetY, targetZ);
    public void UseFixedMode(System.Numerics.Vector3 position, System.Numerics.Vector3 target) => _scene.MainCamera.UseFixedMode(position, target);
    public void UseFpsControlMode(System.String target, System.Single eyeHeight = 1.65f, System.Single smoothing = 18f, System.Single mouseSensitivity = 0.15f) => _scene.MainCamera.UseFpsControlMode(target, eyeHeight, smoothing, mouseSensitivity);
    public void UseFpsMode(System.String target, System.Single eyeHeight = 1.65f, System.Single smoothing = 18f) => _scene.MainCamera.UseFpsMode(target, eyeHeight, smoothing);
    public void UseFreeFlyMode(System.Single moveSpeed = 5f, System.Single mouseSensitivity = 0.15f) => _scene.MainCamera.UseFreeFlyMode(moveSpeed, mouseSensitivity);
    public void UseIsometricMode(System.String target = "", System.Single distance = 12f, System.Single height = 0f, System.Single smoothing = 12f) => _scene.MainCamera.UseIsometricMode(target, distance, height, smoothing);
    public void UseLockedFpsMode(System.String target, System.Single eyeHeight = 1.65f, System.Single smoothing = 18f, System.Single mouseSensitivity = 0.15f) => _scene.MainCamera.UseLockedFpsMode(target, eyeHeight, smoothing, mouseSensitivity);
    public void UseLockOnMode(System.String subject, System.String target, System.Single distance = 5f, System.Single height = 1.6f, System.Single smoothing = 12f, System.Single safeRadius = 0.25f, System.Single shoulderOffset = 0f) => _scene.MainCamera.UseLockOnMode(subject, target, distance, height, smoothing, safeRadius, shoulderOffset);
    public void UseMaxEditorMode(System.Single orbitSensitivity = 0.2f, System.Single panSensitivity = 1f, System.Single zoomSensitivity = 1f) => _scene.MainCamera.UseMaxEditorMode(orbitSensitivity, panSensitivity, zoomSensitivity);
    public void UseOrbitalFollowMode(System.String target, System.Single distance = 6f, System.Single height = 1.5f, System.Single yawSpeed = 30f, System.Single smoothing = 12f) => _scene.MainCamera.UseOrbitalFollowMode(target, distance, height, yawSpeed, smoothing);
    public void UseRtsMode(System.Single height = 12f, System.Single pitch = 55f, System.Single moveSpeed = 8f) => _scene.MainCamera.UseRtsMode(height, pitch, moveSpeed);
    public void UseShoulderMode(System.String target, System.Single distance = 4f, System.Single height = 1.6f, System.Single shoulderOffset = 0.55f, System.Single smoothing = 12f) => _scene.MainCamera.UseShoulderMode(target, distance, height, shoulderOffset, smoothing);
    public void UseSideScrollerMode(System.String target, System.Single distance = 10f, System.Single height = 1.5f, System.Single smoothing = 12f) => _scene.MainCamera.UseSideScrollerMode(target, distance, height, smoothing);
    public void UseThirdPersonMode(System.String target, System.Single distance = 5f, System.Single height = 1.5f, System.Single shoulderOffset = 0f, System.Single smoothing = 12f) => _scene.MainCamera.UseThirdPersonMode(target, distance, height, shoulderOffset, smoothing);
    public void UseTopDownMode(System.String target = "", System.Single height = 12f, System.Single smoothing = 12f) => _scene.MainCamera.UseTopDownMode(target, height, smoothing);
    public void UseTpsMode(System.String target, System.Single distance = 5f, System.Single height = 1.5f, System.Single shoulderOffset = 0f, System.Single smoothing = 12f) => _scene.MainCamera.UseTpsMode(target, distance, height, shoulderOffset, smoothing);
    public System.Int32 Width { get => _scene.MainCamera.Width; }
}

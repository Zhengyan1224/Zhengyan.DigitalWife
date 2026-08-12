using System.Numerics;

namespace Zhengyan.DigitalWife.GameProjects;

public sealed class SceneVmdAnimationController : IDisposable
{
    private readonly Func<string, string> _resolvePath;
    private readonly Dictionary<string, VmdSceneAnimationPlayer> _cameraPlayers = new(StringComparer.OrdinalIgnoreCase);
    private readonly VmdSceneAnimationPlayer _lightingPlayer = new();

    public SceneVmdAnimationController(Func<string, string> resolvePath)
    {
        _resolvePath = resolvePath;
    }

    public bool Update(GameProjectScene scene, float deltaSeconds)
    {
        bool changed = false;
        HashSet<string> activeIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (SceneCameraSettings camera in scene.Cameras)
        {
            string id = string.IsNullOrWhiteSpace(camera.Id) ? camera.Name : camera.Id;
            activeIds.Add(id);
            if (!_cameraPlayers.TryGetValue(id, out VmdSceneAnimationPlayer? player))
            {
                player = new VmdSceneAnimationPlayer();
                _cameraPlayers[id] = player;
            }

            VmdPlaybackSettings playback = camera.Camera.Vmd;
            if (!player.Load(Resolve(playback.Path)))
            {
                continue;
            }

            VmdSceneAnimationPlayer.Update(playback, deltaSeconds, player.CameraMaxFrame);
            playback.Frame = Math.Clamp(playback.Frame, 0.0f, player.CameraMaxFrame);
            if (player.TrySampleCamera(playback.Frame, out VmdCameraPose pose))
            {
                camera.Camera.Position = Vector3Dto.FromVector3(pose.Position);
                camera.Camera.Target = Vector3Dto.FromVector3(pose.Target);
                camera.Camera.Fov = Math.Clamp(pose.Fov, 1.0f, 179.0f);
                camera.Camera.ProjectionMode = pose.Perspective ? "perspective" : "orthographic";
                changed = true;
            }
        }

        foreach (string staleId in _cameraPlayers.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            _cameraPlayers[staleId].Dispose();
            _cameraPlayers.Remove(staleId);
        }

        VmdPlaybackSettings lightingPlayback = scene.Lighting.Vmd;
        if (_lightingPlayer.Load(Resolve(lightingPlayback.Path)))
        {
            VmdSceneAnimationPlayer.Update(lightingPlayback, deltaSeconds, _lightingPlayer.LightMaxFrame);
            lightingPlayback.Frame = Math.Clamp(lightingPlayback.Frame, 0.0f, _lightingPlayer.LightMaxFrame);
            if (_lightingPlayer.TrySampleLight(lightingPlayback.Frame, out VmdLightPose pose))
            {
                scene.Lighting.LightColor = Vector3Dto.FromVector3(Vector3.Max(pose.Color, Vector3.Zero));
                if (pose.Position.LengthSquared() > 1e-10f)
                {
                    scene.Lighting.LightDirection = Vector3Dto.FromVector3(Vector3.Normalize(pose.Position));
                }
                changed = true;
            }
        }

        return changed;
    }

    public void Dispose()
    {
        foreach (VmdSceneAnimationPlayer player in _cameraPlayers.Values)
        {
            player.Dispose();
        }

        _cameraPlayers.Clear();
        _lightingPlayer.Dispose();
    }

    private string Resolve(string path) => string.IsNullOrWhiteSpace(path) ? string.Empty : _resolvePath(path);
}

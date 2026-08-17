using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

internal sealed class RuntimeSceneAnimationController : IDisposable
{
    private readonly Func<string, string> _resolvePath;
    private readonly Dictionary<string, Track> _cameraTracks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Track _lightTrack = new();

    public RuntimeSceneAnimationController(Func<string, string> resolvePath)
    {
        _resolvePath = resolvePath;
    }

    public void Update(GameProjectScene scene, float deltaSeconds)
    {
        HashSet<string> active = new(StringComparer.OrdinalIgnoreCase);
        foreach (SceneCameraSettings camera in scene.Cameras)
        {
            camera.Camera.VmdHasUp = false;
            string id = string.IsNullOrWhiteSpace(camera.Id) ? camera.Name : camera.Id;
            active.Add(id);
            if (!_cameraTracks.TryGetValue(id, out Track? track))
            {
                track = new Track();
                _cameraTracks[id] = track;
            }

            if (!track.Load(Resolve(camera.Camera.Vmd.Path))) continue;
            Advance(camera.Camera.Vmd, deltaSeconds, track.CameraMaxFrame);
            if (track.TrySampleCamera(camera.Camera.Vmd.Frame, out CameraPose pose))
            {
                camera.Camera.Position = Vector3Dto.FromVector3(pose.Position);
                camera.Camera.Target = Vector3Dto.FromVector3(pose.Target);
                camera.Camera.VmdUp = Vector3Dto.FromVector3(pose.Up);
                camera.Camera.VmdHasUp = true;
                camera.Camera.Fov = Math.Clamp(pose.Fov, 1.0f, 179.0f);
                camera.Camera.ProjectionMode = pose.Perspective ? "perspective" : "orthographic";
            }
        }

        foreach (string stale in _cameraTracks.Keys.Where(key => !active.Contains(key)).ToArray())
        {
            _cameraTracks.Remove(stale);
        }

        if (_lightTrack.Load(Resolve(scene.Lighting.Vmd.Path)))
        {
            Advance(scene.Lighting.Vmd, deltaSeconds, _lightTrack.LightMaxFrame);
            if (_lightTrack.TrySampleLight(scene.Lighting.Vmd.Frame, out LightPose light))
            {
                scene.Lighting.LightColor = Vector3Dto.FromVector3(Vector3.Max(light.Color, Vector3.Zero));
                if (light.Direction.LengthSquared() > 1e-8f)
                    scene.Lighting.LightDirection = Vector3Dto.FromVector3(Vector3.Normalize(light.Direction));
            }
        }
    }

    public void Dispose() => _cameraTracks.Clear();

    private string Resolve(string path) => string.IsNullOrWhiteSpace(path) ? string.Empty : _resolvePath(path);

    private static void Advance(VmdPlaybackSettings settings, float deltaSeconds, int maxFrame)
    {
        if (!settings.IsPlaying || maxFrame <= 0) return;
        settings.Frame += Math.Max(deltaSeconds, 0.0f) * 30.0f * Math.Max(settings.PlaybackSpeed, 0.0f);
        if (settings.Loop)
        {
            settings.Frame %= maxFrame;
        }
        else if (settings.Frame >= maxFrame)
        {
            settings.Frame = maxFrame;
            settings.IsPlaying = false;
        }
    }

    private readonly record struct CameraPose(Vector3 Position, Vector3 Target, Vector3 Up, float Fov, bool Perspective);
    private readonly record struct LightPose(Vector3 Color, Vector3 Direction);

    private sealed class Track
    {
        private string _path = string.Empty;
        private bool _attempted;
        private VmdParsing? _vmd;

        public int CameraMaxFrame { get; private set; }
        public int LightMaxFrame { get; private set; }

        public bool Load(string path)
        {
            if (_attempted && string.Equals(path, _path, StringComparison.OrdinalIgnoreCase)) return _vmd is not null;
            _attempted = true;
            _path = path;
            _vmd = null;
            CameraMaxFrame = 0;
            LightMaxFrame = 0;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            try
            {
                _vmd = VmdParsing.ParsingByFile(path);
                if (_vmd is null) return false;
                Array.Sort(_vmd.Cameras, static (left, right) => left.Frame.CompareTo(right.Frame));
                Array.Sort(_vmd.Lights, static (left, right) => left.Frame.CompareTo(right.Frame));
                CameraMaxFrame = _vmd.Cameras.Length == 0 ? 0 : (int)_vmd.Cameras[^1].Frame;
                LightMaxFrame = _vmd.Lights.Length == 0 ? 0 : (int)_vmd.Lights[^1].Frame;
                return true;
            }
            catch
            {
                _vmd = null;
                return false;
            }
        }

        public bool TrySampleCamera(float frame, out CameraPose pose)
        {
            pose = default;
            if (_vmd?.Cameras is not { Length: > 0 } keys) return false;
            (VmdCamera a, VmdCamera b) = Bounds(keys, frame, static key => key.Frame);
            float t = b.Frame == a.Frame ? 0.0f : Math.Clamp((frame - a.Frame) / (b.Frame - a.Frame), 0.0f, 1.0f);
            Vector3 interest = FlipZ(new Vector3(
                Lerp(a.Interest.X, b.Interest.X, Curve(a.Interpolation, 0, t)),
                Lerp(a.Interest.Y, b.Interest.Y, Curve(a.Interpolation, 4, t)),
                Lerp(a.Interest.Z, b.Interest.Z, Curve(a.Interpolation, 8, t))));
            Vector3 rotationEuler = Vector3.Lerp(a.Rotate, b.Rotate, Curve(a.Interpolation, 12, t));
            float distance = Lerp(a.Distance, b.Distance, Curve(a.Interpolation, 16, t));
            float fov = Lerp(a.ViewAngle, b.ViewAngle, Curve(a.Interpolation, 20, t));
            Quaternion rotation = Quaternion.CreateFromYawPitchRoll(rotationEuler.Y, rotationEuler.X, -rotationEuler.Z);
            Vector3 forward = Vector3.Transform(-Vector3.UnitZ, rotation);
            Vector3 up = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, rotation));
            pose = new CameraPose(interest + forward * distance, interest, up, fov, a.IsPerspective);
            return true;
        }

        public bool TrySampleLight(float frame, out LightPose pose)
        {
            pose = default;
            if (_vmd?.Lights is not { Length: > 0 } keys) return false;
            (VmdLight a, VmdLight b) = Bounds(keys, frame, static key => key.Frame);
            float t = b.Frame == a.Frame ? 0.0f : Math.Clamp((frame - a.Frame) / (b.Frame - a.Frame), 0.0f, 1.0f);
            pose = new LightPose(Vector3.Lerp(a.Color, b.Color, t), Vector3.Lerp(FlipZ(a.Position), FlipZ(b.Position), t));
            return true;
        }

        private static (T A, T B) Bounds<T>(T[] keys, float frame, Func<T, uint> getFrame)
        {
            if (frame <= getFrame(keys[0])) return (keys[0], keys[0]);
            for (int i = 1; i < keys.Length; i++)
                if (getFrame(keys[i]) >= frame) return (keys[i - 1], keys[i]);
            return (keys[^1], keys[^1]);
        }

        private static float Curve(byte[] values, int offset, float time)
        {
            if (values.Length < offset + 4) return time;
            return new VmdInterpolationCurve(
                new Vector2(values[offset] / 127.0f, values[offset + 2] / 127.0f),
                new Vector2(values[offset + 1] / 127.0f, values[offset + 3] / 127.0f)).Evaluate(time);
        }

        private static float Lerp(float left, float right, float amount) => left + (right - left) * amount;
        private static Vector3 FlipZ(Vector3 value) => new(value.X, value.Y, -value.Z);
    }
}

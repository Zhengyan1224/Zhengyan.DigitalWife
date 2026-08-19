using Android.Media;
using Android.Util;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidAudioHost : IDisposable
{
    private const string LogTag = "ZhengyanGamePlayer";
    private readonly string _projectDirectory;
    private readonly Dictionary<string, MediaPlayer> _players = new(StringComparer.OrdinalIgnoreCase);

    public AndroidAudioHost(string projectDirectory) => _projectDirectory = projectDirectory;

    public void StartScene(RuntimeScene scene)
    {
        StopAll();
        foreach (AudioAsset audio in scene.Definition.Audio.Where(asset => asset.PlayOnStart))
        {
            Play(audio);
        }
    }

    public bool Play(RuntimeScene scene, string idOrName)
    {
        AudioAsset? asset = scene.Definition.Audio.FirstOrDefault(audio =>
            string.Equals(audio.Name, idOrName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(audio.Path, idOrName, StringComparison.OrdinalIgnoreCase));
        return asset is not null && Play(asset);
    }

    public bool Stop(string idOrName)
    {
        if (!_players.Remove(idOrName, out MediaPlayer? player))
        {
            return false;
        }
        try { player.Stop(); } catch { }
        player.Dispose();
        return true;
    }

    private bool Play(AudioAsset asset)
    {
        string path = GameProjectPath.ToAbsolute(_projectDirectory, asset.Path);
        if (!File.Exists(path))
        {
            Log.Warn(LogTag, $"Android audio file not found: {path}");
            return false;
        }

        Stop(asset.Name);
        try
        {
            MediaPlayer player = new();
            player.SetAudioAttributes(new AudioAttributes.Builder()!
                .SetContentType(AudioContentType.Music)!
                .SetUsage(AudioUsageKind.Game)!
                .Build());
            player.SetDataSource(path);
            player.Looping = asset.Loop;
            float volume = Math.Clamp(asset.Volume, 0.0f, 1.0f);
            player.SetVolume(volume, volume);
            player.Prepare();
            player.Start();
            _players[asset.Name] = player;
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(LogTag, $"Android audio failed '{asset.Name}': {ex.GetBaseException().Message}");
            return false;
        }
    }

    public void StopAll()
    {
        foreach (MediaPlayer player in _players.Values)
        {
            try { player.Stop(); } catch { }
            player.Dispose();
        }
        _players.Clear();
    }

    public void Dispose() => StopAll();
}

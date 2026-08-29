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
    private readonly HashSet<string> _paused = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

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
        string key = ResolveKey(idOrName);
        if (!_players.Remove(key, out MediaPlayer? player))
        {
            return false;
        }
        try { player.Stop(); } catch { }
        player.Dispose();
        _paused.Remove(key);
        foreach (string alias in _aliases.Where(pair => string.Equals(pair.Value, key, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).ToArray())
        {
            _aliases.Remove(alias);
        }
        return true;
    }

    public bool Pause(string idOrName)
    {
        string key = ResolveKey(idOrName);
        if (!_players.TryGetValue(key, out MediaPlayer? player))
        {
            return false;
        }

        try
        {
            if (player.IsPlaying)
            {
                player.Pause();
                _paused.Add(key);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool SetVolume(string idOrName, float volume)
    {
        string key = ResolveKey(idOrName);
        if (!_players.TryGetValue(key, out MediaPlayer? player)) return false;
        float value = Math.Clamp(volume, 0.0f, 1.0f);
        try { player.SetVolume(value, value); return true; } catch { return false; }
    }

    public bool SetLoop(string idOrName, bool loop)
    {
        string key = ResolveKey(idOrName);
        if (!_players.TryGetValue(key, out MediaPlayer? player)) return false;
        try { player.Looping = loop; return true; } catch { return false; }
    }

    public bool IsPlaying(string idOrName)
    {
        string key = ResolveKey(idOrName);
        try { return _players.TryGetValue(key, out MediaPlayer? player) && player.IsPlaying; } catch { return false; }
    }

    public int GetPosition(string idOrName)
    {
        string key = ResolveKey(idOrName);
        try { return _players.TryGetValue(key, out MediaPlayer? player) ? player.CurrentPosition : 0; } catch { return 0; }
    }

    public int GetDuration(string idOrName)
    {
        string key = ResolveKey(idOrName);
        try { return _players.TryGetValue(key, out MediaPlayer? player) ? player.Duration : 0; } catch { return 0; }
    }

    private bool Play(AudioAsset asset)
    {
        string path = GameProjectPath.ToAbsolute(_projectDirectory, asset.Path);
        if (!File.Exists(path))
        {
            Log.Warn(LogTag, $"Android audio file not found: {path}");
            return false;
        }

        RegisterAliases(asset);
        if (_players.TryGetValue(asset.Name, out MediaPlayer? existing))
        {
            try
            {
                if (_paused.Remove(asset.Name))
                {
                    existing.Start();
                }
                else
                {
                    // OpenAL SourcePlay restarts an already-playing source but
                    // resumes a paused source. Mirror that distinction here.
                    existing.SeekTo(0);
                    existing.Start();
                }
                return true;
            }
            catch
            {
                Stop(asset.Name);
            }
        }
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
            RegisterAliases(asset);
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
        _paused.Clear();
        _aliases.Clear();
    }

    private string ResolveKey(string idOrName) => _aliases.TryGetValue(idOrName, out string? key) ? key : idOrName;

    private void RegisterAliases(AudioAsset asset)
    {
        _aliases[asset.Name] = asset.Name;
        if (!string.IsNullOrWhiteSpace(asset.Path))
        {
            _aliases[asset.Path] = asset.Name;
        }
    }

    public void Dispose() => StopAll();
}

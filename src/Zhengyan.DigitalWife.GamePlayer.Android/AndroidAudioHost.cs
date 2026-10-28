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
    private readonly Dictionary<string, float> _volumes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _oneShotSync = new();
    private readonly HashSet<MediaPlayer> _oneShotPlayers = [];
    private bool _disposed;

    public AndroidAudioHost(string projectDirectory) => _projectDirectory = projectDirectory;

    public void PlayOnStart(RuntimeScene scene)
    {
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

    public bool PlayOneShot(RuntimeScene scene, string idOrName, float volumeScale = 1.0f)
    {
        AudioAsset? asset = scene.Definition.Audio.FirstOrDefault(audio =>
            string.Equals(audio.Name, idOrName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(audio.Path, idOrName, StringComparison.OrdinalIgnoreCase));
        if (asset is null) return false;

        lock (_oneShotSync)
        {
            if (_disposed) return false;
            MediaPlayer? player = null;
            try
            {
                string path = GameProjectPath.ToAbsolute(_projectDirectory, asset.Path);
                float volume = _volumes.TryGetValue(asset.Name, out float currentVolume) ? currentVolume : asset.Volume;
                player = CreatePlayer(path, Math.Clamp(volume, 0.0f, 1.0f) * Math.Clamp(volumeScale, 0.0f, 1.0f), loop: false);
                player.Completion += OnOneShotCompleted;
                player.Error += OnOneShotError;
                _oneShotPlayers.Add(player);
                player.Start();
                return true;
            }
            catch (Exception ex)
            {
                if (player is not null) ReleaseOneShot(player);
                Log.Warn(LogTag, $"Android one-shot audio failed '{asset.Name}': {ex.GetBaseException().Message}");
                return false;
            }
        }
    }

    public bool Stop(string idOrName)
    {
        string key = ResolveKey(idOrName);
        if (!_players.Remove(key, out MediaPlayer? player))
        {
            return false;
        }
        ReleasePlayer(player);
        _paused.Remove(key);
        _volumes.Remove(key);
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
        try { player.SetVolume(value, value); _volumes[key] = value; return true; } catch { return false; }
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

    public void LoadAsset(AudioAsset asset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_players.ContainsKey(asset.Name)) return;
        string path = GameProjectPath.ToAbsolute(_projectDirectory, asset.Path);
        MediaPlayer player = CreatePlayer(path, asset.Volume, asset.Loop);
        _players[asset.Name] = player;
        _volumes[asset.Name] = Math.Clamp(asset.Volume, 0.0f, 1.0f);
        RegisterAliases(asset);
    }

    private static MediaPlayer CreatePlayer(string path, float volume, bool loop)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Android audio file not found.", path);

        MediaPlayer player = new();
        try
        {
            player.SetAudioAttributes(new AudioAttributes.Builder()!
                .SetContentType(AudioContentType.Music)!
                .SetUsage(AudioUsageKind.Game)!
                .Build());
            player.SetDataSource(path);
            player.Looping = loop;
            float value = Math.Clamp(volume, 0.0f, 1.0f);
            player.SetVolume(value, value);
            player.Prepare();
            return player;
        }
        catch
        {
            ReleasePlayer(player);
            throw;
        }
    }

    private bool Play(AudioAsset asset)
    {
        try
        {
            LoadAsset(asset);
            MediaPlayer player = _players[asset.Name];
            // Resume a paused source; explicitly playing any other source restarts it.
            if (!_paused.Remove(asset.Name)) player.SeekTo(0);
            player.Start();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(LogTag, $"Android audio failed '{asset.Name}': {ex.GetBaseException().Message}");
            Stop(asset.Name);
            return false;
        }
    }
    public void StopAll()
    {
        MediaPlayer[] oneShots;
        lock (_oneShotSync)
        {
            oneShots = _oneShotPlayers.ToArray();
            _oneShotPlayers.Clear();
        }
        foreach (MediaPlayer player in oneShots) DisposeOneShot(player);
        foreach (MediaPlayer player in _players.Values)
        {
            ReleasePlayer(player);
        }
        _players.Clear();
        _paused.Clear();
        _aliases.Clear();
        _volumes.Clear();
    }

    private void OnOneShotCompleted(object? sender, EventArgs args)
    {
        if (sender is MediaPlayer player) ReleaseOneShot(player);
    }

    private void OnOneShotError(object? sender, MediaPlayer.ErrorEventArgs args)
    {
        args.Handled = true;
        if (sender is MediaPlayer player) ReleaseOneShot(player);
    }

    private void ReleaseOneShot(MediaPlayer player)
    {
        lock (_oneShotSync)
        {
            if (!_oneShotPlayers.Remove(player)) return;
        }
        DisposeOneShot(player);
    }

    private void DisposeOneShot(MediaPlayer player)
    {
        player.Completion -= OnOneShotCompleted;
        player.Error -= OnOneShotError;
        ReleasePlayer(player);
    }

    private static void ReleasePlayer(MediaPlayer player)
    {
        try { player.Stop(); } catch { }
        try { player.Release(); } catch { }
        player.Dispose();
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

    public void Dispose()
    {
        lock (_oneShotSync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        StopAll();
    }
}

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

/// <summary>Stable, platform-neutral ABI implemented by precompiled Web scripts.</summary>
public interface IWebGameScript
{
    void Start(WebScriptContext context);
    void Update(WebScriptContext context, float deltaSeconds);
    ValueTask StartAsync(WebScriptContext context) { Start(context); return ValueTask.CompletedTask; }
    ValueTask UpdateAsync(WebScriptContext context, float deltaSeconds) { Update(context, deltaSeconds); return ValueTask.CompletedTask; }
    ValueTask OnEventAsync(WebScriptContext context, WebScriptEvent gameEvent) { OnEvent(context, gameEvent); return ValueTask.CompletedTask; }

    void OnEvent(WebScriptContext context, WebScriptEvent gameEvent)
    {
    }
}

public readonly record struct WebAudioCommand(
    string Operation,
    string NameOrPath,
    bool Loop = false,
    float Volume = 1.0f,
    float VolumeScale = 1.0f);

public readonly record struct RuntimeMotionCommand(
    string Operation,
    string Path = "",
    bool Restart = false);

/// <summary>Audio commands emitted by a Web script and executed by the browser host.</summary>
public sealed class WebScriptAudio
{
    private readonly List<WebAudioCommand> _commands = [];
    private readonly Dictionary<string, bool> _loops = new(StringComparer.OrdinalIgnoreCase);

    public void Play(string nameOrPath)
        => Enqueue("play", nameOrPath);

    public void Pause(string nameOrPath)
        => Enqueue("pause", nameOrPath);

    public void PlayOneShot(string nameOrPath, float volumeScale = 1.0f)
        => Enqueue("play_one_shot", nameOrPath, volumeScale: volumeScale);

    public void Stop(string nameOrPath)
        => Enqueue("stop", nameOrPath);

    public void SetVolume(string nameOrPath, float volume)
        => Enqueue("set_volume", nameOrPath, volume: volume);

    public void SetLoop(string nameOrPath, bool loop)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath)) return;
        _loops[nameOrPath.Trim()] = loop;
        Enqueue("set_loop", nameOrPath, loop: loop);
    }

    public bool GetLoop(string nameOrPath)
        => !string.IsNullOrWhiteSpace(nameOrPath)
            && _loops.TryGetValue(nameOrPath.Trim(), out bool loop)
            && loop;

    internal IReadOnlyList<WebAudioCommand> Drain()
    {
        if (_commands.Count == 0)
        {
            return [];
        }

        WebAudioCommand[] commands = _commands.ToArray();
        _commands.Clear();
        return commands;
    }

    private void Enqueue(
        string operation,
        string nameOrPath,
        bool loop = false,
        float volume = 1.0f,
        float volumeScale = 1.0f)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath))
        {
            return;
        }

        _commands.Add(new(
            operation,
            nameOrPath.Trim(),
            loop,
            float.IsFinite(volume) ? volume : 1.0f,
            float.IsFinite(volumeScale) ? volumeScale : 1.0f));
    }
}

public sealed record WebScriptEvent(
    string Name,
    string PayloadJson = "",
    string EntityId = "",
    string ControlId = "",
    string ControlName = "",
    string EventName = "",
    string CallbackName = "",
    string SpriteId = "",
    string SpriteName = "",
    string SpriteEventName = "")
{
    public bool IsGuiEvent => string.Equals(Name, "gui", StringComparison.OrdinalIgnoreCase);

    public bool IsSpeechEvent => string.Equals(Name, "speech", StringComparison.OrdinalIgnoreCase);

    public bool IsSpriteEvent => string.Equals(Name, "sprite", StringComparison.OrdinalIgnoreCase);
    public RuntimeAsrScriptEvent? Asr { get; init; }
    public RuntimeLlmScriptEvent? Llm { get; init; }
    public bool IsAsrEvent => Asr is not null;
    public bool IsLlmEvent => Llm is not null;
    public RuntimeRealtimeVoiceScriptEvent? RealtimeVoice { get; init; }
    public bool IsRealtimeVoiceEvent => RealtimeVoice is not null;
}

public sealed record RuntimeSpeechRequest(
    string Text,
    int SpeakerId = 0,
    float Speed = 1.0f,
    float Volume = 1.0f,
    string CallbackName = "",
    Action? Completion = null,
    bool Stop = false);

public sealed class WebScriptContext(RuntimeScene scene, RuntimeEntity? entity)
{
    public RuntimeScene Scene { get; } = scene;
    public Zhengyan.DigitalWife.Scripting.RuntimeGraphicsQuality Graphics => Scene.Graphics;
    public RuntimeEntity? Entity { get; } = entity;
    public double ElapsedSeconds { get; private set; }
    public RuntimeCameraInput Input { get; private set; }
    public WebScriptAudio Audio { get; } = new();
    public WebScriptEvent? Event { get; private set; }

    public void UpdateFrame(double elapsedSeconds, RuntimeCameraInput input)
    {
        ElapsedSeconds = elapsedSeconds;
        Input = input;
        Event = null;
    }

    public void SetEvent(WebScriptEvent? gameEvent)
    {
        Event = gameEvent;
    }

    public IReadOnlyList<WebAudioCommand> DrainAudioCommands() => Audio.Drain();
}

namespace Zhengyan.DigitalWife.GameProjects;

/// <summary>Stable globals contract used by Android C# published assemblies.</summary>
// This contract is also used as Roslyn's globals type by the editor when it
// emits Android script DLLs. It must be concrete so the script submission
// factory can be emitted; the Android runtime still derives its richer
// AndroidScriptGlobals implementation from this base class.
public class AndroidScriptGlobalsContract
{
    public dynamic? Scene { get; protected set; }
    public dynamic? Entity { get; protected set; }
    public dynamic? Input { get; protected set; }
    public dynamic? Audio { get; protected set; }
    public dynamic? Network { get; protected set; }
    public dynamic? Save { get; protected set; }
    public dynamic? Llm { get; protected set; }
    public dynamic? Tts { get; protected set; }
    public dynamic? Asr { get; protected set; }
    public dynamic? Realtime { get; protected set; }
    public dynamic? Event { get; protected set; }
    public dynamic? Services { get; protected set; }
    public float DeltaSeconds { get; protected set; }
    public bool IsStart { get; protected set; }
    public bool IsUpdate { get; protected set; }
    public bool IsEvent => Event is not null;
    public bool IsGuiEvent { get; protected set; }
    public bool IsSpriteEvent { get; protected set; }
    public bool IsSpeechEvent { get; protected set; }
    public string GuiControlId { get; protected set; } = string.Empty;
    public string GuiControlName { get; protected set; } = string.Empty;
    public string GuiEventName { get; protected set; } = string.Empty;
    public string SpeechCallbackName { get; protected set; } = string.Empty;
}

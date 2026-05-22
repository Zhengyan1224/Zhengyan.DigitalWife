namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed class CSharpScriptGlobals
{
    public required RuntimeEntity Entity { get; init; }

    public required RuntimeScene Scene { get; init; }

    public required RuntimeInput Input { get; init; }

    public required RuntimeAudio Audio { get; init; }

    public double DeltaSeconds { get; init; }

    public bool IsStart { get; init; }

    public bool IsUpdate { get; init; }

    public bool IsGuiEvent { get; init; }

    public bool IsLoadingEvent { get; init; }

    public bool IsSpeechEvent { get; init; }

    public string SpeechCallbackName { get; init; } = string.Empty;

    public string LoadingEventName { get; init; } = string.Empty;

    public float LoadingProgress { get; init; }

    public string LoadingMessage { get; init; } = string.Empty;

    public string GuiControlId { get; init; } = string.Empty;

    public string GuiEventName { get; init; } = string.Empty;
}

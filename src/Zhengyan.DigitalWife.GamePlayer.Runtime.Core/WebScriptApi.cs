namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

/// <summary>Stable, platform-neutral ABI implemented by precompiled Web scripts.</summary>
public interface IWebGameScript
{
    void Start(WebScriptContext context);
    void Update(WebScriptContext context, float deltaSeconds);

    void OnEvent(WebScriptContext context, WebScriptEvent gameEvent)
    {
    }
}

public sealed class WebScriptContext(RuntimeScene scene, RuntimeEntity? entity)
{
    public RuntimeScene Scene { get; } = scene;
    public RuntimeEntity? Entity { get; } = entity;
    public double ElapsedSeconds { get; private set; }
    public RuntimeCameraInput Input { get; private set; }

    public void UpdateFrame(double elapsedSeconds, RuntimeCameraInput input)
    {
        ElapsedSeconds = elapsedSeconds;
        Input = input;
    }
}

public sealed record WebScriptEvent(string Name, string PayloadJson = "");

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

internal interface IScriptInstance : IDisposable
{
    void Start(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio);

    void Update(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, double deltaSeconds);

    void GuiEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string controlId, string eventName);

    void LoadingEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string eventName, float progress, string message);

    void SpeechEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string callbackName);

    void LlmEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, RuntimeLlmScriptEvent llmEvent);
}

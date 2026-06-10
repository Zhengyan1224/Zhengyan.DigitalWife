namespace Zhengyan.DigitalWife.Samples.GamePlayer;

internal interface IScriptInstance : IDisposable
{
    void Start(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio);

    void Update(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, double deltaSeconds);

    void GuiEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string controlId, string controlName, string eventName);

    void SpriteEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string spriteId, string spriteName, string eventName);

    void TrayMenuEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string itemId, string itemText, string eventName);

    void LoadingEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string eventName, float progress, string message);

    void SpeechEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string callbackName);

    void LlmEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, RuntimeLlmScriptEvent llmEvent);

    string? InvokeLlmTool(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, RuntimeLlmScriptEvent llmEvent);

    void AsrEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, RuntimeAsrScriptEvent asrEvent);

    void RealtimeVoiceEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, RuntimeRealtimeVoiceScriptEvent realtimeVoiceEvent);
}

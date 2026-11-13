using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed class RuntimeVoice(GameProjectVoiceSettings? settings=null):IDisposable
{
    private readonly GameProjectVoiceSettings _settings=settings??new();
    private bool _disposed;
    public bool IsEnabled=>!_disposed&&_settings.Enabled;
    public Action? PreloadHandler {get;set;}
    public void Preload(){if(IsEnabled)PreloadHandler?.Invoke();}
    public void AttachEntity(RuntimeEntity entity)=>entity.ConfigureVoice(_settings);
    public void Speak(RuntimeEntity entity,string text,int? speakerId=null,float? speed=null,float? volume=null)
        =>Speak(entity,text,new RuntimeVoiceOptions{SpeakerId=speakerId,Speed=speed,Volume=volume});
    public void Speak(RuntimeEntity entity,string text,RuntimeVoiceOptions? options=null)
    {if(!IsEnabled)return;entity.ConfigureVoice(_settings);entity.Speak(text,options??new());}
    public void Stop(RuntimeEntity entity)=>entity.StopSpeaking();
    public void Dispose()=>_disposed=true;
}

using System.Runtime.CompilerServices;
using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

public sealed partial class AndroidScriptEntity
{
    private static readonly ConditionalWeakTable<RuntimeEntity,AndroidScriptEntity> ScriptEntityWrappers=new();
    public static implicit operator AndroidScriptEntity?(RuntimeEntity? entity)
    {
        if(entity is null)return null;
        lock(ScriptEntityWrappers)
        {
            if(ScriptEntityWrappers.TryGetValue(entity,out var existing))return existing;
            var model=(entity.PmxScriptApi as AndroidPmxScriptApi)?.Model;
            return new(entity,entity.ApplyMotion,(speed,playing)=>{if(speed is {} s)entity.PlaybackSpeed=s;if(playing is {} p)entity.IsPlaying=p;},()=>model,
                entity.ScriptAssetPathResolver,speakWithCallback:entity.SpeakWithCallback,stopSpeaking:entity.StopSpeaking,
                speak:(text,speaker,speed,volume,completed,callback)=>
                {if(entity.SpeechRequestHandler is {} handler)handler(new(text,speaker,speed,volume,callback,completed));else if(completed is not null)entity.Speak(text,speaker,speed,volume,completed);else entity.SpeakWithCallback(text,speaker,speed,volume,callback);});
        }
    }
}

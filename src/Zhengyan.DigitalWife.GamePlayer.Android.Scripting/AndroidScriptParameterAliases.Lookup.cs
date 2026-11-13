namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal static partial class AndroidScriptParameterAliases
{
    internal static string? Resolve(string type,string method,string parameter)
    {
        if(Names.TryGetValue((type,method,parameter),out var name))return name;
        if(type is "AndroidScriptAsr" or "IAndroidScriptAsr" && parameter=="entity" && method.StartsWith("Start"))return "callbackTarget";
        if(type=="IAndroidScriptRealtimeVoice"&&parameter=="entity"&&method.StartsWith("Start"))return "callbackTarget";
        if(type!="AndroidScriptEntity")return null;
        if(method=="SetMotionLayerResetPhysicsOnLoop"&&parameter=="reset")return "resetPhysicsOnLoop";
        if(method.EndsWith("Euler")&&parameter is "x" or "y" or "z")return parameter+"Degrees";
        if(parameter!="value")return null;
        if(method.Contains("NodeAnimTranslate")||method.Contains("NodeTranslate"))return "translate";
        if(method.Contains("NodeAnimRotate")||method.Contains("NodeRotate"))return "rotate";
        return method.Contains("NodeScale")?"scale":null;
    }
}

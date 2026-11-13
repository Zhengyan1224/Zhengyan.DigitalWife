using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using Native = Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Runtime = Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

/// <summary>Provides the portable entity API for any model returned by Scene.GetEntity.</summary>
public sealed class AndroidPmxScriptApi(Func<object?> resolve,RuntimeEntity entity) : IRuntimePmxScriptApi
{
    internal Native.PmxModelComponent? Model=>resolve() as Native.PmxModelComponent;
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties,typeof(Native.PmxModelComponent))]
    public object? Get(string property)
    {
        var target=resolve();
        if(target is Native.PmxModelComponent model && property=="MotionLayers")
            return model.GetMotionLayers().Select(ConvertLayer).ToArray();
        return target?.GetType().GetProperty(property)?.GetValue(target);
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties,typeof(Native.PmxModelComponent))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods,typeof(Zhengyan.DigitalWife.Mmd.Game.Components.TexturedPlaneComponent))]
    public object? Invoke(string method,object?[] arguments)
    {
        var target=resolve();if(target is null)return null;
        if(target is Native.PmxModelComponent resetModel&&method=="SetResetPhysicsOnMotionLoop")
        {resetModel.ResetPhysicsOnMotionLoop=(bool)arguments[0]!;return null;}
        if(target is Native.PmxModelComponent pmx && method=="PlayMotion")
        {
            if(arguments.FirstOrDefault() is true)pmx.ResetAnimation();
            pmx.PlayMotion();entity.Definition.IsPlaying=true;return null;
        }
        foreach(var candidate in target.GetType().GetMethods().Where(m=>m.Name==method&&!m.IsGenericMethod))
        {
            var parameters=candidate.GetParameters();
            if(parameters.Length<arguments.Length || parameters.Skip(arguments.Length).Any(p=>!p.IsOptional))continue;
            object?[] native=new object?[parameters.Length];bool matches=true;
            for(int i=0;i<parameters.Length;i++)
            {
                if(i>=arguments.Length){native[i]=parameters[i].DefaultValue;continue;}
                if(parameters[i].IsOut){native[i]=null;continue;}
                Type type=parameters[i].ParameterType;
                if(type.IsByRef)type=type.GetElementType()!;
                object? value=arguments[i];
                if(value is IEnumerable<Runtime.MotionLayerDefinition> layers)
                    value=layers.Select(l=>new Native.MotionLayerDefinition(entity.ScriptAssetPathResolver(l.MotionPath),l.Weight,l.ResetPhysicsOnLoop)).ToArray();
                if(value is not null && !type.IsInstanceOfType(value) && !(Nullable.GetUnderlyingType(type)?.IsInstanceOfType(value)??false)){matches=false;break;}
                native[i]=value;
            }
            if(!matches)continue;
            try
            {
                var result=candidate.Invoke(target,native);
                for(int i=0;i<arguments.Length;i++)if(parameters[i].IsOut)arguments[i]=ConvertResult(native[i]);
                if(target is Native.PmxModelComponent model)
                {
                    entity.Definition.IsPlaying=model.IsPlaying;
                    if(method is "SetMotionLayers" or "AddMotionLayer" or "RemoveMotionLayer" or "ClearMotion" or "ApplyMotion")
                        entity.Definition.MotionLayers=model.GetMotionLayers().Select(l=>new MotionLayerSettings{Path=l.MotionPath,Weight=l.Weight,ResetPhysicsOnLoop=l.ResetPhysicsOnLoop}).ToList();
                }
                return candidate.ReturnType==typeof(void) && method=="SetCustomShader" ? true : ConvertResult(result);
            }
            catch(TargetInvocationException error) when(error.InnerException is not null)
            {ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        }
        return null;
    }

    private static object? ConvertResult(object? result)=>result is Native.PmxNodeState node
        ?new Runtime.PmxNodeState(node.Name,node.Translate,node.Rotate,node.Scale,node.AnimTranslate,node.AnimRotate,node.BaseAnimTranslate,node.BaseAnimRotate):result;
    private static Runtime.MotionLayerInfo ConvertLayer(Native.MotionLayerInfo l)=>new(l.MotionPath,l.Weight,l.TimeSeconds,l.DurationFrames,l.ResetPhysicsOnLoop,l.IsPlaying);
}

using System.Numerics;
using Zhengyan.DigitalWife.Mmd;
using Zhengyan.DigitalWife.GameProjects;
using ScriptRuntime = Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed partial class WebPmxAsset
{
    public Func<string,string?> ScriptTextureResolver { get; set; } = path=>path.StartsWith("rt:")?path:null;
    public Action<string>? ScriptWarning { get; set; }
    public Func<string,string?> ScriptShaderResolver {get;set;} = _=>null;
    public (string Id,string Vertex,string Fragment)? CustomShaderSource {get;private set;}
    private readonly Dictionary<string,WebCustomUniform> _customUniforms=new(StringComparer.Ordinal);
    public WebCustomShader? CustomShader=>CustomShaderSource is {} source?new(source.Id,new Dictionary<string,WebCustomUniform>(_customUniforms)):null;
    public bool ScriptMeshReloadRequested {get;set;}
    private WebPmxMaterial[]? _scriptOriginalMaterials;
    public object? Get(string property)=>property switch
    {
        "MotionLayerCount"=>Pose.MotionLayerCount,
        "AnimationTimeSeconds"=>Pose.MotionLayerCount>0?Pose.GetMotionLayerFrame(0)/30:0,
        "NodeNames"=>Pose.BoneNames,
        "MorphNames"=>_parsing.Morphs.Select(m=>m.Name).ToArray(),
        "MaterialNames"=>Mesh.Materials.Select(m=>m.Name).ToArray(),
        "MorphWeights"=>CurrentScriptMorphWeights(),
        "MorphSaveAnimWeights"=>Pose.ScriptSavedMorphs,
        "MotionLayers"=>Enumerable.Range(0,Pose.MotionLayerCount).Select(i=>new ScriptRuntime.MotionLayerInfo(_entity.MotionLayers.ElementAtOrDefault(i)?.Path??"",
            _entity.MotionLayers.ElementAtOrDefault(i)?.Weight??1,Pose.GetMotionLayerFrame(i)/30,Pose.GetMotionLayerDuration(i),Pose.GetMotionLayerResetPhysicsOnLoop(i),Pose.GetMotionLayerPlaying(i))).ToArray(),
        _=>null
    };
    private IReadOnlyDictionary<string,float> CurrentScriptMorphWeights()
    {var values=Pose.MorphWeights.ToDictionary(p=>p.Key,p=>p.Value);foreach(string name in values.Keys.ToArray())if(Pose.TryGetManualMorphWeight(name,out var v))values[name]=v;return values;}
    private int ScriptLayer(string path)=>_entity.MotionLayers.FindIndex(l=>NormalizeMotionPath(l.Path).Equals(NormalizeMotionPath(path),StringComparison.OrdinalIgnoreCase));
    private void RebuildScriptLayers(PmxPoseEvaluator previous,IReadOnlyList<ScriptRuntime.MotionLayerInfo> layers)
    {
        SetMotionLayers();
        foreach(var layer in layers){int i=ScriptLayer(layer.MotionPath);if(i>=0&&i<Pose.MotionLayerCount){Pose.SetMotionLayerFrame(i,layer.TimeSeconds*30);Pose.SetMotionLayerPlaying(i,layer.IsPlaying);}}
        Pose.CopyPlaybackClockFrom(previous);_wasPlaying=_entity.IsPlaying;_skipPhysicsOnNextPlayFrame=false;
    }
    private bool UpdateScriptPose(Func<PmxPoseEvaluator,bool> apply)
    {bool result=apply(Pose);if(result){foreach(var p in _ownedPoses.Where(p=>!ReferenceEquals(p,Pose)))p.CopyScriptStateFrom(Pose);_poseDirty=true;}return result;}
    public object? Invoke(string method,object?[] args)
    {
        string Text(int i)=>(string)args[i]!;float Number(int i)=>Convert.ToSingle(args[i]);bool Flag(int i)=>(bool)args[i]!;
        switch(method)
        {
            case "SetResetPhysicsOnMotionLoop":foreach(int i in Enumerable.Range(0,Pose.MotionLayerCount))Pose.SetMotionLayerResetPhysicsOnLoop(i,Flag(0));return null;
            case "ApplyMotion": return ApplyMotion(Text(0));
            case "PlayMotion": PlayMotion(args.Length>0&&Flag(0));return null;
            case "PauseMotion": PauseMotion();return null;
            case "StopMotion": StopMotion();return null;
            case "ResetAnimation": foreach(var i in Enumerable.Range(0,Pose.MotionLayerCount))Pose.SetMotionLayerFrame(i,0);_poseDirty=true;return null;
            case "SetMotionLayers":
                _entity.MotionLayers=((IEnumerable<ScriptRuntime.MotionLayerDefinition>)args[0]!).Select(l=>new MotionLayerSettings{Path=NormalizeMotionPath(l.MotionPath),Weight=l.Weight,ResetPhysicsOnLoop=l.ResetPhysicsOnLoop??_entity.ResetPhysicsOnMotionLoop}).ToList();
                SetMotionLayers();return null;
            case "AddMotionLayer":
                var previous=Pose;var oldLayers=(IReadOnlyList<ScriptRuntime.MotionLayerInfo>)Get("MotionLayers")!;
                int existing=ScriptLayer(Text(0));if(existing>=0)_entity.MotionLayers[existing].Weight=Number(1);
                else _entity.MotionLayers.Add(new(){Path=NormalizeMotionPath(Text(0)),Weight=Number(1),ResetPhysicsOnLoop=_entity.ResetPhysicsOnMotionLoop});
                RebuildScriptLayers(previous,oldLayers);return null;
            case "RemoveMotionLayer": int removed=ScriptLayer(Text(0));if(removed>=0){var before=Pose;var saved=(IReadOnlyList<ScriptRuntime.MotionLayerInfo>)Get("MotionLayers")!;_entity.MotionLayers.RemoveAt(removed);RebuildScriptLayers(before,saved);}return null;
            case "ClearMotion": _entity.MotionLayers.Clear();SetMotionLayers();return null;
            case "SeekMotionTime": case "SeekMotionFrame":
                foreach(int i in Enumerable.Range(0,Pose.MotionLayerCount))Pose.SetMotionLayerFrame(i,Number(0)*(method=="SeekMotionTime"?30:1));_poseDirty=true;return null;
            case "TrySetMotionLayerPlaying": case "TrySetMotionLayerTime": case "TrySetMotionLayerFrame": case "SetMotionLayerWeight": case "SetMotionLayerResetPhysicsOnLoop":
                int layer=ScriptLayer(Text(0));if(layer<0||layer>=Pose.MotionLayerCount)return false;
                if(method=="TrySetMotionLayerPlaying")Pose.SetMotionLayerPlaying(layer,Flag(1));
                else if(method=="SetMotionLayerResetPhysicsOnLoop")Pose.SetMotionLayerResetPhysicsOnLoop(layer,Flag(1));
                else if(method=="SetMotionLayerWeight")Pose.SetMotionLayerWeight(layer,Number(1));
                else Pose.SetMotionLayerFrame(layer,Number(1)*(method=="TrySetMotionLayerTime"?30:1));
                _poseDirty=true;return true;
            case "TrySetMorphWeight": return UpdateScriptPose(p=>p.SetScriptMorph(Text(0),Number(1),Flag(2)));
            case "ClearMorphWeightOverride": return UpdateScriptPose(p=>{bool found=p.TryGetManualMorphWeight(Text(0),out _);p.ClearMorphWeight(Text(0));return found;});
            case "ClearMorphWeightOverrides": return UpdateScriptPose(p=>{p.ClearScriptMorphOverrides();return true;});
            case "TrySetMorphSaveAnimWeight": return UpdateScriptPose(p=>p.SetScriptSavedMorph(Text(0),Number(1)));
            case "SaveMorphAnimWeight": return UpdateScriptPose(p=>p.SaveScriptMorph(Text(0)));
            case "LoadMorphAnimWeight": return UpdateScriptPose(p=>p.LoadScriptMorph(Text(0)));
            case "ClearMorphAnimWeight": return UpdateScriptPose(p=>p.ClearScriptSavedMorph(Text(0)));
            case "SaveBaseAnimation": return UpdateScriptPose(p=>{p.SaveScriptBaseAnimation();return true;});
            case "LoadBaseAnimation": return UpdateScriptPose(p=>{p.LoadScriptBaseAnimation();return true;});
            case "ClearBaseAnimation": return UpdateScriptPose(p=>{p.ClearScriptBaseAnimation();return true;});
            case "TryGetNodeState":
                bool found=Pose.TryGetScriptNode(Text(0),out var state);
                args[1]=new ScriptRuntime.PmxNodeState(state.Name,state.Translate,state.Rotate,state.Scale,state.AnimTranslate,state.AnimRotate,state.BaseAnimTranslate,state.BaseAnimRotate);return found;
            case "TrySetNodeTranslate": case "TrySetNodeAnimTranslate": return UpdateScriptPose(p=>p.SetScriptNodeTranslate(Text(0),(Vector3)args[1]!,method=="TrySetNodeAnimTranslate",Flag(2)));
            case "TrySetNodeRotate": case "TrySetNodeAnimRotate": return UpdateScriptPose(p=>p.SetScriptNodeRotate(Text(0),(Quaternion)args[1]!,method=="TrySetNodeAnimRotate",Flag(2)));
            case "TrySetNodeScale": return UpdateScriptPose(p=>p.SetScriptNodeScale(Text(0),(Vector3)args[1]!,Flag(2)));
            case "SaveNodeBaseAnimation": return UpdateScriptPose(p=>p.SaveScriptNode(Text(0)));
            case "LoadNodeBaseAnimation": return UpdateScriptPose(p=>p.LoadScriptNode(Text(0)));
            case "ClearNodeBaseAnimation": return UpdateScriptPose(p=>p.ClearScriptNodeBase(Text(0)));
            case "ClearNodeOverrides": return UpdateScriptPose(p=>p.ClearScriptNodeOverrides(Text(0)));
            case "ClearAllNodeOverrides": return UpdateScriptPose(p=>{p.ClearAllScriptNodeOverrides();return true;});
            case "SetMaterialTexture":
                int material=args[0] is int index?index:Mesh.Materials.ToList().FindIndex(m=>m.Name==Text(0));
                string? uri=ScriptTextureResolver(Text(1));if(material<0||material>=Mesh.Materials.Count||uri is null)return false;
                _scriptOriginalMaterials??=Mesh.Materials.ToArray();var changed=Mesh.Materials.ToArray();changed[material]=changed[material] with{TextureUri=uri};Mesh.Materials=changed;return true;
            case "ClearMaterialTextureOverride":
                if(_scriptOriginalMaterials is not null&&args[0] is int mi&&mi>=0&&mi<Mesh.Materials.Count){var reset=Mesh.Materials.ToArray();reset[mi]=reset[mi] with{TextureUri=_scriptOriginalMaterials[mi].TextureUri};Mesh.Materials=reset;}return null;
            case "ClearMaterialTextureOverrides": if(_scriptOriginalMaterials is not null)Mesh.Materials=_scriptOriginalMaterials.ToArray();return null;
            case "SetCustomShader":
                string? vertex=ScriptShaderResolver(Text(0)),fragment=ScriptShaderResolver(Text(1));
                if(vertex is null||fragment is null){ScriptWarning?.Invoke("Custom shader source files are not registered in this Web game.");return false;}
                vertex=BrowserShaderSource(vertex);fragment=BrowserShaderSource(fragment);
                string id=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(vertex+"\n"+fragment)));
                CustomShaderSource=(id,vertex,fragment);
                if(_gpuSkinning){_gpuSkinning=false;_skinnedVertices=new float[_parsing.Vertices.Length*8];Mesh.Skinning=null;Pose.WriteSkinnedVertices(_skinnedVertices);Mesh.UpdateFromInterleaved(_skinnedVertices);ScriptMeshReloadRequested=true;}
                _poseDirty=true;return true;
            case "ClearCustomShader": CustomShaderSource=null;return null;
            case "ClearCustomShaderUniform": _customUniforms.Remove(Text(0));return null;
            case "ClearCustomShaderUniforms": _customUniforms.Clear();return null;
            default:
                if(method.StartsWith("SetCustomShader",StringComparison.Ordinal)){_customUniforms[Text(0)]=new(method=="SetCustomShaderInt",args.Skip(1).Select(Convert.ToSingle).ToArray());return null;}
                throw new NotSupportedException("Web PMX script operation: "+method);
        }
    }
    internal static string BrowserShaderSource(string source)
    {
        source=System.Text.RegularExpressions.Regex.Replace(source,@"#version\s+330\s+core","#version 300 es");
        if(source.Contains("#version 300 es")&&!source.Contains("precision "))source=source.Replace("#version 300 es","#version 300 es\nprecision highp float;\nprecision highp int;");
        return source;
    }
}
public sealed record WebCustomUniform(bool Integer,float[] Values);
public sealed record WebCustomShader(string Id,IReadOnlyDictionary<string,WebCustomUniform> Uniforms);

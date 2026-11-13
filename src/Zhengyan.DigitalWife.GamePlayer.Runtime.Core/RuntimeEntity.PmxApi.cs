using System.Numerics;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public readonly record struct MotionLayerInfo(string MotionPath,float Weight,float TimeSeconds,int DurationFrames,bool ResetPhysicsOnLoop=true,bool IsPlaying=true);
public readonly record struct PmxNodeState(string Name,Vector3 Translate,Quaternion Rotate,Vector3 Scale,Vector3 AnimTranslate,Quaternion AnimRotate,Vector3 BaseAnimTranslate,Quaternion BaseAnimRotate);
public interface IRuntimePmxScriptApi
{
    object? Get(string property);
    object? Invoke(string method,object?[] arguments);
}

public sealed partial class RuntimeEntity
{
    public IRuntimePmxScriptApi? PmxScriptApi { get; set; }
    public Func<string,string> ScriptAssetPathResolver { get; set; } = path=>path;
    private T? PmxValue<T>(string property) => PmxScriptApi?.Get(property) is T value ? value : default;
    private T? PmxCall<T>(string method,params object?[] arguments) => PmxScriptApi?.Invoke(method,arguments) is T value ? value : default;
    private void PmxAction(string method,params object?[] arguments) => PmxScriptApi?.Invoke(method,arguments);
    public float AnimationTimeSeconds {get=>PmxValue<float>(nameof(AnimationTimeSeconds));set=>SeekMotionTime(value);}
    public int MotionLayerCount=>PmxValue<int>(nameof(MotionLayerCount));
    public IReadOnlyList<string> MaterialNames=>PmxValue<IReadOnlyList<string>>(nameof(MaterialNames))??[];
    public IReadOnlyList<string> MorphNames=>PmxValue<IReadOnlyList<string>>(nameof(MorphNames))??[];
    public IReadOnlyList<string> NodeNames=>PmxValue<IReadOnlyList<string>>(nameof(NodeNames))??[];
    public IReadOnlyDictionary<string,float> MorphWeights=>PmxValue<IReadOnlyDictionary<string,float>>(nameof(MorphWeights))??new Dictionary<string,float>();
    public IReadOnlyDictionary<string,float> MorphSaveAnimWeights=>PmxValue<IReadOnlyDictionary<string,float>>(nameof(MorphSaveAnimWeights))??new Dictionary<string,float>();
    public void AddMotionLayer(string motionPath,float weight=1)=>PmxAction(nameof(AddMotionLayer),ScriptAssetPathResolver(motionPath),weight);
    public void RemoveMotionLayer(string motionPath)=>PmxAction(nameof(RemoveMotionLayer),ScriptAssetPathResolver(motionPath));
    public void ClearMotion()=>PmxAction(nameof(ClearMotion));
    public IReadOnlyList<MotionLayerInfo> GetMotionLayers()=>PmxValue<IReadOnlyList<MotionLayerInfo>>("MotionLayers")??[];
    public MotionLayerInfo? GetMotionLayer(string motionPath)=>GetMotionLayers().Where(l=>string.Equals(ScriptAssetPathResolver(l.MotionPath),ScriptAssetPathResolver(motionPath),StringComparison.OrdinalIgnoreCase)).Cast<MotionLayerInfo?>().FirstOrDefault();
    public void ResetMotion()=>PmxAction("ResetAnimation");
    public void SeekMotionTime(float timeSeconds)=>PmxAction(nameof(SeekMotionTime),Math.Max(0,timeSeconds));
    public void SeekMotionFrame(float frame)=>PmxAction(nameof(SeekMotionFrame),Math.Max(0,frame));
    public bool TrySetMotionLayerPlaying(string motionPath,bool isPlaying)=>PmxCall<bool>(nameof(TrySetMotionLayerPlaying),ScriptAssetPathResolver(motionPath),isPlaying);
    public void SetMotionLayerPlaying(string motionPath,bool isPlaying)=>TrySetMotionLayerPlaying(motionPath,isPlaying);
    public bool TrySetMotionLayerTime(string motionPath,float timeSeconds)=>PmxCall<bool>(nameof(TrySetMotionLayerTime),ScriptAssetPathResolver(motionPath),Math.Max(0,timeSeconds));
    public void SetMotionLayerTime(string motionPath,float timeSeconds)=>TrySetMotionLayerTime(motionPath,timeSeconds);
    public bool TrySetMotionLayerFrame(string motionPath,float frame)=>PmxCall<bool>(nameof(TrySetMotionLayerFrame),ScriptAssetPathResolver(motionPath),Math.Max(0,frame));
    public void SetMotionLayerFrame(string motionPath,float frame)=>TrySetMotionLayerFrame(motionPath,frame);
    public bool PauseMotionLayer(string motionPath)=>TrySetMotionLayerPlaying(motionPath,false);
    public bool PlayMotionLayer(string motionPath)=>TrySetMotionLayerPlaying(motionPath,true);
    public bool TryGetMorphWeight(string morphName,out float weight){weight=GetMorphWeight(morphName);return MorphNames.Contains(morphName);}
    public float GetMorphWeight(string morphName,bool desktopParameterNames=true)=>GetMorphWeight(morphName);
    public bool TrySetMorphWeight(string morphName,float weight,bool overrideAnimation=true)=>PmxCall<bool>(nameof(TrySetMorphWeight),morphName,weight,overrideAnimation);
    public void SetMorphWeight(string morphName,float weight,bool overrideAnimation=true)=>TrySetMorphWeight(morphName,weight,overrideAnimation);
    public void SetMotionLayers(IEnumerable<MotionLayerDefinition> motionLayers,bool desktopParameterNames=true)=>SetMotionLayers(motionLayers);
    public void SetMotionLayerResetPhysicsOnLoop(string motionPath,bool resetPhysicsOnLoop,bool desktopParameterNames=true)=>SetMotionLayerResetPhysicsOnLoop(motionPath,resetPhysicsOnLoop);
    public bool ClearMorphWeightOverride(string morphName)=>PmxCall<bool>(nameof(ClearMorphWeightOverride),morphName);
    public void ClearMorphWeightOverrides()=>PmxAction(nameof(ClearMorphWeightOverrides));
    public bool TryGetMorphSaveAnimWeight(string morphName,out float weight){weight=GetMorphSaveAnimWeight(morphName);return MorphNames.Contains(morphName);}
    public float GetMorphSaveAnimWeight(string morphName)=>MorphSaveAnimWeights.GetValueOrDefault(morphName);
    public bool TrySetMorphSaveAnimWeight(string morphName,float weight)=>PmxCall<bool>(nameof(TrySetMorphSaveAnimWeight),morphName,weight);
    public void SetMorphSaveAnimWeight(string morphName,float weight)=>TrySetMorphSaveAnimWeight(morphName,weight);
    public bool SaveMorphAnimWeight(string morphName)=>PmxCall<bool>(nameof(SaveMorphAnimWeight),morphName);
    public bool SaveAnimWeight(string morphName)=>SaveMorphAnimWeight(morphName);
    public bool LoadMorphAnimWeight(string morphName)=>PmxCall<bool>(nameof(LoadMorphAnimWeight),morphName);
    public bool ClearMorphAnimWeight(string morphName)=>PmxCall<bool>(nameof(ClearMorphAnimWeight),morphName);
    public void SaveBaseAnimation()=>PmxAction(nameof(SaveBaseAnimation));
    public void LoadBaseAnimation()=>PmxAction(nameof(LoadBaseAnimation));
    public void ClearBaseAnimation()=>PmxAction(nameof(ClearBaseAnimation));
    public bool TryGetNodeState(string nodeName,out PmxNodeState state){object?[] args=[nodeName,null];bool found=PmxCall<bool>(nameof(TryGetNodeState),args);state=args[1] is PmxNodeState s?s:default;return found;}
    public PmxNodeState GetNodeState(string nodeName)=>TryGetNodeState(nodeName,out var state)?state:throw new ArgumentException("Unknown PMX node: "+nodeName,nameof(nodeName));
    public bool TrySetNodeTranslate(string nodeName,Vector3 translate,bool overrideAnimation=true)=>PmxCall<bool>(nameof(TrySetNodeTranslate),nodeName,translate,overrideAnimation);
    public void SetNodeTranslate(string nodeName,Vector3 translate,bool overrideAnimation=true){if(!TrySetNodeTranslate(nodeName,translate,overrideAnimation))throw new ArgumentException("Unknown PMX node: "+nodeName);}
    public void SetNodeTranslate(string nodeName,float x,float y,float z,bool overrideAnimation=true)=>SetNodeTranslate(nodeName,new Vector3(x,y,z),overrideAnimation);
    public bool TrySetNodeRotate(string nodeName,Quaternion rotate,bool overrideAnimation=true)=>PmxCall<bool>(nameof(TrySetNodeRotate),nodeName,rotate,overrideAnimation);
    public void SetNodeRotate(string nodeName,Quaternion rotate,bool overrideAnimation=true){if(!TrySetNodeRotate(nodeName,rotate,overrideAnimation))throw new ArgumentException("Unknown PMX node: "+nodeName);}
    public void SetNodeRotateEuler(string nodeName,float xDegrees,float yDegrees,float zDegrees,bool overrideAnimation=true)=>SetNodeRotate(nodeName,ToQuaternion(new(xDegrees,yDegrees,zDegrees)),overrideAnimation);
    public bool TrySetNodeScale(string nodeName,Vector3 scale,bool overrideAnimation=true)=>PmxCall<bool>(nameof(TrySetNodeScale),nodeName,scale,overrideAnimation);
    public void SetNodeScale(string nodeName,Vector3 scale,bool overrideAnimation=true){if(!TrySetNodeScale(nodeName,scale,overrideAnimation))throw new ArgumentException("Unknown PMX node: "+nodeName);}
    public void SetNodeScale(string nodeName,float x,float y,float z,bool overrideAnimation=true)=>SetNodeScale(nodeName,new Vector3(x,y,z),overrideAnimation);
    public bool TrySetNodeAnimTranslate(string nodeName,Vector3 translate,bool overrideAnimation=true)=>PmxCall<bool>(nameof(TrySetNodeAnimTranslate),nodeName,translate,overrideAnimation);
    public void SetNodeAnimTranslate(string nodeName,Vector3 translate,bool overrideAnimation=true){if(!TrySetNodeAnimTranslate(nodeName,translate,overrideAnimation))throw new ArgumentException("Unknown PMX node: "+nodeName);}
    public void SetNodeAnimTranslate(string nodeName,float x,float y,float z,bool overrideAnimation=true)=>SetNodeAnimTranslate(nodeName,new Vector3(x,y,z),overrideAnimation);
    public bool TrySetNodeAnimRotate(string nodeName,Quaternion rotate,bool overrideAnimation=true)=>PmxCall<bool>(nameof(TrySetNodeAnimRotate),nodeName,rotate,overrideAnimation);
    public void SetNodeAnimRotate(string nodeName,Quaternion rotate,bool overrideAnimation=true){if(!TrySetNodeAnimRotate(nodeName,rotate,overrideAnimation))throw new ArgumentException("Unknown PMX node: "+nodeName);}
    public void SetNodeAnimRotateEuler(string nodeName,float xDegrees,float yDegrees,float zDegrees,bool overrideAnimation=true)=>SetNodeAnimRotate(nodeName,ToQuaternion(new(xDegrees,yDegrees,zDegrees)),overrideAnimation);
    public bool SaveNodeBaseAnimation(string nodeName)=>PmxCall<bool>(nameof(SaveNodeBaseAnimation),nodeName);
    public bool LoadNodeBaseAnimation(string nodeName)=>PmxCall<bool>(nameof(LoadNodeBaseAnimation),nodeName);
    public bool ClearNodeBaseAnimation(string nodeName)=>PmxCall<bool>(nameof(ClearNodeBaseAnimation),nodeName);
    public bool ClearNodeOverrides(string nodeName)=>PmxCall<bool>(nameof(ClearNodeOverrides),nodeName);
    public void ClearAllNodeOverrides()=>PmxAction(nameof(ClearAllNodeOverrides));
    public bool SetMaterialTexture(int materialIndex,string textureReference)=>PmxCall<bool>(nameof(SetMaterialTexture),materialIndex,textureReference.StartsWith("rt:")?textureReference:ScriptAssetPathResolver(textureReference));
    public bool SetMaterialTexture(string materialName,string textureReference)=>PmxCall<bool>(nameof(SetMaterialTexture),materialName,textureReference.StartsWith("rt:")?textureReference:ScriptAssetPathResolver(textureReference));
    private static string RenderTextureRef(string name){name=(name??"").Trim();return name.StartsWith("rt:",StringComparison.OrdinalIgnoreCase)?name:"rt:"+name;}
    public bool SetMaterialRenderTexture(int materialIndex,string renderTextureName)=>SetMaterialTexture(materialIndex,RenderTextureRef(renderTextureName));
    public bool SetMaterialRenderTexture(string materialName,string renderTextureName)=>SetMaterialTexture(materialName,RenderTextureRef(renderTextureName));
    public void ClearMaterialTextureOverride(int materialIndex)=>PmxAction(nameof(ClearMaterialTextureOverride),materialIndex);
    public void ClearMaterialTextureOverrides()=>PmxAction(nameof(ClearMaterialTextureOverrides));
    public bool SetCustomShader(string vertexShaderPath,string fragmentShaderPath)=>PmxCall<bool>(nameof(SetCustomShader),ScriptAssetPathResolver(vertexShaderPath),ScriptAssetPathResolver(fragmentShaderPath));
    public bool SetCustomShader(string openGlVertexShaderPath,string openGlFragmentShaderPath,string vulkanVertexSpirvPath,string vulkanFragmentSpirvPath)=>PmxCall<bool>(nameof(SetCustomShader),ScriptAssetPathResolver(openGlVertexShaderPath),ScriptAssetPathResolver(openGlFragmentShaderPath),ScriptAssetPathResolver(vulkanVertexSpirvPath),ScriptAssetPathResolver(vulkanFragmentSpirvPath));
    public void ClearCustomShader()=>PmxAction(nameof(ClearCustomShader));
    public void SetCustomShaderFloat(string name,float value)=>PmxAction(nameof(SetCustomShaderFloat),name,value);
    public void SetCustomShaderInt(string name,int value)=>PmxAction(nameof(SetCustomShaderInt),name,value);
    public void SetCustomShaderVector2(string name,float x,float y)=>PmxAction(nameof(SetCustomShaderVector2),name,x,y);
    public void SetCustomShaderVector3(string name,float x,float y,float z)=>PmxAction(nameof(SetCustomShaderVector3),name,x,y,z);
    public void SetCustomShaderVector4(string name,float x,float y,float z,float w)=>PmxAction(nameof(SetCustomShaderVector4),name,x,y,z,w);
    public void SetCustomShaderColor(string name,float r,float g,float b,float a=1)=>SetCustomShaderVector4(name,r,g,b,a);
    public void ClearCustomShaderUniform(string name)=>PmxAction(nameof(ClearCustomShaderUniform),name);
    public void ClearCustomShaderUniforms()=>PmxAction(nameof(ClearCustomShaderUniforms));
}

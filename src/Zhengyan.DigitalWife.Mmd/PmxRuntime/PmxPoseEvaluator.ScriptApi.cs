using System.Numerics;

namespace Zhengyan.DigitalWife.Mmd;

public readonly record struct PmxScriptNodeState(string Name,Vector3 Translate,Quaternion Rotate,Vector3 Scale,Vector3 AnimTranslate,Quaternion AnimRotate,Vector3 BaseAnimTranslate,Quaternion BaseAnimRotate);

public sealed partial class PmxPoseEvaluator
{
    private Dictionary<int,ScriptNode>? _scriptNodes;
    private Dictionary<string,float>? _scriptBaseMorphs,_scriptSavedMorphs;
    public IReadOnlyDictionary<string,float> ScriptSavedMorphs=>_scriptSavedMorphs??new Dictionary<string,float>();
    public int GetMotionLayerDuration(int index)=>(int)_layerDurations[ValidateLayerIndex(index)];
    public bool GetMotionLayerPlaying(int index)=>_layerPlaying[ValidateLayerIndex(index)];
    public bool GetMotionLayerResetPhysicsOnLoop(int index)=>_layerResetPhysicsOnLoop?[ValidateLayerIndex(index)]??_resetPhysicsOnLoop;
    public bool HasMorph(string name)=>_morphs.Any(m=>m.Source.Name==name);
    public void ClearScriptMorphOverrides()=>_manualMorphs.Clear();
    public bool SetScriptMorph(string name,float weight,bool overrideAnimation)
    {
        if(!HasMorph(name))return false;
        if(overrideAnimation)SetMorphWeight(name,weight);
        else {ClearMorphWeight(name);(_scriptBaseMorphs??=new(StringComparer.Ordinal))[name]=Math.Clamp(weight,0,1);}
        return true;
    }
    public bool SetScriptSavedMorph(string name,float weight)
    {if(!HasMorph(name))return false;(_scriptSavedMorphs??=new(StringComparer.Ordinal))[name]=Math.Clamp(weight,0,1);return true;}
    public bool SaveScriptMorph(string name)=>SetScriptSavedMorph(name,TryGetManualMorphWeight(name,out var manual)?manual:GetMorphWeight(name));
    public bool LoadScriptMorph(string name)
    {if(_scriptSavedMorphs?.TryGetValue(name,out var weight)!=true)return false;(_scriptBaseMorphs??=new(StringComparer.Ordinal))[name]=weight;return true;}
    public bool ClearScriptSavedMorph(string name)=>_scriptSavedMorphs?.Remove(name)==true;
    public void SaveScriptBaseAnimation()
    {foreach(string name in BoneNames)SaveScriptNode(name);foreach(var morph in _morphs)SaveScriptMorph(morph.Source.Name);}
    public void LoadScriptBaseAnimation()
    {foreach(string name in BoneNames)LoadScriptNode(name);foreach(var morph in _morphs)LoadScriptMorph(morph.Source.Name);}
    public void ClearScriptBaseAnimation()
    {if(_scriptNodes is not null)foreach(var node in _scriptNodes.Values){node.SavedTranslate=Vector3.Zero;node.SavedRotate=Quaternion.Identity;node.HasSaved=false;node.UseSaved=false;}_scriptSavedMorphs?.Clear();_scriptBaseMorphs?.Clear();}
    private ScriptNode? ScriptNodeFor(string name)
    {
        if(!_boneIndicesByName.TryGetValue(name,out var index))return null;
        _scriptNodes??=[];
        if(!_scriptNodes.TryGetValue(index,out var node))_scriptNodes[index]=node=new ScriptNode {Translate=_bones[index].RestTranslation};
        return node;
    }
    public bool TryGetScriptNode(string name,out PmxScriptNodeState state)
    {
        state=default;if(!_boneIndicesByName.TryGetValue(name,out var index))return false;
        var node=_scriptNodes?.GetValueOrDefault(index);var pose=_poseBuffer[index];var translate=node?.Translate??_bones[index].RestTranslation;
        state=new(name,ScriptVector(translate),ScriptRotation(node?.Rotate??Quaternion.Identity),node?.Scale??Vector3.One,
            ScriptVector(pose.Translation-(translate-_bones[index].RestTranslation)),ScriptRotation(Quaternion.Normalize(Quaternion.Inverse(node?.Rotate??Quaternion.Identity)*pose.Rotation)),ScriptVector(node?.SavedTranslate??Vector3.Zero),ScriptRotation(node?.SavedRotate??Quaternion.Identity));
        return true;
    }
    public bool SetScriptNodeTranslate(string name,Vector3 value,bool animated,bool overrideAnimation=true)
    {
        var node=ScriptNodeFor(name);if(node is null)return false;
        if(!float.IsFinite(value.LengthSquared()))throw new ArgumentOutOfRangeException(nameof(value));
        if(animated){node.AnimTranslate=ScriptVector(value);node.OverrideTranslate=overrideAnimation;}else {node.Translate=ScriptVector(value);node.ManualTranslate|=overrideAnimation;}
        return true;
    }
    public bool SetScriptNodeRotate(string name,Quaternion value,bool animated,bool overrideAnimation=true)
    {
        var node=ScriptNodeFor(name);if(node is null)return false;
        if(!float.IsFinite(value.LengthSquared())||value.LengthSquared()<1e-10f)throw new ArgumentOutOfRangeException(nameof(value));
        if(animated){node.AnimRotate=ScriptRotation(Quaternion.Normalize(value));node.OverrideRotate=overrideAnimation;}else {node.Rotate=ScriptRotation(Quaternion.Normalize(value));node.ManualRotate|=overrideAnimation;}
        return true;
    }
    public bool SetScriptNodeScale(string name,Vector3 value,bool overrideAnimation=true)
    {var node=ScriptNodeFor(name);if(node is null)return false;if(!float.IsFinite(value.LengthSquared()))throw new ArgumentOutOfRangeException(nameof(value));node.Scale=value;node.ManualScale|=overrideAnimation;return true;}
    public bool SaveScriptNode(string name)
    {
        var node=ScriptNodeFor(name);if(node is null||!_boneIndicesByName.TryGetValue(name,out var index))return false;
        node.SavedTranslate=_poseBuffer[index].Translation-(node.Translate-_bones[index].RestTranslation);node.SavedRotate=Quaternion.Normalize(Quaternion.Inverse(node.Rotate)*_poseBuffer[index].Rotation);node.HasSaved=true;return true;
    }
    public bool LoadScriptNode(string name){var node=ScriptNodeFor(name);if(node?.HasSaved!=true)return false;node.UseSaved=true;return true;}
    public bool ClearScriptNodeBase(string name){var node=ScriptNodeFor(name);if(node is null)return false;node.HasSaved=false;node.UseSaved=false;node.SavedTranslate=Vector3.Zero;node.SavedRotate=Quaternion.Identity;return true;}
    public bool ClearScriptNodeOverrides(string name)
    {if(!_boneIndicesByName.TryGetValue(name,out var index)||_scriptNodes?.TryGetValue(index,out var node)!=true)return false;bool removed=node.ManualTranslate||node.ManualRotate||node.ManualScale||node.OverrideTranslate||node.OverrideRotate;node.ClearOverrides();return removed;}
    public void ClearAllScriptNodeOverrides(){if(_scriptNodes is not null)foreach(var pair in _scriptNodes)pair.Value.ClearOverrides();}
    public void CopyScriptStateFrom(PmxPoseEvaluator previous)
    {
        _manualMorphs.Clear();
        foreach(var pair in previous._manualMorphs)_manualMorphs[pair.Key]=pair.Value;
        _scriptNodes=previous._scriptNodes?.ToDictionary(p=>p.Key,p=>p.Value.Clone());
        _scriptBaseMorphs=previous._scriptBaseMorphs is not null?new(previous._scriptBaseMorphs,StringComparer.Ordinal):null;
        _scriptSavedMorphs=previous._scriptSavedMorphs is not null?new(previous._scriptSavedMorphs,StringComparer.Ordinal):null;
    }
    public void CopyPlaybackClockFrom(PmxPoseEvaluator previous)
    {
        _previousTimeSeconds=previous._previousTimeSeconds;_previousFrame=previous._previousFrame;
        for(int i=0;i<Math.Min(_fallbackPhysics.Length,previous._fallbackPhysics.Length);i++)
        {_fallbackPhysics[i].Offset=previous._fallbackPhysics[i].Offset;_fallbackPhysics[i].Velocity=previous._fallbackPhysics[i].Velocity;}
    }
    private void ApplyScriptNodeOverrides(BonePose[] poses)
    {
        if(_scriptNodes is null)return;
        foreach(var pair in _scriptNodes)
        {
            var n=pair.Value;var pose=poses[pair.Key];bool tracked=_tracks.Any(t=>t.ContainsKey(pair.Key));
            Vector3 translate=n.OverrideTranslate||!tracked?n.AnimTranslate:pose.Translation;
            Quaternion rotate=n.OverrideRotate||!tracked?n.AnimRotate:pose.Rotation;
            if(n.UseSaved){translate+=n.SavedTranslate;rotate=Quaternion.Normalize(n.SavedRotate*rotate);}
            poses[pair.Key]=new(n.Translate-_bones[pair.Key].RestTranslation+translate,Quaternion.Normalize(n.Rotate*rotate));
        }
    }
    private static Vector3 ScriptVector(Vector3 value)=>new(value.X,value.Y,-value.Z);
    private static Quaternion ScriptRotation(Quaternion value)=>new(-value.X,-value.Y,value.Z,value.W);
    private sealed class ScriptNode
    {
        public Vector3 Translate,AnimTranslate,SavedTranslate,Scale=Vector3.One;
        public Quaternion Rotate=Quaternion.Identity,AnimRotate=Quaternion.Identity,SavedRotate=Quaternion.Identity;
        public bool OverrideTranslate,OverrideRotate,ManualTranslate,ManualRotate,ManualScale,HasSaved,UseSaved;
        public ScriptNode Clone()=>(ScriptNode)MemberwiseClone();
        public void ClearOverrides(){AnimTranslate=Vector3.Zero;AnimRotate=Quaternion.Identity;OverrideTranslate=false;OverrideRotate=false;ManualTranslate=false;ManualRotate=false;ManualScale=false;}
    }
}

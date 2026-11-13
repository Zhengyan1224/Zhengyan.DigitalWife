using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed partial class RuntimeEntity
{
    internal RuntimeScene? SceneOwner { get; set; }
    public bool Visible { get; set; } = true;
    public bool IsPlaying { get => Definition.IsPlaying; set { Definition.IsPlaying=value; if(value)PlayMotion();else PauseMotion(); } }
    public bool EnableEdge { get => Definition.EnableEdge; set => Definition.EnableEdge=value; }
    public bool PhysicsEnabled { get => Definition.EnablePhysics; set => Definition.EnablePhysics=value; }
    public Vector3 PhysicsGravity { get => Definition.PhysicsGravity; set => Definition.PhysicsGravity=value; }
    public Vector3 PhysicsGravityDirection { get => Definition.PhysicsGravityDirection.ToVector3(); set => Definition.PhysicsGravityDirection=Vector3Dto.FromVector3(value); }
    public float PhysicsGravityMagnitude { get => Definition.PhysicsGravityMagnitude; set => Definition.PhysicsGravityMagnitude=Math.Max(0,value); }
    public void SetPhysicsGravity(float x,float y,float z)=>SetPhysicsGravity(new Vector3(x,y,z));
    public void SetPhysicsGravity(Vector3 gravity)=>PhysicsGravity=gravity;
    public void SetPhysicsGravityDirection(float x,float y,float z)=>PhysicsGravityDirection=new Vector3(x,y,z);
    public void SetPhysicsGravityMagnitude(float magnitude)=>PhysicsGravityMagnitude=magnitude;
    public bool ResetPhysicsOnMotionLoop
    { get=>Definition.MotionLayers.Count==0?Definition.ResetPhysicsOnMotionLoop:Definition.MotionLayers.All(l=>l.ResetPhysicsOnLoop); set {Definition.ResetPhysicsOnMotionLoop=value;PmxAction("SetResetPhysicsOnMotionLoop",value);foreach(var layer in Definition.MotionLayers.ToArray())SetMotionLayerResetPhysicsOnLoop(layer.Path,value);} }
    public Vector3 CollisionCenter { get=>ColliderPosition;set=>ColliderPosition=value; }
    public Quaternion Rotation { get {var e=RotationDegrees*MathF.PI/180;return Quaternion.CreateFromYawPitchRoll(e.Y,e.X,e.Z);} set=>RotationDegrees=ToEulerDegrees(value); }
    private static Vector3 ToEulerDegrees(Quaternion q)
    { q=Quaternion.Normalize(q);float sinp=2*(q.W*q.X-q.Z*q.Y);return new Vector3(MathF.Asin(Math.Clamp(sinp,-1,1)),MathF.Atan2(2*(q.W*q.Y+q.X*q.Z),1-2*(q.X*q.X+q.Y*q.Y)),MathF.Atan2(2*(q.W*q.Z+q.X*q.Y),1-2*(q.X*q.X+q.Z*q.Z)))*180/MathF.PI; }
    private static Quaternion ToQuaternion(Vector3 degrees){var r=degrees*MathF.PI/180;return Quaternion.CreateFromYawPitchRoll(r.Y,r.X,r.Z);}
    public void Translate(float x,float y,float z)=>Position+=new Vector3(x,y,z);
    public void RotateX(float degrees)=>Rotation=Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitX,degrees*MathF.PI/180)*Rotation);
    public void RotateY(float degrees)=>Rotation=Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY,degrees*MathF.PI/180)*Rotation);
    public void RotateZ(float degrees)=>Rotation=Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitZ,degrees*MathF.PI/180)*Rotation);
    public bool RelationEnabled=>Definition.Relation.Enabled;
    public string RelationEntity=>Definition.Relation.RelationEntity;
    public bool RelationBindComponentTransform=>Definition.Relation.BindComponentTransform;
    public bool RelationBindLighting=>Definition.Relation.BindLighting;
    public bool TryBindRelation(string targetEntityIdOrName,bool bindComponentTransform=true,bool bindLighting=false)
    {var target=SceneOwner?.GetEntity(targetEntityIdOrName);if(target is null||ReferenceEquals(target,this)||!target.IsPmxModel)return false;BindRelation(target.Id,bindComponentTransform,bindLighting);return true;}
    public bool EnableWaterInteraction { get=>IsParticleSystem&&Definition.Particle.EnableWaterInteraction;set{if(IsParticleSystem)Definition.Particle.EnableWaterInteraction=value;} }
    public bool KillOnWaterContact {get=>IsParticleSystem&&Definition.Particle.KillOnWaterContact;set{if(IsParticleSystem)Definition.Particle.KillOnWaterContact=value;} }
    public bool ParticleCastsShadows {get=>IsParticleSystem&&Definition.Particle.CastShadows;set{if(IsParticleSystem)Definition.Particle.CastShadows=value;} }
    public Vector3 SpotLightDirection {get=>SpotDirection;set=>SetSpotLightDirection(value.X,value.Y,value.Z);}
    public float SpotLightInnerConeAngleDegrees {get=>Definition.SpotLight.InnerConeAngleDegrees;set=>Definition.SpotLight.InnerConeAngleDegrees=Math.Clamp(value,0,89.9f);}
    public float SpotLightOuterConeAngleDegrees {get=>Definition.SpotLight.OuterConeAngleDegrees;set=>Definition.SpotLight.OuterConeAngleDegrees=Math.Clamp(value,.01f,89.9f);}
    public bool WaterInteractionEnabled {get=>IsWaterSurface&&Definition.Water.EnableInteraction;set{if(IsWaterSurface)Definition.Water.EnableInteraction=value;} }
    public float WaterInteractionRadius {get=>IsWaterSurface?Definition.Water.InteractionRadius:0;set{if(IsWaterSurface)Definition.Water.InteractionRadius=Math.Max(.001f,value);} }
    public float WaterInteractionStrength {get=>IsWaterSurface?Definition.Water.InteractionStrength:0;set{if(IsWaterSurface)Definition.Water.InteractionStrength=Math.Clamp(value,0,4);} }
    public float ParticleRippleMinIntervalSeconds {get=>IsWaterSurface?Definition.Water.ParticleRippleMinIntervalSeconds:0;set{if(IsWaterSurface)Definition.Water.ParticleRippleMinIntervalSeconds=Math.Max(0,value);} }
    public float ParticleRippleMergeDistance {get=>IsWaterSurface?Definition.Water.ParticleRippleMergeDistance:0;set{if(IsWaterSurface)Definition.Water.ParticleRippleMergeDistance=Math.Max(0,value);} }
    public bool MirrorReflectionEnabled {get=>IsWaterSurface&&Definition.Water.MirrorReflectionEnabled;set{if(IsWaterSurface)Definition.Water.MirrorReflectionEnabled=value;} }
    public bool PlaneMirrorReflectionEnabled {get=>IsTexturedPlane&&Definition.Plane.MirrorReflectionEnabled;set{if(IsTexturedPlane)Definition.Plane.MirrorReflectionEnabled=value;} }
    public float PlaneMirrorReflectionStrength {get=>IsTexturedPlane?Definition.Plane.MirrorReflectionStrength:0;set{if(IsTexturedPlane)Definition.Plane.MirrorReflectionStrength=Math.Clamp(value,0,1);} }
    public bool GerstnerWavesEnabled {get=>IsWaterSurface&&Definition.Water.GerstnerWavesEnabled;set{if(IsWaterSurface)Definition.Water.GerstnerWavesEnabled=value;} }
    public int GerstnerWaveCount {get=>IsWaterSurface?Definition.Water.GerstnerWaveCount:0;set{if(IsWaterSurface)Definition.Water.GerstnerWaveCount=Math.Clamp(value,1,4);} }
    public float GerstnerAmplitude {get=>IsWaterSurface?Definition.Water.GerstnerAmplitude:0;set{if(IsWaterSurface)Definition.Water.GerstnerAmplitude=Math.Max(0,value);} }
    public float GerstnerWavelength {get=>IsWaterSurface?Definition.Water.GerstnerWavelength:0;set{if(IsWaterSurface)Definition.Water.GerstnerWavelength=Math.Max(.01f,value);} }
    public float GerstnerSpeed {get=>IsWaterSurface?Definition.Water.GerstnerSpeed:0;set{if(IsWaterSurface)Definition.Water.GerstnerSpeed=Math.Max(0,value);} }
    public float GerstnerSteepness {get=>IsWaterSurface?Definition.Water.GerstnerSteepness:0;set{if(IsWaterSurface)Definition.Water.GerstnerSteepness=Math.Clamp(value,0,1);} }
    public float GerstnerDirectionDegrees {get=>IsWaterSurface?Definition.Water.GerstnerDirectionDegrees:0;set{if(IsWaterSurface)Definition.Water.GerstnerDirectionDegrees=value;} }
    public float RippleLifetimeSeconds {get=>IsWaterSurface?Definition.Water.RippleLifetimeSeconds:0;set{if(IsWaterSurface)Definition.Water.RippleLifetimeSeconds=Math.Max(.05f,value);} }
    public float RippleWaveSpeed {get=>IsWaterSurface?Definition.Water.RippleWaveSpeed:0;set{if(IsWaterSurface)Definition.Water.RippleWaveSpeed=Math.Max(0,value);} }
    public float RippleFrequency {get=>IsWaterSurface?Definition.Water.RippleFrequency:0;set{if(IsWaterSurface)Definition.Water.RippleFrequency=Math.Max(0,value);} }
    public float RippleNormalStrength {get=>IsWaterSurface?Definition.Water.RippleNormalStrength:0;set{if(IsWaterSurface)Definition.Water.RippleNormalStrength=Math.Max(0,value);} }
}

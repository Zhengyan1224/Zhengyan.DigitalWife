using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

public sealed partial class AndroidScriptEntity
{
    public System.String AddMeshCollider(System.String name, System.Boolean walkable = true, System.Single maxSlopeDegrees = 55f, System.Single offsetX = 0f, System.Single offsetY = 0f, System.Single offsetZ = 0f, System.Single scaleX = 1f, System.Single scaleY = 1f, System.Single scaleZ = 1f, System.Single rotationX = 0f, System.Single rotationY = 0f, System.Single rotationZ = 0f) => _entity.AddMeshCollider(name, walkable, maxSlopeDegrees, offsetX, offsetY, offsetZ, scaleX, scaleY, scaleZ, rotationX, rotationY, rotationZ);
    public System.Single AnimationTimeSeconds { get => _entity.AnimationTimeSeconds; set => _entity.AnimationTimeSeconds = value; }
    public void BindRelation(System.String targetEntityIdOrName, System.Boolean bindComponentTransform = true, System.Boolean bindLighting = false) => _entity.BindRelation(targetEntityIdOrName, bindComponentTransform, bindLighting);
    public void ClearRelationBinding() => _entity.ClearRelationBinding();
    public System.String CollisionAxis { get => _entity.CollisionAxis; set => _entity.CollisionAxis = value; }
    public System.Numerics.Vector3 CollisionCenter { get => _entity.CollisionCenter; set => _entity.CollisionCenter = value; }
    public System.Single CollisionHeight { get => _entity.CollisionHeight; set => _entity.CollisionHeight = value; }
    public System.Single CollisionRadius { get => _entity.CollisionRadius; set => _entity.CollisionRadius = value; }
    public System.Boolean EnableEdge { get => _entity.EnableEdge; set => _entity.EnableEdge = value; }
    public System.Boolean EnableWaterInteraction { get => _entity.EnableWaterInteraction; set => _entity.EnableWaterInteraction = value; }
    public System.Single GerstnerAmplitude { get => _entity.GerstnerAmplitude; set => _entity.GerstnerAmplitude = value; }
    public System.Single GerstnerDirectionDegrees { get => _entity.GerstnerDirectionDegrees; set => _entity.GerstnerDirectionDegrees = value; }
    public System.Single GerstnerSpeed { get => _entity.GerstnerSpeed; set => _entity.GerstnerSpeed = value; }
    public System.Single GerstnerSteepness { get => _entity.GerstnerSteepness; set => _entity.GerstnerSteepness = value; }
    public System.Int32 GerstnerWaveCount { get => _entity.GerstnerWaveCount; set => _entity.GerstnerWaveCount = value; }
    public System.Single GerstnerWavelength { get => _entity.GerstnerWavelength; set => _entity.GerstnerWavelength = value; }
    public System.Boolean GerstnerWavesEnabled { get => _entity.GerstnerWavesEnabled; set => _entity.GerstnerWavesEnabled = value; }
    public System.Boolean IsPlaying { get => _entity.IsPlaying; set => _entity.IsPlaying = value; }
    public System.Boolean IsPointLight { get => _entity.IsPointLight; }
    public System.Boolean IsSpotLight { get => _entity.IsSpotLight; }
    public System.Boolean KillOnWaterContact { get => _entity.KillOnWaterContact; set => _entity.KillOnWaterContact = value; }
    public System.Boolean MirrorReflectionEnabled { get => _entity.MirrorReflectionEnabled; set => _entity.MirrorReflectionEnabled = value; }
    public System.Boolean ParticleCastsShadows { get => _entity.ParticleCastsShadows; set => _entity.ParticleCastsShadows = value; }
    public System.Single ParticleRippleMergeDistance { get => _entity.ParticleRippleMergeDistance; set => _entity.ParticleRippleMergeDistance = value; }
    public System.Single ParticleRippleMinIntervalSeconds { get => _entity.ParticleRippleMinIntervalSeconds; set => _entity.ParticleRippleMinIntervalSeconds = value; }
    public System.Boolean PlaneMirrorReflectionEnabled { get => _entity.PlaneMirrorReflectionEnabled; set => _entity.PlaneMirrorReflectionEnabled = value; }
    public System.Single PlaneMirrorReflectionStrength { get => _entity.PlaneMirrorReflectionStrength; set => _entity.PlaneMirrorReflectionStrength = value; }
    public void PlayMotion() => _entity.PlayMotion();
    public System.Boolean PointLightCastsShadows { get => _entity.PointLightCastsShadows; set => _entity.PointLightCastsShadows = value; }
    public System.Numerics.Vector3 PointLightColor { get => _entity.PointLightColor; set => _entity.PointLightColor = value; }
    public System.Boolean PointLightEnabled { get => _entity.PointLightEnabled; set => _entity.PointLightEnabled = value; }
    public System.Single PointLightIntensity { get => _entity.PointLightIntensity; set => _entity.PointLightIntensity = value; }
    public System.Single PointLightRange { get => _entity.PointLightRange; set => _entity.PointLightRange = value; }
    public System.Boolean RelationBindComponentTransform { get => _entity.RelationBindComponentTransform; }
    public System.Boolean RelationBindLighting { get => _entity.RelationBindLighting; }
    public System.Boolean RelationEnabled { get => _entity.RelationEnabled; }
    public System.String RelationEntity { get => _entity.RelationEntity; }
    public System.Single RippleFrequency { get => _entity.RippleFrequency; set => _entity.RippleFrequency = value; }
    public System.Single RippleLifetimeSeconds { get => _entity.RippleLifetimeSeconds; set => _entity.RippleLifetimeSeconds = value; }
    public System.Single RippleNormalStrength { get => _entity.RippleNormalStrength; set => _entity.RippleNormalStrength = value; }
    public System.Single RippleWaveSpeed { get => _entity.RippleWaveSpeed; set => _entity.RippleWaveSpeed = value; }
    public void RotateX(System.Single degrees) => _entity.RotateX(degrees);
    public void RotateY(System.Single degrees) => _entity.RotateY(degrees);
    public void RotateZ(System.Single degrees) => _entity.RotateZ(degrees);
    public System.Numerics.Quaternion Rotation { get => _entity.Rotation; set => _entity.Rotation = value; }
    public void SetPhysicsGravity(System.Single x, System.Single y, System.Single z) => _entity.SetPhysicsGravity(x, y, z);
    public void SetPhysicsGravity(System.Numerics.Vector3 gravity) => _entity.SetPhysicsGravity(gravity);
    public void SetPhysicsGravityDirection(System.Single x, System.Single y, System.Single z) => _entity.SetPhysicsGravityDirection(x, y, z);
    public void SetPhysicsGravityMagnitude(System.Single magnitude) => _entity.SetPhysicsGravityMagnitude(magnitude);
    public void SetPointLightColor(System.Single red, System.Single green, System.Single blue) => _entity.SetPointLightColor(red, green, blue);
    public void SetSpotLightColor(System.Single red, System.Single green, System.Single blue) => _entity.SetSpotLightColor(red, green, blue);
    public void SetSpotLightDirection(System.Single x, System.Single y, System.Single z) => _entity.SetSpotLightDirection(x, y, z);
    public void Speak(System.String text, System.Int32 speakerId, System.Action onCompleted) => _entity.Speak(text, speakerId, onCompleted);
    public void Speak(System.String text, System.Int32 speakerId, System.Single speed, System.Action onCompleted) => _entity.Speak(text, speakerId, speed, onCompleted);
    public void Speak(System.String text, System.Int32 speakerId, System.Single speed, System.Single volume) => _entity.Speak(text, speakerId, speed, volume);
    public void Speak(System.String text, System.Int32 speakerId, System.Single speed) => _entity.Speak(text, speakerId, speed);
    public void Speak(System.String text, System.Int32 speakerId) => _entity.Speak(text, speakerId);
    public void Speak(System.String text) => _entity.Speak(text);
    public System.Boolean SpotLightCastsShadows { get => _entity.SpotLightCastsShadows; set => _entity.SpotLightCastsShadows = value; }
    public System.Numerics.Vector3 SpotLightColor { get => _entity.SpotLightColor; set => _entity.SpotLightColor = value; }
    public System.Numerics.Vector3 SpotLightDirection { get => _entity.SpotLightDirection; set => _entity.SpotLightDirection = value; }
    public System.Boolean SpotLightEnabled { get => _entity.SpotLightEnabled; set => _entity.SpotLightEnabled = value; }
    public System.Single SpotLightInnerConeAngleDegrees { get => _entity.SpotLightInnerConeAngleDegrees; set => _entity.SpotLightInnerConeAngleDegrees = value; }
    public System.Single SpotLightIntensity { get => _entity.SpotLightIntensity; set => _entity.SpotLightIntensity = value; }
    public System.Single SpotLightOuterConeAngleDegrees { get => _entity.SpotLightOuterConeAngleDegrees; set => _entity.SpotLightOuterConeAngleDegrees = value; }
    public System.Single SpotLightRange { get => _entity.SpotLightRange; set => _entity.SpotLightRange = value; }
    public void Translate(System.Single x, System.Single y, System.Single z) => _entity.Translate(x, y, z);
    public System.Boolean TryBindRelation(System.String targetEntityIdOrName, System.Boolean bindComponentTransform = true, System.Boolean bindLighting = false) => _entity.TryBindRelation(targetEntityIdOrName, bindComponentTransform, bindLighting);
    public System.String Type { get => _entity.Type; }
    public System.Boolean Visible { get => _entity.Visible; set => _entity.Visible = value; }
    public System.Boolean WaterInteractionEnabled { get => _entity.WaterInteractionEnabled; set => _entity.WaterInteractionEnabled = value; }
    public System.Single WaterInteractionRadius { get => _entity.WaterInteractionRadius; set => _entity.WaterInteractionRadius = value; }
    public System.Single WaterInteractionStrength { get => _entity.WaterInteractionStrength; set => _entity.WaterInteractionStrength = value; }
}

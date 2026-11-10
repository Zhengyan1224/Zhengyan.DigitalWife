namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed record WebGraphicsApplied(long Revision,int SceneSamples,int DirectionalResolution,int LocalLightResolution,
    Dictionary<string,int>? ReflectionSamples,string ScenePath);

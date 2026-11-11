namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public readonly record struct MotionLayerDefinition(string MotionPath, float Weight = 1, bool? ResetPhysicsOnLoop = null);

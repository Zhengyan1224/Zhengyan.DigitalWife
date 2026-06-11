namespace Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;

public interface ITransformUpdater
{
    TransformUpdaterStage Stage { get; }

    bool Enabled { get; set; }

    bool UpdateTransform(PmxModelComponent component, float elapsedSeconds);
}


namespace Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;

public sealed class TransformUpdaterManager
{
    private readonly List<ITransformUpdater> _updaters = [];

    public int Count => _updaters.Count;

    public bool HasEnabledUpdaters => _updaters.Any(static updater => updater.Enabled);

    public IReadOnlyList<ITransformUpdater> Items => _updaters;

    public void Add(ITransformUpdater updater)
    {
        ArgumentNullException.ThrowIfNull(updater);

        if (!_updaters.Contains(updater))
        {
            _updaters.Add(updater);
        }
    }

    public bool Remove(ITransformUpdater updater)
    {
        ArgumentNullException.ThrowIfNull(updater);
        return _updaters.Remove(updater);
    }

    public void Clear()
    {
        _updaters.Clear();
    }

    internal void UpdateStage(TransformUpdaterStage stage, PmxModelComponent component, float elapsedSeconds)
    {
        for (int i = 0; i < _updaters.Count; i++)
        {
            ITransformUpdater updater = _updaters[i];
            if (!updater.Enabled || updater.Stage != stage)
            {
                continue;
            }

            _ = updater.UpdateTransform(component, elapsedSeconds);
        }
    }
}


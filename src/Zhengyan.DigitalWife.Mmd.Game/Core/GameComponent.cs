namespace Zhengyan.DigitalWife.Mmd.Game;

public abstract class GameComponent : IDisposable
{
    public Game? Game { get; private set; }

    public bool Enabled { get; set; } = true;

    public int UpdateOrder { get; set; }

    internal void Attach(Game game)
    {
        if (Game == game)
        {
            return;
        }

        Game = game;
        Initialize();
    }

    protected virtual void Initialize() { }

    public virtual void Update(GameTime gameTime) { }

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}


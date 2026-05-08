namespace Zhengyan.DigitalWife.Mmd.Game;

public abstract class DrawableGameComponent : GameComponent
{
    public bool Visible { get; set; } = true;

    public int DrawOrder { get; set; }

    public virtual void Draw(GameTime gameTime) { }
}


namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed record RuntimeGamepadInput(bool Connected=false,string Name="",int Index=-1,float[]? Axes=null,float[]? Buttons=null)
{
    public float Axis(int index)=>Axes is not null&&index<Axes.Length?Axes[index]:0;
    public float Button(int index)=>Buttons is not null&&index<Buttons.Length?Buttons[index]:0;
}


using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

/// <summary>Shared script-facing wrapper for a scene GUI control.</summary>
public sealed class RuntimeGuiControl
{
    private readonly GuiControlSettings _control;

    internal RuntimeGuiControl(GuiControlSettings control) => _control = control;

    public string Id => _control.Id;
    public string Name { get => _control.Name; set => _control.Name = value ?? string.Empty; }
    public string Type { get => _control.Type; set => _control.Type = value ?? string.Empty; }
    public string Text { get => _control.Text; set => _control.Text = value ?? string.Empty; }
    public string Value { get => Text; set => SetValue(value); }
    public bool Visible { get => _control.Visible; set => _control.Visible = value; }
    public bool Checked { get => _control.Checked; set => _control.Checked = value; }
    public float Progress { get => _control.Progress; set => _control.Progress = Math.Clamp(value, 0.0f, 1.0f); }
    public int SelectedIndex { get => _control.SelectedIndex; set => _control.SelectedIndex = Math.Max(value, 0); }
    public float X { get => _control.X; set => _control.X = Math.Max(value, 0.0f); }
    public float Y { get => _control.Y; set => _control.Y = Math.Max(value, 0.0f); }
    public float Width { get => _control.Width; set => _control.Width = Math.Max(value, 1.0f); }
    public float Height { get => _control.Height; set => _control.Height = Math.Max(value, 1.0f); }

    public void SetValue(string value) => Text = value ?? string.Empty;
    public void SetText(string value) => SetValue(value);
}

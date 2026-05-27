using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed class RuntimeGuiControl
{
    private readonly GuiControlSettings _control;

    internal RuntimeGuiControl(GuiControlSettings control)
    {
        _control = control;
    }

    public string Id => _control.Id;

    public string Name
    {
        get => _control.Name;
        set => _control.Name = value ?? string.Empty;
    }

    public string Type
    {
        get => _control.Type;
        set => _control.Type = value ?? string.Empty;
    }

    public string Text
    {
        get => _control.Text;
        set => _control.Text = value ?? string.Empty;
    }

    public string Value
    {
        get => Text;
        set => Text = value;
    }

    public bool Visible
    {
        get => _control.Visible;
        set => _control.Visible = value;
    }

    public float X
    {
        get => _control.X;
        set => _control.X = Math.Max(0.0f, value);
    }

    public float Y
    {
        get => _control.Y;
        set => _control.Y = Math.Max(0.0f, value);
    }

    public float Width
    {
        get => _control.Width;
        set => _control.Width = Math.Max(1.0f, value);
    }

    public float Height
    {
        get => _control.Height;
        set => _control.Height = Math.Max(1.0f, value);
    }

    public string LayoutMode
    {
        get => _control.LayoutMode;
        set => _control.LayoutMode = LayoutResolver.NormalizeLayoutMode(value);
    }

    public string TargetEntity
    {
        get => _control.TargetEntity;
        set => _control.TargetEntity = value ?? string.Empty;
    }

    public string EventName
    {
        get => _control.EventName;
        set => _control.EventName = value ?? string.Empty;
    }

    public bool Checked
    {
        get => _control.Checked;
        set => _control.Checked = value;
    }

    public float Progress
    {
        get => _control.Progress;
        set => _control.Progress = Math.Clamp(value, 0.0f, 1.0f);
    }

    public float FontSize
    {
        get => _control.Style.FontSize;
        set => _control.Style.FontSize = Math.Clamp(value, 8.0f, 96.0f);
    }

    public bool WordWrap
    {
        get => _control.WordWrap;
        set => _control.WordWrap = value;
    }

    public bool Multiline
    {
        get => _control.Multiline;
        set => _control.Multiline = value;
    }

    public void SetWordWrap(bool value)
    {
        WordWrap = value;
    }

    public void SetMultiline(bool value)
    {
        Multiline = value;
    }

    public void SetValue(string value)
    {
        Value = value;
    }

    public IReadOnlyList<string> Items => _control.Items;

    public int SelectedIndex
    {
        get => _control.SelectedIndex;
        set => _control.SelectedIndex = Math.Clamp(value, 0, Math.Max(_control.Items.Count - 1, 0));
    }

    public string SelectedItem => _control.Items.Count == 0
        ? string.Empty
        : _control.Items[Math.Clamp(_control.SelectedIndex, 0, _control.Items.Count - 1)];

    public void SetChecked(bool value)
    {
        Checked = value;
    }

    public void SetProgress(float value)
    {
        Progress = value;
    }

    public void SetLayoutMode(string value)
    {
        LayoutMode = value;
    }

    public void SetFontSize(float value)
    {
        FontSize = value;
    }

    public void SetItems(params string[] items)
    {
        _control.Items = items.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
        SelectedIndex = _control.SelectedIndex;
    }

    public void SetSelectedIndex(int index)
    {
        SelectedIndex = index;
    }

    public void SetPosition(float x, float y)
    {
        X = x;
        Y = y;
    }

    public void SetSize(float width, float height)
    {
        Width = width;
        Height = height;
    }

    public void Show()
    {
        Visible = true;
    }

    public void Hide()
    {
        Visible = false;
    }
}

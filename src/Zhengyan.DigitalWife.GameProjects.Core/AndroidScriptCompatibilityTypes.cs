using System.Numerics;

namespace Zhengyan.DigitalWife.GameProjects;

public sealed record RuntimeLlmChatMessage(string Role, string Content)
{
    public string? ToolCallId { get; init; }
    public IReadOnlyList<RuntimeLlmToolCall> ToolCalls { get; init; } = [];
}
public sealed record RuntimeLlmToolCall(string Id, string Name, string ArgumentsJson);
public sealed record RuntimeLlmTool(string Name, string Description = "", string ParametersJsonSchema = "{}")
{
    public string CallbackName { get; init; } = string.Empty;
    public Func<RuntimeLlmToolCall, CancellationToken, Task<string>>? Handler { get; init; }

    public RuntimeLlmTool(string name, string description, string parametersJsonSchema, Func<RuntimeLlmToolCall, CancellationToken, Task<string>> handler)
        : this(name, description, parametersJsonSchema)
    {
        Handler = handler;
    }

    public RuntimeLlmTool(string name, string description, string parametersJsonSchema, Func<string, string> handler)
        : this(name, description, parametersJsonSchema)
    {
        Handler = (call, _) => Task.FromResult(handler(call.ArgumentsJson));
    }
}

public sealed record RuntimeLlmScriptTool(
    string Name,
    string Description,
    string ParametersJsonSchema,
    string CallbackName)
{
    public RuntimeLlmTool ToTool(object? entity = null, object? scene = null) => new(Name, Description, ParametersJsonSchema)
    {
        CallbackName = CallbackName
    };
}

public sealed class RuntimeDialogueBubble
{
    public RuntimeDialogueBubble(string name) => Name = name ?? string.Empty;
    public string Name { get; }
    public string Text { get; private set; } = string.Empty;
    public string HeaderText { get; private set; } = string.Empty;
    public string FooterText { get; private set; } = string.Empty;
    public float Width { get; set; } = 360.0f;
    public float FontSize { get; set; } = 18.0f;
    public float HeaderFontSize { get; set; } = 16.0f;
    public float FooterFontSize { get; set; } = 15.0f;
    public Vector4 BackgroundColor { get; set; } = new(0.07f, 0.08f, 0.11f, 0.92f);
    public Vector4 BorderColor { get; set; } = new(0.42f, 0.64f, 0.95f, 0.85f);
    public Vector4 HeaderTextColor { get; set; } = new(0.78f, 0.84f, 0.95f, 1.0f);
    public Vector4 TextColor { get; set; } = Vector4.One;
    public Vector4 FooterTextColor { get; set; } = new(0.72f, 0.76f, 0.82f, 1.0f);
    public string TextAlignment { get; set; } = "left";
    public bool Visible { get; private set; }
    public string AnchorEntity { get; private set; } = string.Empty;
    public bool UseEntityTopAnchor { get; private set; } = true;
    public Vector3 WorldOffset { get; private set; }
    public Vector2 ScreenOffset { get; private set; }
    public void AttachToEntity(string entityId, bool useModelTopAnchor = true) { AnchorEntity = entityId ?? string.Empty; UseEntityTopAnchor = useModelTopAnchor; }
    public void SetWorldOffset(float x, float y, float z) => WorldOffset = new Vector3(x, y, z);
    public void SetScreenOffset(float x, float y) => ScreenOffset = new Vector2(x, y);
    public void SetContent(string text, string headerText = "", string footerText = "") { Text = text ?? string.Empty; HeaderText = headerText ?? string.Empty; FooterText = footerText ?? string.Empty; }
    public void Show() => Visible = true;
    public void Hide() => Visible = false;
}

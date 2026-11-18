namespace Zhengyan.DigitalWife.GameProjects;

/// <summary>The object hit when a context menu opened, retained until its item is selected.</summary>
public sealed record ContextMenuTargetInfo
{
    public string TargetType { get; init; } = "window";
    public string TargetId { get; init; } = string.Empty;
    public string TargetName { get; init; } = string.Empty;
    // For GUI/sprite targets this is their configured script recipient, not the hit object.
    public string EntityId { get; init; } = string.Empty;
    public string EntityName { get; init; } = string.Empty;
    public string ColliderId { get; init; } = string.Empty;
    public string ColliderName { get; init; } = string.Empty;
    public string ColliderType { get; init; } = string.Empty;
    public float PointerX { get; init; }
    public float PointerY { get; init; }
}

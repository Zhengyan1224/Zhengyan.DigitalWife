namespace Zhengyan.DigitalWife.Assistant.Text;

public sealed class SentenceChunkerOptions
{
    public bool EnableClauseBoundaries { get; init; } = true;

    public int MinClauseCharacters { get; init; } = 12;

    public int MaxBufferedCharacters { get; init; } = 320;
}

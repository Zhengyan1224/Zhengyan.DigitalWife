using System.Globalization;
using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal readonly record struct AndroidBubbleFontMetrics(float Top, float Bottom, float LineHeight);
internal sealed record AndroidBubbleTextLine(string Text, float X, float Baseline, float FontSize, int Part);
internal sealed record AndroidBubbleTextLayout(Vector2 Size, float Padding, IReadOnlyList<AndroidBubbleTextLine> Lines);

internal static class AndroidDialogueBubbleTextLayout
{
    internal static AndroidBubbleTextLayout Build(RuntimeDialogueBubble bubble, Vector2 canvasSize, Vector2 referenceSize,
        Func<string, float, float> measure, Func<float, AndroidBubbleFontMetrics> metrics)
    {
        float width = Math.Clamp(LayoutResolver.Resolve(bubble.LayoutMode, 0, 0, bubble.Width, 1,
            canvasSize.X, canvasSize.Y, referenceSize.X, referenceSize.Y).Width,
            Math.Min(160, canvasSize.X * .9f), canvasSize.X * .9f);
        float scale = LayoutResolver.IsRelative(bubble.LayoutMode)
            ? MathF.Min(canvasSize.X / Math.Max(1, referenceSize.X), canvasSize.Y / Math.Max(1, referenceSize.Y)) : 1;
        float padding = Math.Min(Math.Max(4, 14 * scale), width * .2f);
        float contentWidth = Math.Max(1, width - padding * 2);
        float y = padding;
        List<AndroidBubbleTextLine> lines = [];
        string[] blocks = [bubble.HeaderText, bubble.Text, bubble.FooterText];
        float[] sizes = [bubble.HeaderFontSize, bubble.FontSize, bubble.FooterFontSize];
        for (int part = 0; part < blocks.Length; part++)
        {
            if (string.IsNullOrWhiteSpace(blocks[part])) continue;
            if (lines.Count > 0) y += Math.Max(4, 6 * scale);
            float fontSize = LayoutResolver.ResolveFontSize(bubble.LayoutMode, sizes[part],
                canvasSize.X, canvasSize.Y, referenceSize.X, referenceSize.Y);
            AndroidBubbleFontMetrics font = metrics(fontSize);
            float lineHeight = Math.Max(font.LineHeight, font.Bottom - font.Top);
            foreach (string text in Wrap(blocks[part], contentWidth, value => measure(value, fontSize)))
            {
                float textWidth = measure(text, fontSize);
                float x = bubble.TextAlignment.ToLowerInvariant() switch
                {
                    "right" => Math.Max(0, contentWidth - textWidth),
                    "center" => Math.Max(0, (contentWidth - textWidth) * .5f),
                    _ => 0
                };
                lines.Add(new(text, padding + x, y - font.Top, fontSize, part));
                y += lineHeight;
            }
        }
        return new(new(width, y + padding), padding, lines);
    }

    internal static IReadOnlyList<string> Wrap(string text, float width, Func<string, float> measure)
    {
        List<string> lines = [];
        foreach (string paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (paragraph.Length == 0) { lines.Add(string.Empty); continue; }
            int[] elements = StringInfo.ParseCombiningCharacters(paragraph);
            int start = 0;
            while (start < elements.Length)
            {
                // Measure actual glyph widths; Chinese, Latin, emoji and
                // combining sequences have different widths at the same size.
                int low = start + 1;
                int high = elements.Length;
                int end = low;
                while (low <= high)
                {
                    int middle = low + (high - low) / 2;
                    int endIndex = middle == elements.Length ? paragraph.Length : elements[middle];
                    if (measure(paragraph[elements[start]..endIndex]) <= width)
                    {
                        end = middle;
                        low = middle + 1;
                    }
                    else high = middle - 1;
                }
                int next = end == elements.Length ? paragraph.Length : elements[end];
                lines.Add(paragraph[elements[start]..next]);
                start = end;
            }
        }
        return lines;
    }
}

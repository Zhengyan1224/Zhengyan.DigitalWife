namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public static class AntiAliasingSamples
{
    public static readonly int[] Choices = [1, 2, 4, 8, 16];

    public static int NormalizeRequested(int samples)
    {
        if (samples <= 1) return 1;
        if (samples <= 2) return 2;
        if (samples <= 4) return 4;
        if (samples <= 8) return 8;
        return 16;
    }

    public static int FallbackToSupported(int requested, int maximumSupported)
    {
        int normalized = NormalizeRequested(requested);
        int maximum = NormalizeMaximum(maximumSupported);
        for (int i = Choices.Length - 1; i >= 0; i--)
        {
            if (Choices[i] <= normalized && Choices[i] <= maximum)
            {
                return Choices[i];
            }
        }

        return 1;
    }

    private static int NormalizeMaximum(int samples)
    {
        for (int i = Choices.Length - 1; i >= 0; i--)
        {
            if (samples >= Choices[i]) return Choices[i];
        }

        return 1;
    }
}

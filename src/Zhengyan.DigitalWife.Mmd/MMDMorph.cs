namespace Zhengyan.DigitalWife.Mmd;

public enum MMDMorphKind
{
    Unknown,
    Position,
    UV,
    Material,
    Bone,
    Group
}

public class MMDMorph
{
    public string Name { get; set; }

    public float Weight { get; set; }

    public float SaveAnimWeight { get; set; }

    public MMDMorphKind Kind { get; set; }

    public MMDMorph()
    {
        Name = string.Empty;
        Kind = MMDMorphKind.Unknown;
    }

    public void SaveBaseAnimation()
    {
        SaveAnimWeight = Weight;
    }

    public void LoadBaseAnimation()
    {
        Weight = SaveAnimWeight;
    }

    public void ClearBaseAnimation()
    {
        SaveAnimWeight = 0.0f;
    }
}

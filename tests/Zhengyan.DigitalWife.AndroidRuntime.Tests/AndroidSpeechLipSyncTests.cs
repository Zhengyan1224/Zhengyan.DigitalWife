extern alias MmdDesktop;
using System.Collections;
using System.Reflection;
using Zhengyan.DigitalWife.GamePlayer.Android;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Zhengyan.DigitalWife.Mmd.Game.Speech;
using Model = MmdDesktop::Zhengyan.DigitalWife.Mmd.PmxModel;
using Morph = MmdDesktop::Zhengyan.DigitalWife.Mmd.MMDMorph;

internal static class AndroidSpeechLipSyncTests
{
    internal static void TestPlaybackMorphs()
    {
        using Model model = new();
        var morphs = (IList)typeof(Model).GetField("_morphs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(model)!;
        Type morphType = typeof(Model).GetNestedType("PmxMorph", BindingFlags.NonPublic)!;
        foreach (string name in new[] { "mouth_a", "mouth_i", "smile" })
        {
            Morph morph = (Morph)Activator.CreateInstance(morphType)!;
            morph.Name = name;
            morph.Weight = name == "smile" ? 0.7f : 0;
            morphs.Add(morph);
        }
        // The constructor validates existence; the test supplies an in-memory
        // model below and never parses or uploads a PMX file.
        PmxModelComponent component = new(typeof(AndroidSpeechLipSyncTests).Assembly.Location);
        typeof(PmxModelComponent).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, model);
        SpeechDictionarySet dictionaries = new(new KanaDictionary(), new VowelDictionary());
        GameProjectLipSyncSettings settings = new()
        {
            VowelMorphMap = new() { ["あ"] = "mouth_a", ["い"] = "mouth_i" },
            UseFallbackVowelOnNoMatch = true, NoMatchFallbackVowel = "あ"
        };
        using AndroidSpeechLipSync speech = new(component, dictionaries, settings, "あい", 1);
        var updater = component.TransformUpdaters.Items.Single();
        updater.UpdateTransform(component, 0.04f);
        if (model.FindMorph(m => m.Name == "mouth_a")!.Weight <= 0) throw new Exception("Speech did not open the mouth.");
        speech.SetRange("い");
        updater.UpdateTransform(component, 0.04f);
        if (model.FindMorph(m => m.Name == "mouth_i")!.Weight <= 0 || model.FindMorph(m => m.Name == "mouth_a")!.Weight != 0)
            throw new Exception("Playback range did not change the vowel pose.");
        speech.Dispose();
        if (component.TransformUpdaters.Count != 0 || model.GetMorphs().Where(m => m.Name.StartsWith("mouth_")).Any(m => m.Weight != 0))
            throw new Exception("Speech completion/cancellation left an active mouth pose.");
        if (model.FindMorph(m => m.Name == "smile")!.Weight != 0.7f) throw new Exception("Lip sync changed an unrelated facial morph.");
        speech.SetRange("あ");
        if (component.TransformUpdaters.Count != 0) throw new Exception("A stale callback restarted lip sync.");
    }
}

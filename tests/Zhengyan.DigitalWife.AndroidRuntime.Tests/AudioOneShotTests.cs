using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Android.Media;
using Zhengyan.DigitalWife.GamePlayer.Android;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.Mmd.Game.Audio;

internal static class AudioOneShotTests
{
    public static void TestAndroidOverlapAndCleanup()
    {
        MediaPlayer.Instances.Clear();
        string root = Path.GetFullPath(Path.Combine("temp", "android-audio-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "bgm.wav"), [0]);
        File.WriteAllBytes(Path.Combine(root, "sfx.wav"), [0]);
        using RuntimeScene scene = new("main.scene.json", new()
        {
            Audio = [new() { Name = "BGM", Path = "bgm.wav", Loop = true, Volume = .5f },
                new() { Name = "Sfx", Path = "sfx.wav", Loop = true, Volume = .8f }]
        }, path => Path.Combine(root, path));
        using AndroidAudioHost host = new(root);
        foreach (var asset in scene.Definition.Audio) host.LoadAsset(asset);
        Check(host.Play(scene, "BGM") && host.Play(scene, "Sfx"), "Regular playback failed.");
        MediaPlayer bgm = MediaPlayer.Instances[0], ordinary = MediaPlayer.Instances[1];
        Check(host.SetVolume("sfx.wav", .6f), "Path alias did not update runtime volume.");
        AndroidScriptAudio audio = new(name => host.Play(scene, name), host.Pause, host.Stop,
            host.SetVolume, host.SetLoop, host.IsPlaying, (name, scale) => host.PlayOneShot(scene, name, scale));
        Check(audio.PlayOneShot("SFX") && audio.PlayOneShot("sfx.wav", .5f), "Script one-shot calls failed.");
        MediaPlayer first = MediaPlayer.Instances[2], second = MediaPlayer.Instances[3];
        Check(first.IsPlaying && second.IsPlaying && bgm.IsPlaying && ordinary.IsPlaying,
            "Repeated one-shots interrupted each other or regular playback.");
        Check(!first.Looping && !second.Looping && ordinary.Looping && bgm.Looping, "One-shot inherited a looping resource setting.");
        Check(Math.Abs(first.Volume - .6f) < .001f && Math.Abs(second.Volume - .3f) < .001f, "One-shot volume ignored current volume or scale.");
        Check(audio.SetVolume("Sfx", .2f) && audio.PlayOneShot("Sfx"), "New one-shot after volume change failed.");
        MediaPlayer third = MediaPlayer.Instances[4];
        Check(Math.Abs(third.Volume - .2f) < .001f && Math.Abs(first.Volume - .6f) < .001f, "Volume change modified an existing independent instance.");
        Check(audio.Pause("Sfx") && first.IsPlaying && second.IsPlaying, "Pause affected independent instances.");
        int seeks = ordinary.SeekCount;
        Check(audio.Play("Sfx") && ordinary.SeekCount == seeks, "Regular pause/resume regressed.");
        Check(audio.Stop("Sfx") && first.IsPlaying && second.IsPlaying, "Stop affected independent instances.");
        first.Complete();
        Check(first.ReleaseCount == 1 && first.DisposeCount == 1 && second.IsPlaying && bgm.IsPlaying, "Completion did not release only the finished instance.");
        Check(second.Fail() && second.ReleaseCount == 1 && second.DisposeCount == 1, "Native playback error leaked the instance.");
        int count = MediaPlayer.Instances.Count;
        Check(!audio.PlayOneShot("missing") && MediaPlayer.Instances.Count == count, "Missing resource created a player.");
        scene.Definition.Audio.Add(new() { Name = "MissingFile", Path = "missing.wav" });
        Check(!audio.PlayOneShot("MissingFile") && bgm.IsPlaying, "Missing file interrupted background music.");
        MediaPlayer.FailNextPrepare = true;
        Check(!audio.PlayOneShot("Sfx") && MediaPlayer.Instances[^1].ReleaseCount == 1, "Preparation failure leaked a player.");
        MediaPlayer.FailNextStart = true;
        Check(!audio.PlayOneShot("Sfx") && MediaPlayer.Instances[^1].ReleaseCount == 1, "Start failure leaked a player.");
        host.StopAll();
        third.Complete();
        Check(MediaPlayer.Instances.All(player => player.ReleaseCount == 1 && player.DisposeCount == 1), "Scene cleanup leaked or released a player twice.");
        Check(audio.PlayOneShot("Sfx"), "Playback could not restart after scene cleanup.");
        host.Dispose();
        Check(!audio.PlayOneShot("Sfx") && MediaPlayer.Instances.All(player => player.ReleaseCount == 1), "Disposed host created or leaked an instance.");
    }

    public static void TestDesktopOverlapAndCleanup()
    {
        using AudioEngine engine = new();
        using AudioClip bgmClip = engine.CreateClip("BGM", new float[24000 * 5], 24000, 1);
        using AudioClip clip = engine.CreateClip("Sfx", new float[24000 * 5], 24000, 1);
        using AudioSource bgm = engine.CreateSource(bgmClip);
        using AudioSource source = engine.CreateSource(clip);
        source.Volume = .6f;
        source.Looping = true;
        bgm.Looping = true;
        source.Play();
        bgm.Play();
        Type runtimeType = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Zhengyan.DigitalWife.GamePlayer.dll"))
            .GetType("Zhengyan.DigitalWife.GamePlayer.RuntimeAudio")!;
        object audio = Activator.CreateInstance(runtimeType, BindingFlags.Instance | BindingFlags.NonPublic, null,
            [new Dictionary<string, AudioSource> { ["Sfx"] = source, ["sfx.wav"] = source }, engine], null)!;
        Action<string, float> play = runtimeType.GetMethod("PlayOneShot")!.CreateDelegate<Action<string, float>>(audio);
        play("Sfx", 1);
        play("sfx.wav", .5f);
        var shots = (List<AudioSource>)typeof(AudioEngine).GetField("_oneShotSources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
        Check(shots.Count == 2 && !ReferenceEquals(shots[0], shots[1]) && shots.All(shot => shot.State == Silk.NET.OpenAL.SourceState.Playing),
            "PC script did not create independently playing sources for the same clip.");
        Check(shots.All(shot => !shot.Looping) && source.Looping && bgm.State == Silk.NET.OpenAL.SourceState.Playing, "PC one-shot interrupted or inherited regular playback.");
        Check(Math.Abs(shots[0].Volume - .6f) < .001f && Math.Abs(shots[1].Volume - .3f) < .001f, "PC one-shot volume ignored runtime volume or scale.");
        play("missing", 1);
        Check(shots.Count == 2, "Missing PC resource created an instance.");
        shots[0].Stop();
        engine.Update();
        Check(shots.Count == 1 && shots[0].State == Silk.NET.OpenAL.SourceState.Playing, "PC update did not clean only the finished source.");
        _ = engine.PlayOneShot(bgmClip);
        engine.StopOneShots(clip);
        Check(shots.Count == 1 && ReferenceEquals(shots[0].Clip, bgmClip), "PC clip cleanup affected another resource.");
        engine.StopOneShots();
        Check(shots.Count == 0 && bgm.State == Silk.NET.OpenAL.SourceState.Playing, "PC one-shot cleanup interrupted regular BGM.");
    }

    public static void TestPythonDispatch()
    {
        string root = Path.GetFullPath(Path.Combine("temp", "audio-python-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        Assembly desktop = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Zhengyan.DigitalWife.GamePlayer.dll"));
        Type workerType = desktop.GetType("Zhengyan.DigitalWife.GamePlayer.PythonScriptInstance")!;
        string worker = (string)workerType.GetMethod("BuildWorkerSource", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
        string workerPath = Path.Combine(root, "worker.py"), scriptPath = Path.Combine(root, "audio.py");
        File.WriteAllText(workerPath, worker);
        File.WriteAllText(scriptPath, """
            def start(entity, scene, input, audio):
                audio.play("Sfx")
                audio.play_one_shot("Sfx")
                audio.play_one_shot("sfx.wav", volume_scale=0.5)
            """);
        ProcessStartInfo start = new("python")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string arg in new[] { "-u", workerPath, scriptPath, root, "Windows" }) start.ArgumentList.Add(arg);
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(), errors = process.StandardError.ReadToEndAsync();
        process.StandardInput.WriteLine("{\"event\":\"start\"}");
        process.StandardInput.Close();
        if (!process.WaitForExit(10_000)) { process.Kill(true); throw new TimeoutException("Python audio worker did not finish."); }
        string error = errors.GetAwaiter().GetResult();
        Check(process.ExitCode == 0 && error.Length == 0, "Python one-shot worker failed: " + error);
        string commandLine = output.GetAwaiter().GetResult().Split('\n').Single(line => line.StartsWith("__DW_COMMANDS__"));
        using JsonDocument commands = JsonDocument.Parse(commandLine["__DW_COMMANDS__".Length..]);
        Check(commands.RootElement.GetArrayLength() == 3, "Python one-shot commands were lost.");
        using AudioEngine engine = new();
        using AudioClip clip = engine.CreateClip("Sfx", new float[24000 * 5], 24000, 1);
        using AudioSource source = engine.CreateSource(clip);
        source.Volume = .6f;
        Type runtimeType = desktop.GetType("Zhengyan.DigitalWife.GamePlayer.RuntimeAudio")!;
        object audio = Activator.CreateInstance(runtimeType, BindingFlags.Instance | BindingFlags.NonPublic, null,
            [new Dictionary<string, AudioSource> { ["Sfx"] = source, ["sfx.wav"] = source }, engine], null)!;
        Type commandType = workerType.GetNestedType("PythonCommand", BindingFlags.NonPublic)!;
        MethodInfo apply = workerType.GetMethod("ApplyAudioCommand", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (JsonElement command in commands.RootElement.EnumerateArray())
        {
            object data = JsonSerializer.Deserialize(command.GetRawText(), commandType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            apply.Invoke(null, [data, audio]);
        }
        var shots = (List<AudioSource>)typeof(AudioEngine).GetField("_oneShotSources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
        Check(shots.Count == 2 && shots.All(shot => shot.State == Silk.NET.OpenAL.SourceState.Playing)
            && Math.Abs(shots[0].Volume - .6f) < .001f && Math.Abs(shots[1].Volume - .3f) < .001f,
            "Python command dispatch did not preserve overlapping playback or volume.");
        engine.StopOneShots();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

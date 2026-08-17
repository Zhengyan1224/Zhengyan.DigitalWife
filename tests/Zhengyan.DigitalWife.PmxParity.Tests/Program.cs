using System.Globalization;
using System.Numerics;
using Zhengyan.DigitalWife.Mmd;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.GamePlayer.Runtime;

return args.Length switch
{
    0 => RunSelfTests(),
    2 when args[0] == "--analyze" => AnalyzePmx(args[1]),
    2 => CompareSnapshots(args[0], args[1], 1e-4f),
    3 when float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float tolerance)
        => CompareSnapshots(args[0], args[1], tolerance),
    _ => Usage()
};

static int AnalyzePmx(string path)
{
    PmxParsing? pmx = File.Exists(path) ? PmxParsing.ParsingByFile(path) : null;
    if (pmx is null)
    {
        Console.Error.WriteLine($"Unable to parse PMX: {path}");
        return 2;
    }

    PmxRuntimeFeatureReport report = PmxRuntimeDiagnostics.Analyze(pmx);
    Console.WriteLine($"vertices={report.VertexCount}; faces={report.FaceCount}; materials={report.MaterialCount}; bones={report.BoneCount}; morphs={report.MorphCount}; rigidBodies={report.RigidBodyCount}; joints={report.JointCount}");
    Console.WriteLine("skinning=" + string.Join(", ", report.SkinningTypes.OrderBy(item => item.Key).Select(item => $"{item.Key}:{item.Value}")));
    Console.WriteLine("morphs=" + string.Join(", ", report.MorphTypes.OrderBy(item => item.Key).Select(item => $"{item.Key}:{item.Value}")));
    foreach (string warning in report.Warnings)
    {
        Console.WriteLine("warning: " + warning);
    }
    return 0;
}

static int RunSelfTests()
{
    VmdInterpolationCurve linear = VmdInterpolationCurve.Linear;
    foreach (float time in new[] { 0.0f, 0.1f, 0.25f, 0.5f, 0.9f, 1.0f })
    {
        AssertNear(time, linear.Evaluate(time), 2e-5f, $"linear VMD curve at {time}");
    }

    byte[] interpolation = new byte[64];
    for (int channel = 0; channel < 4; channel++)
    {
        interpolation[channel] = 20;
        interpolation[channel + 4] = 20;
        interpolation[channel + 8] = 107;
        interpolation[channel + 12] = 107;
    }
    AssertNear(0.37f, VmdInterpolationCurve.FromVmd(interpolation, 2).Evaluate(0.37f), 2e-5f, "VMD channel layout");

    Matrix4x4[] globals = [Matrix4x4.Identity, Matrix4x4.CreateTranslation(1.0f, 2.0f, 3.0f)];
    Matrix4x4[] skin = [Matrix4x4.Identity, Matrix4x4.CreateRotationY(0.5f) * globals[1]];
    Dictionary<string, float> morphs = new(StringComparer.Ordinal) { ["smile"] = 0.75f };
    string first = PmxRuntimeDiagnostics.FormatPoseSnapshot(["root", "head"], globals, skin, morphs);
    string second = PmxRuntimeDiagnostics.FormatPoseSnapshot(["root", "head"], globals, skin, morphs);
    if (!string.Equals(first, second, StringComparison.Ordinal) || !first.StartsWith("PMX_POSE_V1", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("PMX pose snapshots are not deterministic.");
    }

    RunRuntimeSceneLifecycleTests();

    Console.WriteLine("PMX/VMD parity self-tests passed.");
    return 0;
}

static void RunRuntimeSceneLifecycleTests()
{
    GameProjectScene scene = new() { Name = "Runtime A" };
    scene.Cameras[0].Camera.ControlMode = "editor";
    scene.Cameras[0].Viewport = new CameraViewportSettings
    {
        Enabled = true,
        LayoutMode = "relative",
        X = 0.0f,
        Y = 0.0f,
        Width = 0.5f,
        Height = 1.0f
    };
    RuntimeScene runtime = new("memory.scene.json", scene, static path => path);
    if (runtime.RenderCameras.Count != 1 || runtime.RenderCameras[0].ResolveViewport(1920, 1080, 1920, 1080).Width != 960)
    {
        throw new InvalidOperationException("Runtime viewport normalization failed.");
    }

    RuntimeEntity point = runtime.AddPointLight("Point", new Vector3(1.0f, 2.0f, 3.0f), Vector3.One, 2.0f, 10.0f);
    RuntimeEntity spot = runtime.AddSpotLight("Spot", Vector3.Zero, Vector3.Zero, Vector3.One);
    if (!point.IsPointLight || !spot.IsSpotLight || runtime.PointLights.Count() != 1 || runtime.SpotLights.Count() != 1)
    {
        throw new InvalidOperationException("Runtime light registry failed.");
    }
    point.LightIntensity = 3.0f;
    if (MathF.Abs(scene.Entities[0].PointLight.Intensity - 3.0f) > 1e-5f || !runtime.RemoveEntity("Spot"))
    {
        throw new InvalidOperationException("Runtime entity mutation failed.");
    }
    runtime.Dispose();

    string directory = Path.Combine(Path.GetTempPath(), "zhengyan-runtime-scene-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        GameProject project = new()
        {
            DefaultScene = "scenes/a.scene.json",
            EditorScene = "scenes/a.scene.json",
            Scenes = ["scenes/a.scene.json", "scenes/b.scene.json"]
        };
        GameProjectStore.SaveScene(directory, project.Scenes[0], new GameProjectScene { Name = "Scene A" });
        GameProjectStore.SaveScene(directory, project.Scenes[1], new GameProjectScene { Name = "Scene B" });
        RuntimeSceneManager manager = new(project, directory);
        int failures = 0;
        manager.SceneLoadFailed += _ => failures++;
        if (!manager.LoadInitial() || manager.Current?.Name != "Scene A")
            throw new InvalidOperationException("Initial runtime scene load failed.");
        manager.RequestSceneChange("scenes/b.scene.json");
        manager.Update(1.0f / 60.0f);
        if (manager.Current?.Name != "Scene B")
            throw new InvalidOperationException("Runtime scene switch failed.");
        manager.RequestSceneChange("scenes/missing.scene.json");
        manager.Update(1.0f / 60.0f);
        if (failures != 1 || manager.Current?.Name != "Scene B")
            throw new InvalidOperationException("Runtime scene load failure recovery failed.");
        manager.Unload();
        if (manager.Current is not null)
            throw new InvalidOperationException("Runtime scene unload failed.");
        manager.Dispose();
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static int CompareSnapshots(string expectedPath, string actualPath, float tolerance)
{
    if (!File.Exists(expectedPath) || !File.Exists(actualPath))
    {
        Console.Error.WriteLine("Both snapshot files must exist.");
        return 2;
    }

    Dictionary<string, float[]> expected = ParseSnapshot(expectedPath);
    Dictionary<string, float[]> actual = ParseSnapshot(actualPath);
    List<string> failures = [];
    foreach ((string key, float[] expectedValues) in expected)
    {
        if (!actual.TryGetValue(key, out float[]? actualValues))
        {
            failures.Add($"Missing: {key}");
            continue;
        }
        if (expectedValues.Length != actualValues.Length)
        {
            failures.Add($"Value count differs: {key}");
            continue;
        }
        float maximumError = expectedValues.Zip(actualValues, (left, right) => MathF.Abs(left - right)).Max();
        if (maximumError > tolerance)
        {
            failures.Add($"{key}: max error {maximumError:R} > {tolerance:R}");
        }
    }
    foreach (string extra in actual.Keys.Except(expected.Keys, StringComparer.Ordinal))
    {
        failures.Add($"Unexpected: {extra}");
    }

    if (failures.Count == 0)
    {
        Console.WriteLine($"Pose snapshots match within tolerance {tolerance:R}.");
        return 0;
    }
    foreach (string failure in failures)
    {
        Console.Error.WriteLine(failure);
    }
    return 1;
}

static Dictionary<string, float[]> ParseSnapshot(string path)
{
    Dictionary<string, float[]> result = new(StringComparer.Ordinal);
    foreach (string line in File.ReadLines(path).Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)))
    {
        string[] columns = line.Split('\t');
        if (columns.Length == 4 && columns[0] == "bone")
        {
            result[$"bone:{columns[1]}:global"] = ParseValues(columns[2]);
            result[$"bone:{columns[1]}:skin"] = ParseValues(columns[3]);
        }
        else if (columns.Length == 3 && columns[0] == "morph")
        {
            result[$"morph:{columns[1]}"] = ParseValues(columns[2]);
        }
    }
    return result;
}

static float[] ParseValues(string value) => value.Split(',')
    .Select(item => float.Parse(item, NumberStyles.Float, CultureInfo.InvariantCulture))
    .ToArray();

static void AssertNear(float expected, float actual, float tolerance, string description)
{
    if (MathF.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"{description}: expected {expected:R}, actual {actual:R}.");
    }
}

static int Usage()
{
    Console.Error.WriteLine("Usage: dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests [--analyze model.pmx | expected.pose actual.pose [tolerance]]");
    return 2;
}

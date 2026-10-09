extern alias MmdDesktop;
using System.Reflection;
using System.Text.Json;
using Veldrid;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Mmd = MmdDesktop::Zhengyan.DigitalWife.Mmd;

internal static class VulkanProjectModelRegression
{
    // Read-only integration probe. No editor save/import operation touches the
    // user's project, and no native window is needed.
    public static void Run(string root)
    {
        HashSet<string> models = new(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "scenes"), "*.json"))
        {
            using JsonDocument scene = JsonDocument.Parse(File.ReadAllText(file));
            foreach (JsonElement entity in scene.RootElement.GetProperty("entities").EnumerateArray())
                if (entity.TryGetProperty("assetPath", out JsonElement asset) && asset.GetString() is { } path &&
                    path.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase) &&
                    (path.Contains("Body/", StringComparison.OrdinalIgnoreCase) || path.Contains("MaidOutfit/", StringComparison.OrdinalIgnoreCase)))
                    models.Add(Path.GetFullPath(Path.Combine(root, path)));
        }
        if (models.Count == 0) throw new Exception("No character model found in the project.");
        using var device = Veldrid.GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions());
        VulkanRenderer renderer = new();
        typeof(VulkanRenderer).GetField("_device", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, device);
        bool oldOpenCl = Mmd.Kernel.UseOpenCL;
        Mmd.Kernel.UseOpenCL = false;
        try
        {
            for (int cycle = 0; cycle < 3; cycle++)
                foreach (string path in models)
                {
                    using Mmd.PmxModel model = new()
                    {
                        SkinningComputeFactory = (vertices, bones) => new VulkanPmxSkinningCompute(renderer, vertices, bones)
                    };
                    if (!model.Load(path, Path.GetDirectoryName(path)!)) throw new Exception("Invalid PMX file.");
                    model.InitializeAnimation();
                    using Mmd.VmdAnimation motion = new();
                    string motionPath = Path.Combine(root, "assets", "motions", "basic_stand.vmd");
                    bool hasMotion = File.Exists(motionPath) && motion.Load(motionPath, model);
                    model.Update();
                    using PmxGpuResources resources = new(new(renderer, default), model);
                    resources.UploadPose(model, true);
                    if (!model.TryBindGpuSkinningOutput(resources.PositionBuffer.NativeResource!, resources.NormalBuffer.NativeResource!, resources.UvBuffer.NativeResource!))
                        throw new Exception("Unable to bind GPU output.");
                    for (int frame = 0; frame < 12; frame++)
                    {
                        model.BeginAnimation();
                        if (hasMotion) motion.Evaluate(frame * 3);
                        model.UpdateMorphAnimation();
                        model.UpdateNodeAnimation(false);
                        model.UpdateNodeAnimation(true);
                        model.EndAnimation();
                        model.InvalidateGpuSkinningOutput();
                        model.Update();
                        if (!model.IsGpuSkinningOutputBound) throw new Exception($"GPU validation failed: {path}, cycle={cycle}, frame={frame}.");
                    }
                    device.WaitForIdle();
                    Console.WriteLine($"PASS Vulkan model lifecycle {cycle + 1}: {Path.GetFileName(path)}; vertices={model.GetVertexCount()}; backend={model.ComputeBackend}");
                }
        }
        finally { device.WaitForIdle(); Mmd.Kernel.UseOpenCL = oldOpenCl; }
    }
}

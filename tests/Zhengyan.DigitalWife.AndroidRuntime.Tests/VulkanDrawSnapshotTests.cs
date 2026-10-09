extern alias MmdDesktop;

using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Veldrid;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Mmd = MmdDesktop::Zhengyan.DigitalWife.Mmd;
using Device = Veldrid.GraphicsDevice;

internal static unsafe class VulkanDrawSnapshotTests
{
    public static void TestPmxPasses()
    {
        if (!VulkanRenderer.IsSupported(out string reason))
        {
            Console.WriteLine($"SKIP native PMX drawing: {reason}");
            return;
        }
        using Device device = Device.CreateVulkan(new GraphicsDeviceOptions());
        ResourceFactory factory = device.ResourceFactory;
        using CommandList commands = factory.CreateCommandList();
        VulkanRenderer renderer = new();
        Set(renderer, "_device", device);
        Set(renderer, "_commandList", commands);
        // Android loads one entity per loading frame: construction is not
        // guaranteed to take place in slot zero.
        Set(renderer, "_frameSlot", 2);
        using TriangleModel model = new();
        using PmxGpuResources resources = new(new(renderer, Vector4.Zero), model);
        resources.PositionBuffer.Update(new ReadOnlySpan<Vector3>(model.GetPositions(), 3));
        resources.NormalBuffer.Update(new ReadOnlySpan<Vector3>(model.GetNormals(), 3));
        resources.UvBuffer.Update(new ReadOnlySpan<Vector2>(model.GetUVs(), 3));
        using VeldridPmxMainPassRenderer main = new(renderer, resources);
        using VeldridPmxAuxiliaryPassRenderer auxiliary = new(renderer, resources);
        Mmd.MMDMaterial material = new() { BothFace = true, Ambient = Vector3.One };
        Mmd.MMDMesh[] meshes = [new(0, 3, material)];
        Dictionary<Mmd.MMDMaterial, MaterialTextures> materials = new()
        {
            [material] = new() { DescriptorSet = resources.CreateMaterialDescriptorSet(null, null, null) }
        };
        using Texture color = Color(factory);
        using Texture depth = Depth(factory);
        using Texture mirrorColor = Color(factory);
        using Texture mirrorDepth = Depth(factory);
        using Framebuffer target = factory.CreateFramebuffer(new(depth, color));
        using Framebuffer mirror = factory.CreateFramebuffer(new(mirrorDepth, mirrorColor));
        using Framebuffer shadow = factory.CreateFramebuffer(new(mirrorDepth));
        Fence[] fences = Enumerable.Range(0, 3).Select(_ => factory.CreateFence(true)).ToArray();
        const int frames = 90;
        Texture[] captures = Enumerable.Range(0, frames * 2).Select(_ => factory.CreateTexture(
            TextureDescription.Texture2D(64, 64, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging))).ToArray();
        try
        {
            for (int frame = 0; frame < frames; frame++)
            {
                int slot = frame % 3;
                device.WaitForFence(fences[slot]);
                Set(renderer, "_frameSlot", slot);
                Set(renderer, "_uniformFrameId", (long)frame);
                Set(renderer, "_frameOpen", true);
                commands.Begin();
                SelectTarget(shadow);
                auxiliary.DrawShadowDepth(resources, meshes, Matrix4x4.Identity, 0);
                float x = (frame & 1) == 0 ? -.5f : .5f;
                SelectTarget(mirror);
                Draw(-x, .4f, new(1, 1, 0));
                SelectTarget(target);
                Draw(x, .2f, new(0, 1, 0));
                object activePass = GetActivePass(commands);
                Draw(x, .8f, new(1, 0, 0)); // Must remain behind the green triangle.
                Draw(-x, .2f, new(0, 0, 1));
                Check(activePass.Equals(GetActivePass(commands)), "PMX uniform uploads interrupted the render pass.");
                // Force a page boundary after earlier snapshots have already
                // been recorded. No earlier camera/material may be overwritten.
                for (int i = 0; i < 600; i++) renderer.FrameUniforms.Upload(new Vector4(i));
                commands.CopyTexture(color, captures[frame * 2]);
                commands.CopyTexture(mirrorColor, captures[frame * 2 + 1]);
                commands.End();
                device.ResetFence(fences[slot]);
                device.SubmitCommands(commands, fences[slot]);
                Set(renderer, "_frameOpen", false);
            }
            device.WaitForIdle();
            for (int frame = 0; frame < frames; frame++)
            {
                int x = (frame & 1) == 0 ? 16 : 48;
                CheckPixel(captures[frame * 2], x, new(0, 255, 0), frame, "near model/depth");
                CheckPixel(captures[frame * 2], 64 - x, new(0, 0, 255), frame, "second model");
                CheckPixel(captures[frame * 2 + 1], 64 - x, new(255, 255, 0), frame, "mirror camera/material");
            }
        }
        finally
        {
            device.WaitForIdle();
            foreach (Fence fence in fences) fence.Dispose();
            foreach (Texture capture in captures) capture.Dispose();
            renderer.FrameUniforms.Dispose();
        }

        void SelectTarget(Framebuffer framebuffer)
        {
            commands.SetFramebuffer(framebuffer);
            typeof(VulkanRenderer).GetProperty("CurrentOutputDescription", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(renderer, framebuffer.OutputDescription);
            if (framebuffer.ColorTargets.Count > 0) commands.ClearColorTarget(0, RgbaFloat.Black);
            commands.ClearDepthStencil(1);
            commands.SetFullViewports();
            commands.SetFullScissorRects();
        }

        void Draw(float x, float z, Vector3 tint)
        {
            material.Diffuse = tint;
            int count = main.Draw(resources, meshes, materials, Matrix4x4.CreateTranslation(x, 0, z),
                Matrix4x4.Identity, Matrix4x4.Identity, Vector3.Zero, -Vector3.UnitZ, Vector3.One, 1,
                [], [], false, "smooth", null, null, null);
            Check(count == 1, "The PMX draw was skipped.");
        }

        void CheckPixel(Texture texture, int x, Vector3 expected, int frame, string label)
        {
            MappedResource pixels = device.Map(texture, MapMode.Read);
            try
            {
                byte* pixel = (byte*)pixels.Data + 32 * pixels.RowPitch + x * 4;
                Vector3 actual = new(pixel[0], pixel[1], pixel[2]);
                Check(Vector3.Distance(actual, expected) < 8, $"Frame {frame} {label}: expected {expected}, got {actual}.");
            }
            finally { device.Unmap(texture); }
        }
    }

    private static Texture Color(ResourceFactory factory) => factory.CreateTexture(TextureDescription.Texture2D(
        64, 64, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
    private static Texture Depth(ResourceFactory factory) => factory.CreateTexture(TextureDescription.Texture2D(
        64, 64, 1, 1, PixelFormat.R32_Float, TextureUsage.DepthStencil));
    private static void Set(VulkanRenderer renderer, string name, object value)
        => typeof(VulkanRenderer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, value);
    private static object GetActivePass(CommandList commands)
        => commands.GetType().GetField("_activeRenderPass", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(commands)!;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // Small backend-independent geometry fixture; all GPU objects and PMX
    // material/shadow passes above are the real engine implementations.
    private sealed class TriangleModel : Mmd.MMDModel
    {
        private readonly Vector3* _positions = (Vector3*)NativeMemory.Alloc(36);
        private readonly Vector3* _normals = (Vector3*)NativeMemory.Alloc(36);
        private readonly Vector2* _uvs = (Vector2*)NativeMemory.AllocZeroed(24);
        private readonly uint* _indices = (uint*)NativeMemory.Alloc(12);
        public TriangleModel()
        {
            _positions[0] = new(-.3f, -.4f, 0);
            _positions[1] = new(.3f, -.4f, 0);
            _positions[2] = new(0, .4f, 0);
            for (int i = 0; i < 3; i++) { _normals[i] = Vector3.UnitZ; _indices[i] = (uint)i; }
        }
        public override bool HasUvMorphs => false;
        public override int GetVertexCount() => 3;
        public override int GetIndexCount() => 3;
        public override Vector3* GetPositions() => _positions;
        public override Vector3* GetNormals() => _normals;
        public override Vector2* GetUVs() => _uvs;
        public override Vector3* GetUpdatePositions() => _positions;
        public override Vector3* GetUpdateNormals() => _normals;
        public override Vector2* GetUpdateUVs() => _uvs;
        public override uint* GetIndices() => _indices;
        public override Mmd.MMDNode[] GetNodes() => [];
        public override Mmd.MMDMorph[] GetMorphs() => [];
        public override Mmd.MMDIkSolver[] GetIkSolvers() => [];
        public override Mmd.MMDMaterial[] GetMaterials() => [];
        public override Mmd.MMDMesh[] GetMeshes() => [];
        public override Mmd.MMDNode? FindNode(Predicate<Mmd.MMDNode> predicate) => null;
        public override Mmd.MMDMorph? FindMorph(Predicate<Mmd.MMDMorph> predicate) => null;
        public override Mmd.MMDIkSolver? FindIkSolver(Predicate<Mmd.MMDIkSolver> predicate) => null;
        public override bool Load(string path, string mmdDataDir) => throw new NotSupportedException();
        public override void InitializeAnimation() { }
        public override void BeginAnimation() { }
        public override void EndAnimation() { }
        public override void UpdateMorphAnimation() { }
        public override void UpdateNodeAnimation(bool afterPhysicsAnim) { }
        public override void ResetPhysics() { }
        public override void UpdatePhysicsAnimation(float elapsed) { }
        public override void Update() { }
        public override void Destroy() { }
        public override void Dispose()
        {
            NativeMemory.Free(_positions); NativeMemory.Free(_normals); NativeMemory.Free(_uvs); NativeMemory.Free(_indices);
        }
    }
}

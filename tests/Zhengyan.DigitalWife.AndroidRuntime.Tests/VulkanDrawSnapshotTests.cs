extern alias MmdDesktop;

using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Veldrid;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Zhengyan.DigitalWife.GamePlayer.Android;
using Mmd = MmdDesktop::Zhengyan.DigitalWife.Mmd;
using Device = Veldrid.GraphicsDevice;

internal static unsafe class VulkanDrawSnapshotTests
{
    public static void TestShadowFilters()
    {
        if (!VulkanRenderer.IsSupported(out string reason)) { Console.WriteLine($"SKIP shadow filtering: {reason}"); return; }
        using Device device = Device.CreateVulkan(new GraphicsDeviceOptions());
        ResourceFactory factory = device.ResourceFactory;
        using CommandList commands = factory.CreateCommandList();
        VulkanRenderer renderer = new();
        Set(renderer, "_device", device); Set(renderer, "_commandList", commands);
        using TriangleModel model = new();
        using PmxGpuResources resources = new(new(renderer, Vector4.Zero), model);
        resources.PositionBuffer.Update(new ReadOnlySpan<Vector3>(model.GetPositions(), 3));
        resources.NormalBuffer.Update(new ReadOnlySpan<Vector3>(model.GetNormals(), 3));
        resources.UvBuffer.Update(new ReadOnlySpan<Vector2>(model.GetUVs(), 3));
        using VeldridPmxMainPassRenderer main = new(renderer, resources);
        Mmd.MMDMaterial material = new() { BothFace = true, Diffuse = Vector3.One, Ambient = Vector3.Zero };
        Mmd.MMDMesh[] meshes = [new(0, 3, material)];
        Dictionary<Mmd.MMDMaterial, MaterialTextures> materials = new()
        { [material] = new() { DescriptorSet = resources.CreateMaterialDescriptorSet(null, null, null) } };
        using Texture map = factory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R32_Float, TextureUsage.Sampled));
        using TextureView mapView = factory.CreateTextureView(map);
        using Sampler sampler = factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp,SamplerAddressMode.Clamp,SamplerAddressMode.Clamp,
            SamplerFilter.MinPoint_MagPoint_MipPoint,null,0,0,uint.MaxValue,0,SamplerBorderColor.OpaqueWhite));
        float[] depths = Enumerable.Range(0, 16).Select(i => i % 4 < 2 ? .25f : .75f).ToArray();
        device.UpdateTexture(map, depths, 0, 0, 0, 4, 4, 1, 0, 0);
        using Texture color = factory.CreateTexture(TextureDescription.Texture2D(128, 128, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        using Texture depth = factory.CreateTexture(TextureDescription.Texture2D(128, 128, 1, 1, PixelFormat.R32_Float, TextureUsage.DepthStencil));
        using Texture capture = factory.CreateTexture(TextureDescription.Texture2D(128, 128, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));
        using Framebuffer target = factory.CreateFramebuffer(new(depth, color));
        RuntimeTextureHandle texture = new(Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsBackend.Vulkan, 0, mapView);
        ShadowMapBinding directional = new(texture, Matrix4x4.Identity, 0, 100, 1, 0)
        { NativeSampler = sampler, TexelSize = new(.25f), NormalOffset = 0 };
        LocalLightShadowBinding local = new()
        {
            Texture = texture, NativeSampler = sampler, Strength = 1, Bias = 0, NormalOffset = 0, TexelSize = new(.25f),
            PointLights = [new(0, .1f, 100, Enumerable.Repeat(Matrix4x4.Identity, 6).ToArray(), Enumerable.Repeat(new Vector4(0,0,1,1),6).ToArray())],
            SpotLights = []
        };
        PointLightData[] points = [new(new(0, 0, 2), Vector3.One, 1, 100, CastShadows: true)];
        long frame = 0;
        try
        {
            foreach (bool point in new[] { false, true })
            foreach (bool toon in new[] { false, true })
            {
                Set(renderer, "_uniformFrameId", frame++); Set(renderer, "_frameOpen", true);
                commands.Begin(); commands.SetFramebuffer(target);
                typeof(VulkanRenderer).GetProperty("CurrentOutputDescription", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer,target.OutputDescription);
                commands.ClearColorTarget(0,RgbaFloat.Black); commands.ClearDepthStencil(1);
                commands.SetFullViewports(); commands.SetFullScissorRects();
                main.Draw(resources,meshes,materials,Matrix4x4.CreateScale(3,3,1)*Matrix4x4.CreateTranslation(0,0,.5f),
                    Matrix4x4.Identity,Matrix4x4.Identity,point?Vector3.Zero:Vector3.One,-Vector3.UnitZ,Vector3.Zero,0,
                    point?points:[],[],true,toon?"toon":"smooth",point?null:directional,point?local:null,null);
                commands.CopyTexture(color,capture); commands.End(); device.SubmitCommands(commands); device.WaitForIdle();
                Set(renderer,"_frameOpen",false);
                MappedResource pixels=device.Map(capture,MapMode.Read);
                try
                {
                    HashSet<byte> levels=[];
                    for(int x=48;x<80;x++)
                    {
                        float worldX=(x+.5f)/128*2-1, uv=worldX*.5f+.5f;
                        float visibility=point ? (Linear(uv-.125f)+Linear(uv+.125f))*.5f
                            : (Linear(uv-.25f)+Linear(uv)+Linear(uv+.25f))/3;
                        if(toon) visibility=visibility>=.5f?1:0;
                        float distance=MathF.Sqrt(worldX*worldX+1.5f*1.5f+MathF.Pow(1f/128,2));
                        float radiance=point?MathF.Pow(1-distance/100,2)*1.5f/distance:1;
                        byte actual=*((byte*)pixels.Data+64*pixels.RowPitch+x*4);
                        float expected=visibility*radiance*255;
                        Check(MathF.Abs(actual-expected)<3,$"Shadow filtering point={point}, toon={toon}, x={x}: expected {expected}, got {actual}.");
                        levels.Add(actual);
                    }
                    if(!toon) Check(levels.Count>12,"Shadow coverage still has quantized nearest-neighbour bands.");
                    Console.WriteLine($"PASS shadow filter point={point}, toon={toon}, distinct levels={levels.Count}");
                }
                finally { device.Unmap(capture); }
            }
        }
        finally { device.WaitForIdle(); renderer.FrameUniforms.Dispose(); }

        static float Linear(float uv)
        {
            float pixel=Math.Clamp(uv,.25f,.75f)*4-.5f;
            int lower=(int)MathF.Floor(pixel); float fraction=pixel-lower;
            return (lower>=2?1-fraction:0)+(lower+1>=2?fraction:0);
        }
    }

    public static void TestPmxEdgeWidths()
    {
        Check(AndroidRenderPolicy.ResolvePmxEdgeWidthScale(720, 1280, 720, 1280) == 2,
            "Android did not apply the visually calibrated width at the reference resolution.");
        Check(AndroidRenderPolicy.ResolvePmxEdgeWidthScale(1080, 2400, 720, 1280) == 3,
            "A tall phone stretched the outline with its aspect ratio.");
        Check(AndroidRenderPolicy.ResolvePmxEdgeWidthScale(1980, 720, 480, 320) == 4.5f,
            "A wide phone stretched the outline with its aspect ratio.");
        Check(AndroidRenderPolicy.ResolvePmxEdgeWidthScale(360, 640, 720, 1280) == 1,
            "A smaller output did not downscale the outline.");
        string fixtureDirectory = Path.GetFullPath(Path.Combine("temp", "pmx-edge-tests"));
        Directory.CreateDirectory(fixtureDirectory);
        string fixturePath = Path.Combine(fixtureDirectory, "unused.pmx");
        File.WriteAllBytes(fixturePath, []);
        using PmxModelComponent component = new(fixturePath);
        Check(component.EdgeWidthScale == 1, "Desktop and offscreen outlines must default to PMX pixel widths.");
        if (!VulkanRenderer.IsSupported(out string reason))
        {
            Console.WriteLine($"SKIP native edge rendering: {reason}");
            return;
        }
        using Device device = Device.CreateVulkan(new GraphicsDeviceOptions());
        ResourceFactory factory = device.ResourceFactory;
        using CommandList commands = factory.CreateCommandList();
        VulkanRenderer renderer = new();
        Set(renderer, "_device", device);
        Set(renderer, "_commandList", commands);
        using TriangleModel model = new(edgeNormals: true);
        using PmxGpuResources resources = new(new(renderer, Vector4.Zero), model);
        resources.PositionBuffer.Update(new ReadOnlySpan<Vector3>(model.GetPositions(), 3));
        resources.NormalBuffer.Update(new ReadOnlySpan<Vector3>(model.GetNormals(), 3));
        using VeldridPmxAuxiliaryPassRenderer auxiliary = new(renderer, resources);
        Mmd.MMDMaterial material = new() { EdgeFlag = 1, EdgeColor = Vector4.One };
        Mmd.MMDMesh[] meshes = [new(0, 6, material)];
        long frame = 0;
        try
        {
            int desktop = Expansion(64, 1);
            int unscaledPhone = Expansion(128, 1);
            int previousPhone = Expansion(128, 2);
            float scale = AndroidRenderPolicy.ResolvePmxEdgeWidthScale(128, 128, 64, 64);
            int phone = Expansion(128, scale);
            int splitViewport = Expansion(64, scale);
            Console.WriteLine($"PMX edge raster widths: desktop={desktop}, unscaled phone={unscaledPhone}, previous phone={previousPhone}, calibrated phone={phone}, split viewport={splitViewport}");
            Check(desktop > 0 && phone >= previousPhone + 2 && Math.Abs(phone - previousPhone * 2) <= 2,
                $"Calibrated outline did not double the previous Android width: previous={previousPhone}, phone={phone}.");
            Check(phone >= unscaledPhone + 2, "The phone still renders the original thin physical-pixel outline.");
            Check(Math.Abs(splitViewport - phone) <= 2, "A split-screen camera changed the display's pixel scale.");
        }
        finally
        {
            device.WaitForIdle();
            renderer.FrameUniforms.Dispose();
        }

        int Expansion(uint size, float scale) => Width(size, scale, 4) - Width(size, scale, 0);

        int Width(uint size, float scale, float edgeSize)
        {
            using Texture color = factory.CreateTexture(TextureDescription.Texture2D(
                size, size, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
            using Texture depth = factory.CreateTexture(TextureDescription.Texture2D(
                size, size, 1, 1, PixelFormat.R32_Float, TextureUsage.DepthStencil));
            using Texture capture = factory.CreateTexture(TextureDescription.Texture2D(
                size, size, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));
            using Framebuffer target = factory.CreateFramebuffer(new(depth, color));
            Set(renderer, "_uniformFrameId", frame++);
            Set(renderer, "_frameOpen", true);
            commands.Begin();
            commands.SetFramebuffer(target);
            typeof(VulkanRenderer).GetProperty("CurrentOutputDescription", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(renderer, target.OutputDescription);
            commands.ClearColorTarget(0, RgbaFloat.Black);
            commands.ClearDepthStencil(1);
            commands.SetFullViewports();
            commands.SetFullScissorRects();
            material.EdgeSize = edgeSize;
            auxiliary.DrawEdge(resources, meshes, Matrix4x4.Identity, Matrix4x4.Identity,
                Matrix4x4.Identity, new Vector2(size, size) / scale);
            commands.CopyTexture(color, capture);
            commands.End();
            device.SubmitCommands(commands);
            device.WaitForIdle();
            Set(renderer, "_frameOpen", false);
            MappedResource pixels = device.Map(capture, MapMode.Read);
            try
            {
                byte* row = (byte*)pixels.Data + (size / 2) * pixels.RowPitch;
                int count = 0;
                for (int x = 0; x < size; x++) if (row[x * 4] > 200) count++;
                return count;
            }
            finally { device.Unmap(capture); }
        }
    }

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
        private readonly uint* _indices = (uint*)NativeMemory.Alloc(24);
        private readonly bool _edgeNormals;
        public TriangleModel(bool edgeNormals = false)
        {
            _edgeNormals = edgeNormals;
            _positions[0] = new(-.3f, -.4f, 0);
            _positions[1] = new(.3f, -.4f, 0);
            _positions[2] = new(0, .4f, 0);
            for (int i = 0; i < 3; i++)
            {
                _normals[i] = edgeNormals ? Vector3.Normalize(_positions[i]) : Vector3.UnitZ;
                _indices[i] = (uint)i;
                _indices[i + 3] = (uint)(2 - i);
            }
        }
        public override bool HasUvMorphs => false;
        public override int GetVertexCount() => 3;
        public override int GetIndexCount() => _edgeNormals ? 6 : 3;
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

using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Silk.NET.OpenGLES;
using Veldrid;
using Veldrid.SPIRV;
using EngineGraphicsDevice = Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsDevice;
using VeldridSampler = Veldrid.Sampler;
using VeldridShader = Veldrid.Shader;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx;

/// <summary>Backend-neutral entry point for the PMX main material pass.</summary>
internal interface IPmxMainPassRenderer : IDisposable
{
    int Draw(
        PmxGpuResources resources,
        IReadOnlyList<Zhengyan.DigitalWife.Mmd.MMDMesh> meshes,
        IReadOnlyDictionary<Zhengyan.DigitalWife.Mmd.MMDMaterial, MaterialTextures> materials,
        Matrix4x4 world,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector3 lightColor,
        Vector3 lightDirection,
        Vector3 ambientLightColor,
        float ambientLightStrength,
        bool enableShadow,
        ShadowMapBinding? shadowMap,
        Func<int, RuntimeTextureHandle?>? resolveTextureOverride,
        int materialIndexOffset = 0);
}

internal static class PmxMainPassRendererFactory
{
    public static IPmxMainPassRenderer Create(EngineGraphicsDevice graphicsDevice, PmxGpuResources resources)
    {
        return graphicsDevice.Renderer switch
        {
            OpenGlRenderer openGl => new OpenGlPmxMainPassRenderer(openGl.Gl, resources),
            VulkanRenderer vulkan => new VeldridPmxMainPassRenderer(vulkan, resources),
            _ => throw new NotSupportedException($"PMX main pass is not implemented for {graphicsDevice.Backend}.")
        };
    }
}

/// <summary>OpenGL compatibility implementation behind the same PMX pass contract.</summary>
internal sealed unsafe class OpenGlPmxMainPassRenderer : IPmxMainPassRenderer
{
    private readonly GL _gl;
    private readonly PmxShader _shader;
    private readonly uint _vao;
    private bool _disposed;

    public OpenGlPmxMainPassRenderer(GL gl, PmxGpuResources resources)
    {
        _gl = gl;
        _shader = new PmxShader(gl);
        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(GLEnum.ArrayBuffer, resources.PositionBuffer.LegacyBufferId);
        gl.VertexAttribPointer(_shader.InPos, 3, GLEnum.Float, false, (uint)sizeof(Vector3), (void*)0);
        gl.EnableVertexAttribArray(_shader.InPos);
        gl.BindBuffer(GLEnum.ArrayBuffer, resources.NormalBuffer.LegacyBufferId);
        gl.VertexAttribPointer(_shader.InNor, 3, GLEnum.Float, false, (uint)sizeof(Vector3), (void*)0);
        gl.EnableVertexAttribArray(_shader.InNor);
        gl.BindBuffer(GLEnum.ArrayBuffer, resources.UvBuffer.LegacyBufferId);
        gl.VertexAttribPointer(_shader.InUV, 2, GLEnum.Float, false, (uint)sizeof(Vector2), (void*)0);
        gl.EnableVertexAttribArray(_shader.InUV);
        gl.BindBuffer(GLEnum.ElementArrayBuffer, resources.IndexBuffer.LegacyBufferId);
        gl.BindVertexArray(0);
    }

    public int Draw(
        PmxGpuResources resources,
        IReadOnlyList<Zhengyan.DigitalWife.Mmd.MMDMesh> meshes,
        IReadOnlyDictionary<Zhengyan.DigitalWife.Mmd.MMDMaterial, MaterialTextures> materials,
        Matrix4x4 world,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector3 lightColor,
        Vector3 lightDirection,
        Vector3 ambientLightColor,
        float ambientLightStrength,
        bool enableShadow,
        ShadowMapBinding? shadowMap,
        Func<int, RuntimeTextureHandle?>? resolveTextureOverride,
        int materialIndexOffset = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Matrix4x4 worldView = world * view;
        Matrix4x4 worldViewProjection = worldView * projection;
        Vector3 viewSpaceLightDirection = Vector3.Normalize(Vector3.TransformNormal(lightDirection, view));
        resources.UploadFrameUniforms(new PmxGpuResources.PmxFrameUniformData
        {
            World = world,
            View = view,
            Projection = projection,
            WorldViewProjection = worldViewProjection,
            LightColor = new Vector4(lightColor, 1.0f),
            LightDirection = new Vector4(viewSpaceLightDirection, 0.0f),
            AmbientLightColor = new Vector4(ambientLightColor, 1.0f),
            Parameters = new Vector4(ambientLightStrength, enableShadow ? 1.0f : 0.0f, 0.0f, 0.0f)
        });

        _gl.Enable(GLEnum.DepthTest);
        _gl.Enable(GLEnum.Blend);
        _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);
        _gl.DepthMask(true);
        _gl.UseProgram(_shader.Id);
        _gl.BindVertexArray(_vao);
        _gl.SetUniform(_shader.UniWVP, worldViewProjection);
        _gl.SetUniform(_shader.UniWV, worldView);
        _gl.SetUniform(_shader.UniTex, 0);
        _gl.SetUniform(_shader.UniSphereTex, 1);
        _gl.SetUniform(_shader.UniToonTex, 2);
        _gl.SetUniform(_shader.UniLightColor, lightColor);
        _gl.SetUniform(_shader.UniLightDir, viewSpaceLightDirection);
        _gl.SetUniform(_shader.UniAmbientLightColor, ambientLightColor);
        _gl.SetUniform(_shader.UniAmbientLightStrength, ambientLightStrength);
        _gl.SetUniform(_shader.UniShadowMap0, 3);
        _gl.SetUniform(_shader.UniShadowMap1, 4);
        _gl.SetUniform(_shader.UniShadowMap2, 5);
        _gl.SetUniform(_shader.UniShadowMap3, 6);
        ApplyShadowMap(world, enableShadow ? shadowMap : null);

        int drawCount = 0;
        foreach (Zhengyan.DigitalWife.Mmd.MMDMesh mesh in meshes)
        {
            Zhengyan.DigitalWife.Mmd.MMDMaterial material = mesh.Material;
            if (!materials.TryGetValue(material, out MaterialTextures? textures) || material.Alpha <= 0.0f)
            {
                continue;
            }

            int materialIndex = materialIndexOffset + GetMaterialIndex(materials, material);
            DrawMaterial(resources, mesh, material, textures, materialIndex, resolveTextureOverride);
            drawCount++;
        }

        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        return drawCount;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gl.DeleteVertexArray(_vao);
        _shader.Dispose();
        GC.SuppressFinalize(this);
    }

    private void DrawMaterial(
        PmxGpuResources resources,
        Zhengyan.DigitalWife.Mmd.MMDMesh mesh,
        Zhengyan.DigitalWife.Mmd.MMDMaterial material,
        MaterialTextures textures,
        int materialIndex,
        Func<int, RuntimeTextureHandle?>? resolveTextureOverride)
    {
        float textureMode = GetTextureMode(textures.Texture);
        resources.UploadMaterialUniforms(new PmxGpuResources.PmxMaterialUniformData
        {
            Ambient = new Vector4(material.Ambient, 1.0f),
            Diffuse = new Vector4(material.Diffuse, material.Alpha),
            Specular = new Vector4(material.Specular, material.SpecularPower),
            TextureMultiply = material.TextureMulFactor,
            TextureAdd = material.TextureAddFactor,
            SphereMultiply = material.SpTextureMulFactor,
            SphereAdd = material.SpTextureAddFactor,
            ToonMultiply = material.ToonTextureMulFactor,
            ToonAdd = material.ToonTextureAddFactor,
            Modes = new Vector4(textureMode, GetSphereTextureMode(material, textures), textures.ToonTexture is null ? 0.0f : 1.0f, materialIndex)
        });

        if (textures.DescriptorSet is not null)
        {
            BindDescriptorSet(textures.DescriptorSet);
        }

        _gl.SetUniform(_shader.UniAmbient, material.Ambient);
        _gl.SetUniform(_shader.UniDiffuse, material.Diffuse);
        _gl.SetUniform(_shader.UniSpecular, material.Specular);
        _gl.SetUniform(_shader.UniSpecularPower, material.SpecularPower);
        _gl.SetUniform(_shader.UniAlpha, material.Alpha);

        _gl.ActiveTexture(TextureUnit.Texture0);
        uint overrideTextureId = resolveTextureOverride?.Invoke(materialIndex)?.LegacyTextureId ?? 0;
        if (overrideTextureId != 0)
        {
            _gl.SetUniform(_shader.UniTexMode, 1);
            _gl.BindTexture(GLEnum.Texture2D, overrideTextureId);
        }
        else
        {
            _gl.SetUniform(_shader.UniTexMode, (int)textureMode);
        }

        _gl.SetUniform(_shader.UniTexMulFactor, material.TextureMulFactor);
        _gl.SetUniform(_shader.UniTexAddFactor, material.TextureAddFactor);
        _gl.SetUniform(_shader.UniSphereTexMode, (int)GetSphereTextureMode(material, textures));
        _gl.SetUniform(_shader.UniSphereTexMulFactor, material.SpTextureMulFactor);
        _gl.SetUniform(_shader.UniSphereTexAddFactor, material.SpTextureAddFactor);
        _gl.SetUniform(_shader.UniToonTexMode, textures.ToonTexture is null ? 0 : 1);
        _gl.SetUniform(_shader.UniToonTexMulFactor, material.ToonTextureMulFactor);
        _gl.SetUniform(_shader.UniToonTexAddFactor, material.ToonTextureAddFactor);

        if (material.BothFace)
        {
            _gl.Disable(GLEnum.CullFace);
        }
        else
        {
            _gl.Enable(GLEnum.CullFace);
            _gl.CullFace(GLEnum.Back);
        }

        _gl.DrawElements(GLEnum.Triangles, mesh.VertexCount, GLEnum.UnsignedInt, (void*)(mesh.BeginIndex * sizeof(uint)));
    }

    private void BindDescriptorSet(PmxMaterialDescriptorSet descriptorSet)
    {
        foreach (PmxTextureDescriptor binding in descriptorSet.Bindings)
        {
            _gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + binding.Binding));
            _gl.BindTexture(GLEnum.Texture2D, binding.Texture.LegacyTextureId);
            if (binding.Sampler.LegacySamplerId != 0)
            {
                _gl.BindSampler(binding.Binding, binding.Sampler.LegacySamplerId);
            }
        }
    }

    private void ApplyShadowMap(Matrix4x4 world, ShadowMapBinding? shadowMap)
    {
        if (shadowMap is not { TextureId: not 0 } binding)
        {
            _gl.SetUniform(_shader.UniShadowMapEnabled, 0);
            return;
        }

        Matrix4x4 lightWvp = world * binding.LightViewProjection;
        _gl.SetUniform(_shader.UniShadowMapEnabled, 1);
        _gl.SetUniform(_shader.UniShadowMapStrength, Math.Clamp(binding.Strength, 0.0f, 1.0f));
        _gl.SetUniform(_shader.UniShadowMapBias, Math.Max(0.0f, binding.Bias));
        _gl.SetUniform(_shader.UniLightWvp0, lightWvp);
        _gl.SetUniform(_shader.UniLightWvp1, lightWvp);
        _gl.SetUniform(_shader.UniLightWvp2, lightWvp);
        _gl.SetUniform(_shader.UniLightWvp3, lightWvp);

        Span<float> splits = stackalloc float[5];
        splits[0] = Math.Max(0.0f, binding.NearDistance);
        splits[1] = Math.Max(splits[0] + 0.001f, binding.FarDistance);
        splits[2] = splits[1] + 0.001f;
        splits[3] = splits[2] + 0.001f;
        splits[4] = splits[3] + 0.001f;
        fixed (float* splitPointer = splits)
        {
            _gl.Uniform1(_shader.UniShadowMapSplitPosition0, 5, splitPointer);
        }

        for (int unit = 3; unit <= 6; unit++)
        {
            _gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + unit));
            _gl.BindTexture(GLEnum.Texture2D, binding.TextureId);
        }
    }

    private static float GetTextureMode(ITexture2D? texture)
    {
        return texture is null
            ? 0.0f
            : texture.AlphaMode switch
            {
                TextureAlphaMode.Blend => 2.0f,
                TextureAlphaMode.ColorMask => 3.0f,
                TextureAlphaMode.BlendMaskColor => 4.0f,
                _ => 1.0f
            };
    }

    private static float GetSphereTextureMode(
        Zhengyan.DigitalWife.Mmd.MMDMaterial material,
        MaterialTextures textures)
    {
        if (textures.SphereTexture is null) return 0.0f;
        return material.SpTextureMode switch
        {
            Zhengyan.DigitalWife.Mmd.SphereTextureMode.Mul => 1.0f,
            Zhengyan.DigitalWife.Mmd.SphereTextureMode.Add => 2.0f,
            _ => 0.0f
        };
    }

    private static int GetMaterialIndex(
        IReadOnlyDictionary<Zhengyan.DigitalWife.Mmd.MMDMaterial, MaterialTextures> materials,
        Zhengyan.DigitalWife.Mmd.MMDMaterial material)
    {
        int index = 0;
        foreach (Zhengyan.DigitalWife.Mmd.MMDMaterial candidate in materials.Keys)
        {
            if (ReferenceEquals(candidate, material)) return index;
            index++;
        }

        return -1;
    }
}

/// <summary>
/// Vulkan implementation of the PMX main material pass. Edge and shadow passes
/// are handled by the separate PMX auxiliary-pass renderer; custom user shaders
/// retain their own contract and lifecycle.
/// </summary>
internal sealed class VeldridPmxMainPassRenderer : IPmxMainPassRenderer
{
    private readonly VulkanRenderer _renderer;
    private readonly ResourceLayout _frameLayout;
    private readonly ResourceLayout _materialLayout;
    private readonly Dictionary<FrameSetKey, ResourceSet> _frameSets = [];
    private readonly FrameSetKey _fallbackFrameSetKey;
    private readonly ShaderSetDescription _shaderSet;
    private readonly VeldridShader[] _shaders;
    private readonly Dictionary<MaterialSetKey, ResourceSet> _materialSets = [];
    private readonly List<PipelineBundle> _pipelineBundles = [];
    private bool _disposed;

    public VeldridPmxMainPassRenderer(VulkanRenderer renderer, PmxGpuResources resources)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        ArgumentNullException.ThrowIfNull(resources);

        ResourceFactory factory = renderer.ResourceFactory;
        _frameLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("PmxFrame", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PmxShadowMap", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PmxShadowSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        _materialLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("PmxMaterial", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PmxBaseTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PmxBaseSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PmxSphereTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PmxSphereSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PmxToonTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PmxToonSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        TextureView fallbackTexture = RequireTextureView(resources.DefaultTexture);
        VeldridSampler fallbackSampler = RequireSampler(resources.TextureSampler);
        _fallbackFrameSetKey = new FrameSetKey(fallbackTexture, fallbackSampler);
        _frameSets[_fallbackFrameSetKey] = factory.CreateResourceSet(new ResourceSetDescription(
            _frameLayout,
            RequireDeviceBuffer(resources.FrameUniformBuffer),
            fallbackTexture,
            fallbackSampler));

        ShaderDescription vertexShader = VulkanShaderCompiler.CompileSource(
            "pmx_main.vert", VertexShaderSource, ShaderStages.Vertex);
        ShaderDescription fragmentShader = VulkanShaderCompiler.CompileSource(
            "pmx_main.frag", FragmentShaderSource, ShaderStages.Fragment);
        _shaders = factory.CreateFromSpirv(vertexShader, fragmentShader);

        VertexLayoutDescription[] vertexLayouts =
        [
            new VertexLayoutDescription(new VertexElementDescription(
                "Position", VertexElementSemantic.Position, VertexElementFormat.Float3)),
            new VertexLayoutDescription(new VertexElementDescription(
                "Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3)),
            new VertexLayoutDescription(new VertexElementDescription(
                "TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2))
        ];
        _shaderSet = new ShaderSetDescription(vertexLayouts, _shaders);
        _ = GetPipelineBundle(renderer.Device.SwapchainFramebuffer.OutputDescription);
    }

    public int Draw(
        PmxGpuResources resources,
        IReadOnlyList<Zhengyan.DigitalWife.Mmd.MMDMesh> meshes,
        IReadOnlyDictionary<Zhengyan.DigitalWife.Mmd.MMDMaterial, MaterialTextures> materials,
        Matrix4x4 world,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector3 lightColor,
        Vector3 lightDirection,
        Vector3 ambientLightColor,
        float ambientLightStrength,
        bool enableShadow,
        ShadowMapBinding? shadowMap,
        Func<int, RuntimeTextureHandle?>? resolveTextureOverride,
        int materialIndexOffset = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_renderer.IsFrameOpen || meshes.Count == 0)
        {
            return 0;
        }

        Matrix4x4 worldView = world * view;
        Matrix4x4 worldViewProjection = worldView * projection;
        Vector3 viewSpaceLightDirection = Vector3.Normalize(Vector3.TransformNormal(lightDirection, view));
        TextureView? shadowTexture = shadowMap?.NativeTexture as TextureView;
        VeldridSampler? shadowSampler = shadowMap?.NativeSampler as VeldridSampler;
        bool shadowAvailable = enableShadow && shadowTexture is not null && shadowSampler is not null;
        Matrix4x4 shadowLightViewProjection = shadowAvailable
            ? world * shadowMap!.Value.LightViewProjection
            : Matrix4x4.Identity;
        PmxGpuResources.PmxFrameUniformData frameData = new()
        {
            World = world,
            View = view,
            Projection = projection,
            WorldViewProjection = worldViewProjection,
            LightColor = new Vector4(lightColor, 1.0f),
            LightDirection = new Vector4(viewSpaceLightDirection, 0.0f),
            AmbientLightColor = new Vector4(ambientLightColor, 1.0f),
            Parameters = new Vector4(ambientLightStrength, enableShadow ? 1.0f : 0.0f, 0.0f, 0.0f),
            ShadowLightViewProjection = shadowLightViewProjection,
            ShadowParameters = new Vector4(
                shadowAvailable ? 1.0f : 0.0f,
                shadowAvailable ? Math.Clamp(shadowMap!.Value.Strength, 0.0f, 1.0f) : 0.0f,
                shadowAvailable ? Math.Max(0.0f, shadowMap!.Value.Bias) : 0.0f,
                0.0f)
        };

        CommandList commands = _renderer.CommandList;
        commands.UpdateBuffer(RequireDeviceBuffer(resources.FrameUniformBuffer), 0, frameData);
        commands.SetVertexBuffer(0, RequireDeviceBuffer(resources.PositionBuffer));
        commands.SetVertexBuffer(1, RequireDeviceBuffer(resources.NormalBuffer));
        commands.SetVertexBuffer(2, RequireDeviceBuffer(resources.UvBuffer));
        commands.SetIndexBuffer(RequireDeviceBuffer(resources.IndexBuffer), IndexFormat.UInt32);
        commands.SetGraphicsResourceSet(0, GetFrameSet(resources, shadowTexture, shadowSampler));
        PipelineBundle pipelines = GetPipelineBundle(_renderer.CurrentOutputDescription);

        int drawCount = 0;
        foreach (Zhengyan.DigitalWife.Mmd.MMDMesh mesh in meshes)
        {
            Zhengyan.DigitalWife.Mmd.MMDMaterial material = mesh.Material;
            if (!materials.TryGetValue(material, out MaterialTextures? textures) || material.Alpha <= 0.0f || textures.DescriptorSet is null)
            {
                continue;
            }

            int materialIndex = materialIndexOffset + GetMaterialIndex(materials, material);
            TextureView? overrideTexture = resolveTextureOverride?.Invoke(materialIndex)?.NativeResource as TextureView;
            PmxGpuResources.PmxMaterialUniformData materialData = new()
            {
                Ambient = new Vector4(material.Ambient, 1.0f),
                Diffuse = new Vector4(material.Diffuse, material.Alpha),
                Specular = new Vector4(material.Specular, material.SpecularPower),
                TextureMultiply = material.TextureMulFactor,
                TextureAdd = material.TextureAddFactor,
                SphereMultiply = material.SpTextureMulFactor,
                SphereAdd = material.SpTextureAddFactor,
                ToonMultiply = material.ToonTextureMulFactor,
                ToonAdd = material.ToonTextureAddFactor,
                Modes = new Vector4(
                    overrideTexture is null ? GetTextureMode(textures.Texture) : 1.0f,
                    GetSphereTextureMode(material, textures),
                    textures.ToonTexture is null ? 0.0f : 1.0f,
                    materialIndex)
            };

            commands.UpdateBuffer(RequireDeviceBuffer(resources.MaterialUniformBuffer), 0, materialData);
            commands.SetPipeline(material.BothFace ? pipelines.DoubleSided : pipelines.Culled);
            commands.SetGraphicsResourceSet(1, GetMaterialSet(resources, textures.DescriptorSet, overrideTexture));
            commands.DrawIndexed((uint)mesh.VertexCount, 1, (uint)mesh.BeginIndex, 0, 0);
            drawCount++;
        }

        return drawCount;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (ResourceSet resourceSet in _materialSets.Values)
        {
            resourceSet.Dispose();
        }

        _materialSets.Clear();
        foreach (ResourceSet resourceSet in _frameSets.Values)
        {
            resourceSet.Dispose();
        }

        _frameSets.Clear();
        _frameLayout.Dispose();
        _materialLayout.Dispose();
        foreach (PipelineBundle bundle in _pipelineBundles)
        {
            bundle.Culled.Dispose();
            bundle.DoubleSided.Dispose();
        }

        _pipelineBundles.Clear();
        foreach (VeldridShader shader in _shaders)
        {
            shader.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private ResourceSet GetMaterialSet(
        PmxGpuResources resources,
        PmxMaterialDescriptorSet descriptorSet,
        TextureView? overrideTexture)
    {
        MaterialSetKey key = new(descriptorSet, overrideTexture);
        if (_materialSets.TryGetValue(key, out ResourceSet? resourceSet))
        {
            return resourceSet;
        }

        PmxTextureDescriptor baseTexture = descriptorSet.Bindings[0];
        PmxTextureDescriptor sphereTexture = descriptorSet.Bindings[1];
        PmxTextureDescriptor toonTexture = descriptorSet.Bindings[2];
        resourceSet = _renderer.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _materialLayout,
            RequireDeviceBuffer(resources.MaterialUniformBuffer),
            overrideTexture ?? RequireTextureView(baseTexture.Texture),
            RequireSampler(baseTexture.Sampler),
            RequireTextureView(sphereTexture.Texture),
            RequireSampler(sphereTexture.Sampler),
            RequireTextureView(toonTexture.Texture),
            RequireSampler(toonTexture.Sampler)));
        _materialSets[key] = resourceSet;
        return resourceSet;
    }

    private ResourceSet GetFrameSet(
        PmxGpuResources resources,
        TextureView? shadowTexture,
        VeldridSampler? shadowSampler)
    {
        TextureView fallbackTexture = RequireTextureView(resources.DefaultTexture);
        VeldridSampler fallbackSampler = RequireSampler(resources.TextureSampler);
        FrameSetKey key = new(shadowTexture ?? fallbackTexture, shadowSampler ?? fallbackSampler);
        if (_frameSets.TryGetValue(key, out ResourceSet? resourceSet))
        {
            return resourceSet;
        }

        foreach (FrameSetKey staleKey in _frameSets.Keys
            .Where(existingKey => !existingKey.Equals(_fallbackFrameSetKey))
            .ToArray())
        {
            _frameSets[staleKey].Dispose();
            _frameSets.Remove(staleKey);
        }

        resourceSet = _renderer.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _frameLayout,
            RequireDeviceBuffer(resources.FrameUniformBuffer),
            key.Texture,
            key.Sampler));
        _frameSets[key] = resourceSet;
        return resourceSet;
    }

    private PipelineBundle GetPipelineBundle(OutputDescription outputDescription)
    {
        foreach (PipelineBundle existing in _pipelineBundles)
        {
            if (existing.OutputDescription.Equals(outputDescription))
            {
                return existing;
            }
        }

        ResourceLayout[] layouts = [_frameLayout, _materialLayout];
        PipelineBundle created = new(
            outputDescription,
            _renderer.ResourceFactory.CreateGraphicsPipeline(CreatePipelineDescription(
                _shaderSet, layouts, outputDescription, cullBack: true)),
            _renderer.ResourceFactory.CreateGraphicsPipeline(CreatePipelineDescription(
                _shaderSet, layouts, outputDescription, cullBack: false)));
        _pipelineBundles.Add(created);
        return created;
    }

    private static GraphicsPipelineDescription CreatePipelineDescription(
        ShaderSetDescription shaderSet,
        ResourceLayout[] layouts,
        OutputDescription outputDescription,
        bool cullBack)
    {
        return new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            new DepthStencilStateDescription(
                depthTestEnabled: true,
                depthWriteEnabled: true,
                comparisonKind: ComparisonKind.LessEqual),
            cullBack
                ? new RasterizerStateDescription(FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false)
                : new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
            PrimitiveTopology.TriangleList,
            shaderSet,
            layouts,
            outputDescription);
    }

    private static int GetMaterialIndex(
        IReadOnlyDictionary<Zhengyan.DigitalWife.Mmd.MMDMaterial, MaterialTextures> materials,
        Zhengyan.DigitalWife.Mmd.MMDMaterial material)
    {
        int index = 0;
        foreach (Zhengyan.DigitalWife.Mmd.MMDMaterial candidate in materials.Keys)
        {
            if (ReferenceEquals(candidate, material)) return index;
            index++;
        }

        return -1;
    }

    private static float GetTextureMode(ITexture2D? texture)
    {
        return texture is null
            ? 0.0f
            : texture.AlphaMode switch
            {
                TextureAlphaMode.Blend => 2.0f,
                TextureAlphaMode.ColorMask => 3.0f,
                TextureAlphaMode.BlendMaskColor => 4.0f,
                _ => 1.0f
            };
    }

    private static float GetSphereTextureMode(
        Zhengyan.DigitalWife.Mmd.MMDMaterial material,
        MaterialTextures textures)
    {
        if (textures.SphereTexture is null) return 0.0f;
        return material.SpTextureMode switch
        {
            Zhengyan.DigitalWife.Mmd.SphereTextureMode.Mul => 1.0f,
            Zhengyan.DigitalWife.Mmd.SphereTextureMode.Add => 2.0f,
            _ => 0.0f
        };
    }

    private static DeviceBuffer RequireDeviceBuffer(IGpuBuffer buffer)
    {
        return buffer.NativeResource as DeviceBuffer
            ?? throw new InvalidOperationException("PMX Vulkan pass requires a Veldrid device buffer.");
    }

    private static TextureView RequireTextureView(ITexture2D texture)
    {
        return texture.NativeResource as TextureView
            ?? throw new InvalidOperationException("PMX Vulkan pass requires a Veldrid texture view.");
    }

    private static VeldridSampler RequireSampler(IGpuSampler sampler)
    {
        return sampler.NativeResource as VeldridSampler
            ?? throw new InvalidOperationException("PMX Vulkan pass requires a Veldrid sampler.");
    }

    private sealed record PipelineBundle(OutputDescription OutputDescription, Pipeline Culled, Pipeline DoubleSided);

    private readonly record struct MaterialSetKey(PmxMaterialDescriptorSet DescriptorSet, TextureView? OverrideTexture);
    private readonly record struct FrameSetKey(TextureView Texture, VeldridSampler Sampler);

    private const string VertexShaderSource = """
        layout(set = 0, binding = 0, std140) uniform PmxFrame
        {
            mat4 u_World;
            mat4 u_View;
            mat4 u_Projection;
            mat4 u_WVP;
            vec4 u_LightColor;
            vec4 u_LightDir;
            vec4 u_AmbientLightColor;
            vec4 u_Parameters;
            mat4 u_ShadowWVP;
            vec4 u_ShadowParameters;
        } u_Frame;

        layout(location = 0) in vec3 in_Pos;
        layout(location = 1) in vec3 in_Nor;
        layout(location = 2) in vec2 in_UV;
        layout(location = 0) out vec3 vs_Pos;
        layout(location = 1) out vec3 vs_Nor;
        layout(location = 2) out vec2 vs_UV;
        layout(location = 3) out vec4 vs_ShadowPos;

        void main()
        {
            gl_Position = u_Frame.u_WVP * vec4(in_Pos, 1.0);
            mat4 worldView = u_Frame.u_View * u_Frame.u_World;
            vs_Pos = (worldView * vec4(in_Pos, 1.0)).xyz;
            vs_Nor = mat3(worldView) * in_Nor;
            vs_UV = vec2(in_UV.x, -in_UV.y);
            vs_ShadowPos = u_Frame.u_ShadowWVP * vec4(in_Pos, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        layout(set = 0, binding = 0, std140) uniform PmxFrame
        {
            mat4 u_World;
            mat4 u_View;
            mat4 u_Projection;
            mat4 u_WVP;
            vec4 u_LightColor;
            vec4 u_LightDir;
            vec4 u_AmbientLightColor;
            vec4 u_Parameters;
            mat4 u_ShadowWVP;
            vec4 u_ShadowParameters;
        } u_Frame;

        layout(set = 0, binding = 1) uniform texture2D u_ShadowMap;
        layout(set = 0, binding = 2) uniform sampler u_ShadowSampler;

        layout(set = 1, binding = 0, std140) uniform PmxMaterial
        {
            vec4 u_Ambient;
            vec4 u_Diffuse;
            vec4 u_Specular;
            vec4 u_TexMulFactor;
            vec4 u_TexAddFactor;
            vec4 u_SphereTexMulFactor;
            vec4 u_SphereTexAddFactor;
            vec4 u_ToonTexMulFactor;
            vec4 u_ToonTexAddFactor;
            vec4 u_Modes;
        } u_Material;

        layout(set = 1, binding = 1) uniform texture2D u_Tex;
        layout(set = 1, binding = 2) uniform sampler u_TexSampler;
        layout(set = 1, binding = 3) uniform texture2D u_SphereTex;
        layout(set = 1, binding = 4) uniform sampler u_SphereSampler;
        layout(set = 1, binding = 5) uniform texture2D u_ToonTex;
        layout(set = 1, binding = 6) uniform sampler u_ToonSampler;

        layout(location = 0) in vec3 vs_Pos;
        layout(location = 1) in vec3 vs_Nor;
        layout(location = 2) in vec2 vs_UV;
        layout(location = 3) in vec4 vs_ShadowPos;
        layout(location = 0) out vec4 out_Color;

        vec3 ComputeTexMulFactor(vec3 color, vec4 factor)
        {
            return mix(vec3(1.0), color * factor.rgb, factor.a);
        }

        vec3 ComputeTexAddFactor(vec3 color, vec4 factor)
        {
            vec3 value = clamp(color + (color - vec3(1.0)) * factor.a, vec3(0.0), vec3(1.0));
            return value + factor.rgb;
        }

        float SampleShadowMap()
        {
            vec3 ndc = vs_ShadowPos.xyz / max(abs(vs_ShadowPos.w), 0.0001);
            vec2 uv = ndc.xy * 0.5 + 0.5;
            if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0 || ndc.z < -1.0 || ndc.z > 1.0)
            {
                return 1.0;
            }

            float depth = (ndc.z * 0.5 + 0.5) - u_Frame.u_ShadowParameters.z;
            return texture(sampler2D(u_ShadowMap, u_ShadowSampler), uv).r >= depth ? 1.0 : 0.0;
        }

        void main()
        {
            vec3 eyeDir = normalize(-vs_Pos);
            vec3 lightDir = normalize(-u_Frame.u_LightDir.xyz);
            vec3 normal = normalize(vs_Nor);
            float ndotl = clamp(dot(normal, lightDir), 0.0, 1.0);
            float toonCoord = clamp(dot(normal, lightDir) * 0.5 + 0.5, 0.0, 1.0);
            vec3 albedo = u_Material.u_Diffuse.rgb;
            float alpha = u_Material.u_Diffuse.a;

            if (u_Frame.u_ShadowParameters.x > 0.5)
            {
                float visibility = SampleShadowMap();
                float shadowFactor = mix(1.0 - clamp(u_Frame.u_ShadowParameters.y, 0.0, 1.0), 1.0, visibility);
                ndotl *= shadowFactor;
                toonCoord = mix(0.0, toonCoord, shadowFactor);
            }

            if (u_Material.u_Modes.x > 0.5)
            {
                vec4 texColor = texture(sampler2D(u_Tex, u_TexSampler), vs_UV);
                texColor.rgb = ComputeTexMulFactor(texColor.rgb, u_Material.u_TexMulFactor);
                texColor.rgb = ComputeTexAddFactor(texColor.rgb, u_Material.u_TexAddFactor);
                albedo *= texColor.rgb;
                if (u_Material.u_Modes.x > 3.5)
                {
                    alpha *= 1.0 - pow(1.0 - texColor.a, 1.5);
                }
                else if (u_Material.u_Modes.x > 1.5 && u_Material.u_Modes.x < 2.5)
                {
                    alpha *= texColor.a;
                }
            }

            if (alpha < 0.01) discard;

            vec3 baseColor = albedo;
            if (u_Material.u_Modes.y > 0.5)
            {
                vec2 sphereUv = vec2(normal.x * 0.5 + 0.5, 1.0 - (normal.y * 0.5 + 0.5));
                vec3 sphereColor = texture(sampler2D(u_SphereTex, u_SphereSampler), sphereUv).rgb;
                sphereColor = ComputeTexMulFactor(sphereColor, u_Material.u_SphereTexMulFactor);
                sphereColor = ComputeTexAddFactor(sphereColor, u_Material.u_SphereTexAddFactor);
                if (u_Material.u_Modes.y < 1.5) baseColor *= sphereColor;
                else baseColor += sphereColor;
            }

            vec3 litColor = u_Frame.u_LightColor.rgb * ndotl;
            if (u_Material.u_Modes.z > 0.5)
            {
                vec3 toonColor = texture(sampler2D(u_ToonTex, u_ToonSampler), vec2(0.0, toonCoord)).rgb;
                toonColor = ComputeTexMulFactor(toonColor, u_Material.u_ToonTexMulFactor);
                toonColor = ComputeTexAddFactor(toonColor, u_Material.u_ToonTexAddFactor);
                litColor *= toonColor;
            }

            vec3 specular = vec3(0.0);
            if (u_Material.u_Specular.a > 0.0 && ndotl > 0.0)
            {
                vec3 halfVector = normalize(eyeDir + lightDir);
                specular = pow(max(0.0, dot(halfVector, normal)), u_Material.u_Specular.a)
                    * u_Material.u_Specular.rgb * u_Frame.u_LightColor.rgb;
            }

            vec3 ambient = albedo * u_Material.u_Ambient.rgb
                * u_Frame.u_AmbientLightColor.rgb * u_Frame.u_Parameters.x;
            out_Color = vec4(clamp((baseColor * litColor) + specular + ambient, vec3(0.0), vec3(1.0)), alpha);
        }
        """;
}

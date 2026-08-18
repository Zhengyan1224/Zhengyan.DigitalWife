using Android.Opengl;
using Android.Util;
using Java.Nio;
using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.Mmd;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidPmxSceneRenderer : IDisposable
{
    private const string LogTag = "ZhengyanGamePlayer";
    private const int VertexFloatCount = 21;
    private const int VertexStride = VertexFloatCount * sizeof(float);
    private const int MaxGpuBones = 96;
    private const int MaxPointLights = 8;
    private const int MaxSpotLights = 8;
    private const int ShadowMapSize = 1024;

    private readonly List<PmxGpuModel> _models = [];
    private readonly List<PlaneGpu> _planes = [];
    private readonly List<ParticleGpu> _particles = [];
    private readonly List<WaterGpu> _waters = [];
    private readonly Dictionary<string, RenderTargetGpu> _renderTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PmxGpuModel> _updateOrder = [];
    private readonly Dictionary<string, int> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _softAlphaTextures = [];
    private readonly int _program;
    private readonly int _mvpLocation;
    private readonly int _modelLocation;
    private readonly int _viewLocation;
    private readonly int _cameraPositionLocation;
    private readonly int _diffuseLocation;
    private readonly int _materialAmbientLocation;
    private readonly int _specularLocation;
    private readonly int _specularPowerLocation;
    private readonly int _textureLocation;
    private readonly int _hasTextureLocation;
    private readonly int _lightDirectionLocation;
    private readonly int _lightColorLocation;
    private readonly int _ambientColorLocation;
    private readonly int _ambientStrengthLocation;
    private readonly int _lightViewProjectionLocation;
    private readonly int _shadowMapLocation;
    private readonly int _hasShadowMapLocation;
    private readonly int _receiveShadowLocation;
    private readonly int _shadowModeLocation;
    private readonly int _shadowColorLocation;
    private readonly int _pointLightCountLocation;
    private readonly int[] _pointLightPositionRangeLocations;
    private readonly int[] _pointLightColorIntensityLocations;
    private readonly int _spotLightCountLocation;
    private readonly int[] _spotLightPositionRangeLocations;
    private readonly int[] _spotLightDirectionOuterLocations;
    private readonly int[] _spotLightColorIntensityLocations;
    private readonly int[] _spotLightConeLocations;
    private readonly int _sphereLocation;
    private readonly int _toonLocation;
    private readonly int _sphereModeLocation;
    private readonly int _hasSphereLocation;
    private readonly int _hasToonLocation;
    private readonly int _textureMultiplyLocation;
    private readonly int _textureAddLocation;
    private readonly int _sphereMultiplyLocation;
    private readonly int _sphereAddLocation;
    private readonly int _toonMultiplyLocation;
    private readonly int _toonAddLocation;
    private readonly int _useGpuSkinningLocation;
    private readonly int _bonesLocation;
    private readonly int _edgeProgram;
    private readonly int _edgeMvpLocation;
    private readonly int _edgeModelViewLocation;
    private readonly int _edgeScreenSizeLocation;
    private readonly int _edgeSizeLocation;
    private readonly int _edgeColorLocation;
    private readonly int _edgeUseGpuSkinningLocation;
    private readonly int _edgeBonesLocation;
    private readonly int _shadowProgram;
    private readonly int _shadowMvpLocation;
    private readonly int _shadowUseGpuSkinningLocation;
    private readonly int _shadowBonesLocation;
    private readonly int _shadowFramebuffer;
    private readonly int _shadowDepthTexture;
    private readonly int _shadowColorTexture;
    private readonly int _skyboxProgram;
    private readonly int _skyboxMvpLocation;
    private readonly int _skyboxTextureLocation;
    private readonly int _skyboxTintLocation;
    private readonly int _skyboxExposureLocation;
    private readonly int _skyboxVertexArrayObject;
    private readonly int _skyboxVertexBuffer;
    private int _skyboxTexture;
    private readonly int _particleProgram;
    private readonly int _particleViewProjectionLocation;
    private readonly int _particleCameraRightLocation;
    private readonly int _particleCameraUpLocation;
    private readonly int _particleTextureLocation;
    private readonly int _particleOpacityLocation;
    private readonly int _particleUseTextureColorLocation;
    private readonly int _waterProgram;
    private readonly int _waterViewProjectionLocation;
    private readonly int _waterLightDirectionLocation;
    private readonly int _waterLightColorLocation;
    private readonly int _waterAmbientLocation;
    private readonly int _waterDeepColorLocation;
    private readonly int _waterReflectionTintLocation;
    private readonly int _waterAlphaLocation;
    private readonly int _waterSkyTextureLocation;
    private readonly int _waterHasSkyTextureLocation;
    private readonly int _postProgram;
    private readonly int _postTintLocation;
    private readonly int _postAlphaLocation;
    private readonly int _postVertexArrayObject;
    private bool _shadowAvailable;
    private Matrix4x4 _lightViewProjection = Matrix4x4.Identity;
    private RuntimeScene? _loadedScene;
    private string? _projectDirectory;
    private long _loadedEntityRevision = -1;
    private bool _disposed;

    public AndroidPmxSceneRenderer()
    {
        _program = CreateProgram(VertexShaderSource, FragmentShaderSource);
        _mvpLocation = GLES30.GlGetUniformLocation(_program, "uMvp");
        _modelLocation = GLES30.GlGetUniformLocation(_program, "uModel");
        _viewLocation = GLES30.GlGetUniformLocation(_program, "uView");
        _cameraPositionLocation = GLES30.GlGetUniformLocation(_program, "uCameraPosition");
        _diffuseLocation = GLES30.GlGetUniformLocation(_program, "uDiffuse");
        _materialAmbientLocation = GLES30.GlGetUniformLocation(_program, "uMaterialAmbient");
        _specularLocation = GLES30.GlGetUniformLocation(_program, "uSpecular");
        _specularPowerLocation = GLES30.GlGetUniformLocation(_program, "uSpecularPower");
        _textureLocation = GLES30.GlGetUniformLocation(_program, "uTexture");
        _hasTextureLocation = GLES30.GlGetUniformLocation(_program, "uHasTexture");
        _lightDirectionLocation = GLES30.GlGetUniformLocation(_program, "uLightDirection");
        _lightColorLocation = GLES30.GlGetUniformLocation(_program, "uLightColor");
        _ambientColorLocation = GLES30.GlGetUniformLocation(_program, "uAmbientColor");
        _ambientStrengthLocation = GLES30.GlGetUniformLocation(_program, "uAmbientStrength");
        _lightViewProjectionLocation = GLES30.GlGetUniformLocation(_program, "uLightViewProjection");
        _shadowMapLocation = GLES30.GlGetUniformLocation(_program, "uShadowMap");
        _hasShadowMapLocation = GLES30.GlGetUniformLocation(_program, "uHasShadowMap");
        _receiveShadowLocation = GLES30.GlGetUniformLocation(_program, "uReceiveShadow");
        _shadowModeLocation = GLES30.GlGetUniformLocation(_program, "uShadowMode");
        _shadowColorLocation = GLES30.GlGetUniformLocation(_program, "uShadowColor");
        _pointLightCountLocation = GLES30.GlGetUniformLocation(_program, "uPointLightCount");
        _pointLightPositionRangeLocations = GetUniformLocations(_program, "uPointLightPositionRange", MaxPointLights);
        _pointLightColorIntensityLocations = GetUniformLocations(_program, "uPointLightColorIntensity", MaxPointLights);
        _spotLightCountLocation = GLES30.GlGetUniformLocation(_program, "uSpotLightCount");
        _spotLightPositionRangeLocations = GetUniformLocations(_program, "uSpotLightPositionRange", MaxSpotLights);
        _spotLightDirectionOuterLocations = GetUniformLocations(_program, "uSpotLightDirectionOuter", MaxSpotLights);
        _spotLightColorIntensityLocations = GetUniformLocations(_program, "uSpotLightColorIntensity", MaxSpotLights);
        _spotLightConeLocations = GetUniformLocations(_program, "uSpotLightCone", MaxSpotLights);
        _sphereLocation = GLES30.GlGetUniformLocation(_program, "uSphereTexture");
        _toonLocation = GLES30.GlGetUniformLocation(_program, "uToonTexture");
        _sphereModeLocation = GLES30.GlGetUniformLocation(_program, "uSphereMode");
        _hasSphereLocation = GLES30.GlGetUniformLocation(_program, "uHasSphereTexture");
        _hasToonLocation = GLES30.GlGetUniformLocation(_program, "uHasToonTexture");
        _textureMultiplyLocation = GLES30.GlGetUniformLocation(_program, "uTextureMultiply");
        _textureAddLocation = GLES30.GlGetUniformLocation(_program, "uTextureAdd");
        _sphereMultiplyLocation = GLES30.GlGetUniformLocation(_program, "uSphereMultiply");
        _sphereAddLocation = GLES30.GlGetUniformLocation(_program, "uSphereAdd");
        _toonMultiplyLocation = GLES30.GlGetUniformLocation(_program, "uToonMultiply");
        _toonAddLocation = GLES30.GlGetUniformLocation(_program, "uToonAdd");
        _useGpuSkinningLocation = GLES30.GlGetUniformLocation(_program, "uUseGpuSkinning");
        _bonesLocation = GLES30.GlGetUniformLocation(_program, "uBones[0]");

        _edgeProgram = CreateProgram(EdgeVertexShaderSource, EdgeFragmentShaderSource);
        _edgeMvpLocation = GLES30.GlGetUniformLocation(_edgeProgram, "uMvp");
        _edgeModelViewLocation = GLES30.GlGetUniformLocation(_edgeProgram, "uModelView");
        _edgeScreenSizeLocation = GLES30.GlGetUniformLocation(_edgeProgram, "uScreenSize");
        _edgeSizeLocation = GLES30.GlGetUniformLocation(_edgeProgram, "uEdgeSize");
        _edgeColorLocation = GLES30.GlGetUniformLocation(_edgeProgram, "uEdgeColor");
        _edgeUseGpuSkinningLocation = GLES30.GlGetUniformLocation(_edgeProgram, "uUseGpuSkinning");
        _edgeBonesLocation = GLES30.GlGetUniformLocation(_edgeProgram, "uBones[0]");

        _shadowProgram = CreateProgram(ShadowVertexShaderSource, ShadowFragmentShaderSource);
        _shadowMvpLocation = GLES30.GlGetUniformLocation(_shadowProgram, "uMvp");
        _shadowUseGpuSkinningLocation = GLES30.GlGetUniformLocation(_shadowProgram, "uUseGpuSkinning");
        _shadowBonesLocation = GLES30.GlGetUniformLocation(_shadowProgram, "uBones[0]");
        (_shadowFramebuffer, _shadowDepthTexture, _shadowColorTexture, _shadowAvailable) = CreateShadowMapResources();

        _skyboxProgram = CreateProgram(SkyboxVertexShaderSource, SkyboxFragmentShaderSource);
        _skyboxMvpLocation = GLES30.GlGetUniformLocation(_skyboxProgram, "uMvp");
        _skyboxTextureLocation = GLES30.GlGetUniformLocation(_skyboxProgram, "uTexture");
        _skyboxTintLocation = GLES30.GlGetUniformLocation(_skyboxProgram, "uTint");
        _skyboxExposureLocation = GLES30.GlGetUniformLocation(_skyboxProgram, "uExposure");
        (_skyboxVertexArrayObject, _skyboxVertexBuffer) = CreateSkyboxMesh();

        _particleProgram = CreateProgram(ParticleVertexShaderSource, ParticleFragmentShaderSource);
        _particleViewProjectionLocation = GLES30.GlGetUniformLocation(_particleProgram, "uViewProjection");
        _particleCameraRightLocation = GLES30.GlGetUniformLocation(_particleProgram, "uCameraRight");
        _particleCameraUpLocation = GLES30.GlGetUniformLocation(_particleProgram, "uCameraUp");
        _particleTextureLocation = GLES30.GlGetUniformLocation(_particleProgram, "uTexture");
        _particleOpacityLocation = GLES30.GlGetUniformLocation(_particleProgram, "uOpacity");
        _particleUseTextureColorLocation = GLES30.GlGetUniformLocation(_particleProgram, "uUseTextureColor");

        _waterProgram = CreateProgram(WaterVertexShaderSource, WaterFragmentShaderSource);
        _waterViewProjectionLocation = GLES30.GlGetUniformLocation(_waterProgram, "uViewProjection");
        _waterLightDirectionLocation = GLES30.GlGetUniformLocation(_waterProgram, "uLightDirection");
        _waterLightColorLocation = GLES30.GlGetUniformLocation(_waterProgram, "uLightColor");
        _waterAmbientLocation = GLES30.GlGetUniformLocation(_waterProgram, "uAmbientColor");
        _waterDeepColorLocation = GLES30.GlGetUniformLocation(_waterProgram, "uDeepColor");
        _waterReflectionTintLocation = GLES30.GlGetUniformLocation(_waterProgram, "uReflectionTint");
        _waterAlphaLocation = GLES30.GlGetUniformLocation(_waterProgram, "uAlpha");
        _waterSkyTextureLocation = GLES30.GlGetUniformLocation(_waterProgram, "uSkyTexture");
        _waterHasSkyTextureLocation = GLES30.GlGetUniformLocation(_waterProgram, "uHasSkyTexture");

        _postProgram = CreateProgram(PostVertexShaderSource, PostFragmentShaderSource);
        _postTintLocation = GLES30.GlGetUniformLocation(_postProgram, "uTint");
        _postAlphaLocation = GLES30.GlGetUniformLocation(_postProgram, "uAlpha");
        int[] postArrays = new int[1];
        GLES30.GlGenVertexArrays(1, postArrays, 0);
        _postVertexArrayObject = postArrays[0];
    }

    public int ModelCount => _models.Count;

    public bool TrySetMotionLayerState(
        string entityIdOrName,
        int layerIndex,
        float? frame = null,
        bool? playing = null,
        bool? loop = null,
        float? playbackSpeed = null,
        float? weight = null)
    {
        PmxGpuModel? model = FindModel(entityIdOrName);
        return model is not null
            && model.TrySetMotionLayerState(layerIndex, frame, playing, loop, playbackSpeed, weight);
    }

    public bool TryCreatePoseSnapshot(string entityIdOrName, out string snapshot)
    {
        PmxGpuModel? model = FindModel(entityIdOrName);
        if (model is null)
        {
            snapshot = string.Empty;
            return false;
        }
        snapshot = model.CreatePoseSnapshot();
        return true;
    }

    public void Load(RuntimeScene scene, string? projectDirectory)
    {
        ClearModels();
        _loadedScene = scene;
        _projectDirectory = projectDirectory;
        if (scene is null || string.IsNullOrWhiteSpace(projectDirectory))
        {
            _loadedEntityRevision = scene?.EntityRevision ?? -1;
            return;
        }

        foreach (RuntimeEntity runtimeEntity in scene.PmxModels)
        {
            GameEntity entity = runtimeEntity.Definition;
            string modelPath = GameProjectPath.ToAbsolute(projectDirectory, entity.AssetPath);
            if (!File.Exists(modelPath))
            {
                Log.Warn(LogTag, $"Android PMX asset was not found: {modelPath}");
                continue;
            }

            try
            {
                PmxParsing? pmx = PmxParsing.ParsingByFile(modelPath);
                if (pmx is null)
                {
                    Log.Warn(LogTag, $"Android PMX parser rejected asset: {modelPath}");
                    continue;
                }

                PmxRuntimeFeatureReport featureReport = PmxRuntimeDiagnostics.Analyze(pmx);
                Log.Info(
                    LogTag,
                    $"Android PMX feature report '{entity.Name}': vertices={featureReport.VertexCount}; faces={featureReport.FaceCount}; materials={featureReport.MaterialCount}; bones={featureReport.BoneCount}; morphs={featureReport.MorphCount}; rigidBodies={featureReport.RigidBodyCount}; joints={featureReport.JointCount}");
                foreach (string warning in featureReport.Warnings)
                {
                    Log.Warn(LogTag, $"Android PMX '{entity.Name}': {warning}");
                }

                IReadOnlyList<(VmdParsing Animation, float Weight)> motions = LoadMotionLayers(entity, projectDirectory);
                PmxGpuModel gpuModel = PmxGpuModel.Create(
                    pmx,
                    entity.Transform,
                    modelPath,
                    LoadTexture,
                    LoadCommonToonTexture,
                    textureId => _softAlphaTextures.Contains(textureId),
                    motions,
                    entity.IsPlaying ? entity.PlaybackSpeed : 0.0f,
                    entity.LoopMotion,
                    entity.EnablePhysics,
                    entity.PhysicsGravity,
                    entity.ResetPhysicsOnMotionLoop,
                    entity.EnableEdge,
                    entity.Id,
                    entity.Name,
                    entity.Relation,
                    runtimeEntity);
                _models.Add(gpuModel);
                Log.Info(LogTag, $"Android GLES uploaded PMX '{entity.Name}': vertices={pmx.Vertices.Length}; faces={pmx.Faces.Length}; materials={pmx.Materials.Length}; skinning={gpuModel.SkinningBackend}; layers={motions.Count}; physics={gpuModel.PhysicsBackend}");
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Android PMX upload failed for '{entity.Name}': {ex}");
            }
        }

        foreach (RenderTextureSettings settings in scene.Definition.RenderTextures.Where(candidate => candidate.Enabled))
        {
            try
            {
                RenderTargetGpu target = RenderTargetGpu.Create(settings);
                _renderTargets[settings.Id] = target;
                if (!string.IsNullOrWhiteSpace(settings.Name)) _renderTargets.TryAdd(settings.Name, target);
                Log.Info(LogTag, $"Android GLES created RenderTexture '{settings.Name}': {target.Width}x{target.Height}.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogTag, $"Android RenderTexture '{settings.Name}' is unavailable: {ex.Message}");
            }
        }

        foreach (RuntimeEntity runtimeEntity in scene.TexturedPlanes)
        {
            try
            {
                _planes.Add(PlaneGpu.Create(runtimeEntity, ResolveSceneTexture(runtimeEntity.Definition.Plane.TexturePath, projectDirectory)));
                Log.Info(LogTag, $"Android GLES uploaded textured plane '{runtimeEntity.Name}'.");
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Android textured-plane upload failed for '{runtimeEntity.Name}': {ex}");
            }
        }

        if (scene.Definition.Skybox.Enabled && !string.IsNullOrWhiteSpace(scene.Definition.Skybox.TexturePath))
        {
            try
            {
                string skyboxPath = GameProjectPath.ToAbsolute(projectDirectory, scene.Definition.Skybox.TexturePath);
                _skyboxTexture = LoadTexture(skyboxPath);
                if (_skyboxTexture == 0)
                {
                    Log.Warn(LogTag, $"Android skybox texture was not loaded: {skyboxPath}");
                }
            }
            catch (Exception ex)
            {
                _skyboxTexture = 0;
                Log.Warn(LogTag, $"Android skybox upload failed: {ex.Message}");
            }
        }

        foreach (RuntimeEntity runtimeEntity in scene.ParticleSystems)
        {
            try
            {
                string? texturePath = runtimeEntity.Definition.Particle.TexturePath;
                int texture = !string.IsNullOrWhiteSpace(texturePath)
                    ? ResolveSceneTexture(texturePath, projectDirectory)
                    : 0;
                if (texture == 0)
                {
                    texture = LoadParticlePresetTexture(runtimeEntity.Definition.Particle.TexturePreset);
                }
                _particles.Add(ParticleGpu.Create(runtimeEntity, texture));
                Log.Info(LogTag, $"Android GLES uploaded particle system '{runtimeEntity.Name}': count={runtimeEntity.Definition.Particle.ParticleCount}.");
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Android particle-system upload failed for '{runtimeEntity.Name}': {ex}");
            }
        }

        foreach (RuntimeEntity runtimeEntity in scene.WaterSurfaces)
        {
            try
            {
                _waters.Add(WaterGpu.Create(runtimeEntity));
                Log.Info(LogTag, $"Android GLES uploaded water surface '{runtimeEntity.Name}'.");
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Android water-surface upload failed for '{runtimeEntity.Name}': {ex}");
            }
        }

        ResolveRelations();
        _loadedEntityRevision = scene.EntityRevision;
    }

    public void Draw(RuntimeScene scene, int referenceWidth, int referenceHeight, int width, int height, double timeSeconds)
    {
        if (!ReferenceEquals(_loadedScene, scene) || _loadedEntityRevision != scene.EntityRevision)
        {
            Load(scene, _projectDirectory);
        }

        if (_models.Count == 0 && _planes.Count == 0 && _particles.Count == 0 && _waters.Count == 0 && _skyboxTexture == 0)
        {
            return;
        }

        foreach (PmxGpuModel model in _updateOrder)
        {
            model.UpdateAnimation(timeSeconds);
        }
        foreach (PmxGpuModel model in _updateOrder)
        {
            model.ApplyRelation();
        }
        foreach (PmxGpuModel model in _models)
        {
            model.SyncTransform();
        }
        RenderDirectionalShadow(scene);

        foreach (RuntimeCamera camera in scene.RenderCameras)
        {
            RenderTargetGpu? renderTarget = FindRenderTarget(scene, camera);
            if (renderTarget is not null && !renderTarget.ShouldRefresh(timeSeconds))
            {
                continue;
            }
            RuntimeViewport viewport = renderTarget is null
                ? camera.ResolveViewport(width, height, referenceWidth, referenceHeight)
                : new RuntimeViewport(0, 0, renderTarget.Width, renderTarget.Height);
            GLES30.GlBindFramebuffer(GLES30.GlFramebuffer, renderTarget?.Framebuffer ?? 0);
            GLES30.GlViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height);
            GLES30.GlScissor(viewport.X, viewport.Y, viewport.Width, viewport.Height);
            GLES30.GlEnable(GLES30.GlScissorTest);
            GLES30.GlClearColor(
                renderTarget?.ClearColor.X ?? scene.Definition.Lighting.ClearColor.X,
                renderTarget?.ClearColor.Y ?? scene.Definition.Lighting.ClearColor.Y,
                renderTarget?.ClearColor.Z ?? scene.Definition.Lighting.ClearColor.Z,
                renderTarget?.ClearColor.W ?? scene.Definition.Lighting.ClearColor.W);
            GLES30.GlClear(GLES30.GlColorBufferBit | GLES30.GlDepthBufferBit | GLES30.GlStencilBufferBit);

            Matrix4x4 view = camera.CreateView();
            Matrix4x4 projection = camera.CreateProjection(viewport.Width / (float)Math.Max(viewport.Height, 1));
            Vector3 position = camera.Settings.Position.ToVector3();
            GLES30.GlEnable(GLES30.GlDepthTest);
            GLES30.GlDepthFunc(GLES30.GlLequal);
            GLES30.GlEnable(GLES30.GlBlend);
            GLES30.GlBlendFunc(GLES30.GlSrcAlpha, GLES30.GlOneMinusSrcAlpha);
            GLES30.GlDisable(0x0B44); // GL_CULL_FACE
            DrawSkybox(scene, camera, view, projection);
            GLES30.GlUseProgram(_program);
            ApplyLighting(scene);
            GLES30.GlUniformMatrix4fv(_lightViewProjectionLocation, 1, false, ToGlArray(_lightViewProjection), 0);
            GLES30.GlActiveTexture(GLES30.GlTexture3);
            GLES30.GlBindTexture(GLES30.GlTexture2d, _shadowDepthTexture);
            GLES30.GlUniform1i(_shadowMapLocation, 3);
            GLES30.GlUniform1i(_hasShadowMapLocation, _shadowAvailable ? 1 : 0);
            Vector4 shadowColor = scene.Definition.Lighting.ShadowColor.ToVector4();
            GLES30.GlUniform4f(_shadowColorLocation, shadowColor.X, shadowColor.Y, shadowColor.Z, shadowColor.W);
            GLES30.GlUniformMatrix4fv(_viewLocation, 1, false, ToGlArray(view), 0);
            GLES30.GlUniform3f(_cameraPositionLocation, position.X, position.Y, position.Z);
            GLES30.GlUniform1i(_textureLocation, 0);
            GLES30.GlUniform1i(_sphereLocation, 1);
            GLES30.GlUniform1i(_toonLocation, 2);

            foreach (PlaneGpu plane in _planes)
            {
                DrawPlane(plane, camera, view, projection);
            }

            foreach (PmxGpuModel model in _models)
            {
                model.SyncTransform();
                GLES30.GlUniform1i(_receiveShadowLocation, model.ReceivesShadows ? 1 : 0);
                GLES30.GlUniform1i(_shadowModeLocation, model.UsesToonReceivedShadow ? 1 : 0);
                model.BindSkinning(_useGpuSkinningLocation, _bonesLocation);
                Matrix4x4 mvp = model.Transform * view * projection;
                GLES30.GlUniformMatrix4fv(_mvpLocation, 1, false, ToGlArray(mvp), 0);
                GLES30.GlUniformMatrix4fv(_modelLocation, 1, false, ToGlArray(model.Transform), 0);
                model.Draw(
                    _diffuseLocation,
                    _materialAmbientLocation,
                    _specularLocation,
                    _specularPowerLocation,
                    _hasTextureLocation,
                    _sphereModeLocation,
                    _hasSphereLocation,
                    _hasToonLocation,
                    _textureMultiplyLocation,
                    _textureAddLocation,
                    _sphereMultiplyLocation,
                    _sphereAddLocation,
                    _toonMultiplyLocation,
                    _toonAddLocation);
            }

            DrawParticles(scene, camera, view, projection, timeSeconds);

            DrawWater(scene, camera, view, projection, timeSeconds);

            GLES30.GlUseProgram(_edgeProgram);
            GLES30.GlUniform2f(_edgeScreenSizeLocation, viewport.Width, viewport.Height);
            foreach (PmxGpuModel model in _models)
            {
                Matrix4x4 mvp = model.Transform * view * projection;
                Matrix4x4 modelView = model.Transform * view;
                GLES30.GlUniformMatrix4fv(_edgeMvpLocation, 1, false, ToGlArray(mvp), 0);
                GLES30.GlUniformMatrix4fv(_edgeModelViewLocation, 1, false, ToGlArray(modelView), 0);
                model.DrawEdges(_edgeUseGpuSkinningLocation, _edgeBonesLocation, _edgeSizeLocation, _edgeColorLocation);
            }

            DrawUnderwaterOverlay(scene, camera);
            GLES30.GlBindFramebuffer(GLES30.GlFramebuffer, 0);
            renderTarget?.MarkRendered(timeSeconds);
        }

        GLES30.GlDisable(GLES30.GlScissorTest);
        GLES30.GlViewport(0, 0, Math.Max(width, 1), Math.Max(height, 1));
        GLES30.GlBindVertexArray(0);
        GLES30.GlUseProgram(0);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ClearModels();
        _loadedScene = null;
        _projectDirectory = null;
        _loadedEntityRevision = -1;
        GLES30.GlDeleteProgram(_program);
        GLES30.GlDeleteProgram(_edgeProgram);
        GLES30.GlDeleteProgram(_shadowProgram);
        GLES30.GlDeleteProgram(_skyboxProgram);
        GLES30.GlDeleteProgram(_particleProgram);
        GLES30.GlDeleteProgram(_waterProgram);
        GLES30.GlDeleteProgram(_postProgram);
        GLES30.GlDeleteVertexArrays(1, [_postVertexArrayObject], 0);
        GLES30.GlDeleteVertexArrays(1, [_skyboxVertexArrayObject], 0);
        GLES30.GlDeleteBuffers(1, [_skyboxVertexBuffer], 0);
        GLES30.GlDeleteFramebuffers(1, [_shadowFramebuffer], 0);
        GLES30.GlDeleteTextures(2, [_shadowDepthTexture, _shadowColorTexture], 0);
    }

    private void ApplyLighting(RuntimeScene scene)
    {
        LightingSettings lighting = scene.Definition.Lighting;
        Vector3 direction = NormalizeOrDefault(lighting.LightDirection.ToVector3(), new Vector3(-0.5f, -1.0f, -0.5f));
        Vector3 color = Vector3.Max(lighting.LightColor.ToVector3(), Vector3.Zero);
        Vector3 ambient = Vector3.Max(lighting.AmbientColor.ToVector3(), Vector3.Zero);
        GLES30.GlUniform3f(_lightDirectionLocation, direction.X, direction.Y, direction.Z);
        GLES30.GlUniform3f(_lightColorLocation, color.X, color.Y, color.Z);
        GLES30.GlUniform3f(_ambientColorLocation, ambient.X, ambient.Y, ambient.Z);
        GLES30.GlUniform1f(_ambientStrengthLocation, Math.Max(lighting.AmbientStrength, 0.0f));

        RuntimeEntity[] pointLights = scene.PointLights.Take(MaxPointLights).ToArray();
        GLES30.GlUniform1i(_pointLightCountLocation, pointLights.Length);
        for (int i = 0; i < pointLights.Length; i++)
        {
            RuntimeEntity light = pointLights[i];
            Vector3 lightColor = light.LightColor;
            GLES30.GlUniform4f(_pointLightPositionRangeLocations[i], light.Position.X, light.Position.Y, light.Position.Z, light.LightRange);
            GLES30.GlUniform4f(_pointLightColorIntensityLocations[i], lightColor.X, lightColor.Y, lightColor.Z, light.LightIntensity);
        }

        RuntimeEntity[] spotLights = scene.SpotLights.Take(MaxSpotLights).ToArray();
        GLES30.GlUniform1i(_spotLightCountLocation, spotLights.Length);
        for (int i = 0; i < spotLights.Length; i++)
        {
            RuntimeEntity light = spotLights[i];
            Vector3 lightColor = light.LightColor;
            Vector3 lightDirection = light.SpotDirection;
            float outerCosine = MathF.Cos(light.SpotOuterConeAngleDegrees * MathF.PI / 180.0f);
            float innerCosine = MathF.Cos(light.SpotInnerConeAngleDegrees * MathF.PI / 180.0f);
            GLES30.GlUniform4f(_spotLightPositionRangeLocations[i], light.Position.X, light.Position.Y, light.Position.Z, light.LightRange);
            GLES30.GlUniform4f(_spotLightDirectionOuterLocations[i], lightDirection.X, lightDirection.Y, lightDirection.Z, outerCosine);
            GLES30.GlUniform4f(_spotLightColorIntensityLocations[i], lightColor.X, lightColor.Y, lightColor.Z, light.LightIntensity);
            GLES30.GlUniform4f(_spotLightConeLocations[i], innerCosine, 0.0f, 0.0f, 0.0f);
        }
    }

    private void DrawSkybox(RuntimeScene scene, RuntimeCamera camera, Matrix4x4 view, Matrix4x4 projection)
    {
        if (_skyboxTexture == 0 || !scene.Definition.Skybox.Enabled)
        {
            return;
        }

        Matrix4x4 viewRotation = view;
        viewRotation.Translation = Vector3.Zero;
        Matrix4x4 world = Matrix4x4.CreateScale(80.0f)
            * Matrix4x4.CreateTranslation(camera.Settings.Position.ToVector3());
        GLES30.GlUseProgram(_skyboxProgram);
        GLES30.GlUniformMatrix4fv(_skyboxMvpLocation, 1, false, ToGlArray(world * viewRotation * projection), 0);
        Vector3 tint = scene.Definition.Skybox.Tint.ToVector3();
        GLES30.GlUniform3f(
            _skyboxTintLocation,
            Math.Max(tint.X, 0.0f),
            Math.Max(tint.Y, 0.0f),
            Math.Max(tint.Z, 0.0f));
        GLES30.GlUniform1f(_skyboxExposureLocation, Math.Max(scene.Definition.Skybox.Exposure, 0.0f));
        GLES30.GlUniform1i(_skyboxTextureLocation, 0);
        GLES30.GlActiveTexture(GLES30.GlTexture0);
        GLES30.GlBindTexture(GLES30.GlTexture2d, _skyboxTexture);
        GLES30.GlDisable(GLES30.GlDepthTest);
        GLES30.GlDepthMask(false);
        GLES30.GlBindVertexArray(_skyboxVertexArrayObject);
        GLES30.GlDrawArrays(GLES30.GlTriangles, 0, 36);
        GLES30.GlBindVertexArray(0);
        GLES30.GlDepthMask(true);
        GLES30.GlEnable(GLES30.GlDepthTest);
    }

    private static (int VertexArrayObject, int VertexBuffer) CreateSkyboxMesh()
    {
        float[] vertices =
        [
            -1, -1, -1,  1, -1, -1,  1,  1, -1,  1,  1, -1, -1,  1, -1, -1, -1, -1,
            -1, -1,  1, -1,  1,  1,  1,  1,  1,  1,  1,  1,  1, -1,  1, -1, -1,  1,
            -1,  1,  1, -1,  1, -1, -1, -1, -1, -1, -1, -1, -1, -1,  1, -1,  1,  1,
             1,  1,  1,  1,  1, -1,  1, -1, -1,  1, -1, -1,  1, -1,  1,  1,  1,  1,
            -1, -1, -1,  1, -1, -1,  1, -1,  1,  1, -1,  1, -1, -1,  1, -1, -1, -1,
            -1,  1, -1, -1,  1,  1,  1,  1,  1,  1,  1,  1,  1,  1, -1, -1,  1, -1,
        ];
        int[] arrays = new int[1];
        int[] buffers = new int[1];
        GLES30.GlGenVertexArrays(1, arrays, 0);
        GLES30.GlGenBuffers(1, buffers, 0);
        GLES30.GlBindVertexArray(arrays[0]);
        GLES30.GlBindBuffer(GLES30.GlArrayBuffer, buffers[0]);
        using ByteBuffer bytes = ByteBuffer.AllocateDirect(vertices.Length * sizeof(float))!;
        bytes.Order(ByteOrder.NativeOrder()!);
        using FloatBuffer data = bytes.AsFloatBuffer();
        data.Put(vertices);
        data.Position(0);
        GLES30.GlBufferData(GLES30.GlArrayBuffer, vertices.Length * sizeof(float), data, GLES30.GlStaticDraw);
        GLES30.GlEnableVertexAttribArray(0);
        GLES30.GlVertexAttribPointer(0, 3, GLES30.GlFloat, false, 3 * sizeof(float), 0);
        GLES30.GlBindVertexArray(0);
        return (arrays[0], buffers[0]);
    }

    private void DrawParticles(RuntimeScene scene, RuntimeCamera camera, Matrix4x4 view, Matrix4x4 projection, double timeSeconds)
    {
        if (_particles.Count == 0)
        {
            return;
        }

        Vector3 forward = NormalizeOrDefault(camera.Settings.Target.ToVector3() - camera.Settings.Position.ToVector3(), -Vector3.UnitZ);
        Vector3 right = NormalizeOrDefault(Vector3.Cross(forward, Vector3.UnitY), Vector3.UnitX);
        Vector3 up = NormalizeOrDefault(Vector3.Cross(right, forward), Vector3.UnitY);
        GLES30.GlUseProgram(_particleProgram);
        GLES30.GlUniformMatrix4fv(_particleViewProjectionLocation, 1, false, ToGlArray(view * projection), 0);
        GLES30.GlUniform3f(_particleCameraRightLocation, right.X, right.Y, right.Z);
        GLES30.GlUniform3f(_particleCameraUpLocation, up.X, up.Y, up.Z);
        GLES30.GlUniform1i(_particleTextureLocation, 0);
        GLES30.GlEnable(GLES30.GlBlend);
        GLES30.GlDisable(0x0B44); // GL_CULL_FACE
        GLES30.GlEnable(GLES30.GlDepthTest);
        GLES30.GlDepthMask(false);

        foreach (ParticleGpu particle in _particles)
        {
            GLES30.GlBlendFunc(
                particle.Additive ? GLES30.GlSrcAlpha : GLES30.GlSrcAlpha,
                particle.Additive ? GLES30.GlOne : GLES30.GlOneMinusSrcAlpha);
            GLES30.GlUniform1f(_particleOpacityLocation, Math.Clamp(particle.Opacity, 0.0f, 1.0f));
            GLES30.GlUniform1i(_particleUseTextureColorLocation, particle.UseTextureColor ? 1 : 0);
            GLES30.GlActiveTexture(GLES30.GlTexture0);
            GLES30.GlBindTexture(GLES30.GlTexture2d, particle.TextureId);
            particle.Draw(timeSeconds, right, up);
        }

        GLES30.GlBindTexture(GLES30.GlTexture2d, 0);
        GLES30.GlBindVertexArray(0);
        GLES30.GlDepthMask(true);
        GLES30.GlDisable(GLES30.GlBlend);
    }

    private void DrawWater(RuntimeScene scene, RuntimeCamera camera, Matrix4x4 view, Matrix4x4 projection, double timeSeconds)
    {
        if (_waters.Count == 0)
        {
            return;
        }

        LightingSettings lighting = scene.Definition.Lighting;
        Vector3 lightDirection = NormalizeOrDefault(lighting.LightDirection.ToVector3(), new Vector3(-0.5f, -1.0f, -0.5f));
        Vector3 lightColor = Vector3.Max(lighting.LightColor.ToVector3(), Vector3.Zero);
        Vector3 ambient = Vector3.Max(lighting.AmbientColor.ToVector3(), Vector3.Zero) * Math.Max(lighting.AmbientStrength, 0.0f);
        GLES30.GlUseProgram(_waterProgram);
        GLES30.GlUniformMatrix4fv(_waterViewProjectionLocation, 1, false, ToGlArray(view * projection), 0);
        GLES30.GlUniform3f(_waterLightDirectionLocation, lightDirection.X, lightDirection.Y, lightDirection.Z);
        GLES30.GlUniform3f(_waterLightColorLocation, lightColor.X, lightColor.Y, lightColor.Z);
        GLES30.GlUniform3f(_waterAmbientLocation, ambient.X, ambient.Y, ambient.Z);
        GLES30.GlUniform1i(_waterSkyTextureLocation, 4);
        GLES30.GlUniform1i(_waterHasSkyTextureLocation, _skyboxTexture == 0 ? 0 : 1);
        GLES30.GlActiveTexture(GLES30.GlTexture4);
        GLES30.GlBindTexture(GLES30.GlTexture2d, _skyboxTexture);
        GLES30.GlEnable(GLES30.GlBlend);
        GLES30.GlBlendFunc(GLES30.GlSrcAlpha, GLES30.GlOneMinusSrcAlpha);
        GLES30.GlEnable(GLES30.GlDepthTest);
        GLES30.GlDepthMask(false);
        GLES30.GlDisable(0x0B44); // GL_CULL_FACE
        foreach (WaterGpu water in _waters)
        {
            Vector3 deep = Vector3.Max(water.DeepColor, Vector3.Zero);
            Vector3 reflection = Vector3.Max(water.ReflectionTint, Vector3.Zero);
            GLES30.GlUniform3f(_waterDeepColorLocation, deep.X, deep.Y, deep.Z);
            GLES30.GlUniform3f(_waterReflectionTintLocation, reflection.X, reflection.Y, reflection.Z);
            GLES30.GlUniform1f(_waterAlphaLocation, Math.Clamp(water.Alpha, 0.0f, 1.0f));
            water.Draw(timeSeconds);
        }
        GLES30.GlBindVertexArray(0);
        GLES30.GlActiveTexture(GLES30.GlTexture4);
        GLES30.GlBindTexture(GLES30.GlTexture2d, 0);
        GLES30.GlDepthMask(true);
        GLES30.GlDisable(GLES30.GlBlend);
    }

    private void DrawUnderwaterOverlay(RuntimeScene scene, RuntimeCamera camera)
    {
        WaterGpu? water = _waters
            .Where(candidate => candidate.UnderwaterEffectEnabled && camera.Settings.Position.ToVector3().Y < candidate.SurfaceY)
            .OrderByDescending(candidate => candidate.SurfaceY)
            .FirstOrDefault();
        if (water is null)
        {
            return;
        }

        float depth = Math.Max(water.SurfaceY - camera.Settings.Position.ToVector3().Y, 0.0f);
        float visibility = Math.Max(water.UnderwaterVisibilityDistance, 0.001f);
        float alpha = Math.Clamp(1.0f - MathF.Exp(-Math.Max(water.UnderwaterFogDensity, 0.0f) * depth / visibility), 0.0f, 0.82f);
        Vector3 tint = Vector3.Max(water.UnderwaterFogColor, Vector3.Zero);
        GLES30.GlUseProgram(_postProgram);
        GLES30.GlBindVertexArray(_postVertexArrayObject);
        GLES30.GlUniform3f(_postTintLocation, tint.X, tint.Y, tint.Z);
        GLES30.GlUniform1f(_postAlphaLocation, alpha);
        GLES30.GlDisable(GLES30.GlDepthTest);
        GLES30.GlDepthMask(false);
        GLES30.GlEnable(GLES30.GlBlend);
        GLES30.GlBlendFunc(GLES30.GlSrcAlpha, GLES30.GlOneMinusSrcAlpha);
        GLES30.GlDrawArrays(GLES30.GlTriangles, 0, 3);
        GLES30.GlDepthMask(true);
        GLES30.GlDisable(GLES30.GlBlend);
    }

    private RenderTargetGpu? FindRenderTarget(RuntimeScene scene, RuntimeCamera camera)
    {
        SceneCameraSettings definition = camera.Definition;
        RenderTextureSettings? settings = scene.Definition.RenderTextures.FirstOrDefault(candidate =>
            candidate.Enabled
            && (string.Equals(candidate.Camera, definition.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Camera, definition.Name, StringComparison.OrdinalIgnoreCase)));
        if (settings is null || !_renderTargets.TryGetValue(settings.Id, out RenderTargetGpu? target))
        {
            return null;
        }
        return target;
    }

    private void DrawPlane(PlaneGpu plane, RuntimeCamera camera, Matrix4x4 view, Matrix4x4 projection)
    {
        Matrix4x4 world = plane.CreateWorld(camera);
        Matrix4x4 mvp = world * view * projection;
        GLES30.GlUniformMatrix4fv(_mvpLocation, 1, false, ToGlArray(mvp), 0);
        GLES30.GlUniformMatrix4fv(_modelLocation, 1, false, ToGlArray(world), 0);
        GLES30.GlUniform1i(_useGpuSkinningLocation, 0);
        GLES30.GlUniform4f(_diffuseLocation, plane.Tint.X, plane.Tint.Y, plane.Tint.Z, plane.Tint.W);
        GLES30.GlUniform3f(_materialAmbientLocation, 1.0f, 1.0f, 1.0f);
        GLES30.GlUniform3f(_specularLocation, 0.0f, 0.0f, 0.0f);
        GLES30.GlUniform1f(_specularPowerLocation, 0.0f);
        GLES30.GlUniform1i(_hasTextureLocation, plane.TextureId == 0 ? 0 : 1);
        GLES30.GlUniform1i(_hasSphereLocation, 0);
        GLES30.GlUniform1i(_hasToonLocation, 0);
        GLES30.GlUniform1i(_sphereModeLocation, 0);
        GLES30.GlUniform4f(_textureMultiplyLocation, 1.0f, 1.0f, 1.0f, 1.0f);
        GLES30.GlUniform4f(_textureAddLocation, 0.0f, 0.0f, 0.0f, 0.0f);
        GLES30.GlUniform4f(_sphereMultiplyLocation, 1.0f, 1.0f, 1.0f, 1.0f);
        GLES30.GlUniform4f(_sphereAddLocation, 0.0f, 0.0f, 0.0f, 0.0f);
        GLES30.GlUniform4f(_toonMultiplyLocation, 1.0f, 1.0f, 1.0f, 1.0f);
        GLES30.GlUniform4f(_toonAddLocation, 0.0f, 0.0f, 0.0f, 0.0f);
        GLES30.GlUniform1i(_receiveShadowLocation, plane.ReceivesShadows ? 1 : 0);
        GLES30.GlUniform1i(_shadowModeLocation, 0);
        GLES30.GlActiveTexture(GLES30.GlTexture0);
        GLES30.GlBindTexture(GLES30.GlTexture2d, plane.TextureId);
        GLES30.GlBindVertexArray(plane.VertexArrayObject);
        GLES30.GlEnable(0x0B44); // GL_CULL_FACE
        GLES30.GlCullFace(GLES30.GlBack);
        GLES30.GlDepthMask(plane.Tint.W >= 0.999f);
        GLES30.GlDrawArrays(GLES30.GlTriangles, 0, 6);
        GLES30.GlDepthMask(true);
        GLES30.GlDisable(0x0B44); // GL_CULL_FACE
        GLES30.GlBindVertexArray(0);
    }

    private void RenderDirectionalShadow(RuntimeScene scene)
    {
        if (!_shadowAvailable)
        {
            _lightViewProjection = Matrix4x4.Identity;
            return;
        }

        RuntimeCamera camera = scene.MainCamera;
        Vector3 center = camera.Settings.Target.ToVector3();
        Vector3 direction = NormalizeOrDefault(scene.Definition.Lighting.LightDirection.ToVector3(), new Vector3(-0.5f, -1.0f, -0.5f));
        Vector3 lightPosition = center + direction * 48.0f;
        Vector3 up = MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;
        Matrix4x4 lightView = Matrix4x4.CreateLookAt(lightPosition, center, up);
        Matrix4x4 lightProjection = CreateOrthographicProjection(52.0f, 52.0f, 0.1f, 120.0f);
        _lightViewProjection = lightView * lightProjection;

        GLES30.GlBindFramebuffer(GLES30.GlFramebuffer, _shadowFramebuffer);
        GLES30.GlViewport(0, 0, ShadowMapSize, ShadowMapSize);
        GLES30.GlColorMask(false, false, false, false);
        GLES30.GlDepthMask(true);
        GLES30.GlEnable(GLES30.GlDepthTest);
        GLES30.GlClearDepthf(1.0f);
        GLES30.GlClear(GLES30.GlDepthBufferBit);
        GLES30.GlUseProgram(_shadowProgram);
        GLES30.GlDisable(0x0B44); // GL_CULL_FACE
        foreach (PmxGpuModel model in _models.Where(model => model.CastsShadows))
        {
            model.BindSkinning(_shadowUseGpuSkinningLocation, _shadowBonesLocation);
            GLES30.GlUniformMatrix4fv(_shadowMvpLocation, 1, false, ToGlArray(model.Transform * _lightViewProjection), 0);
            model.DrawDepth();
        }
        GLES30.GlColorMask(true, true, true, true);
        GLES30.GlBindFramebuffer(GLES30.GlFramebuffer, 0);
    }

    private static Matrix4x4 CreateOrthographicProjection(float width, float height, float near, float far)
    {
        return new Matrix4x4(
            2.0f / width, 0.0f, 0.0f, 0.0f,
            0.0f, 2.0f / height, 0.0f, 0.0f,
            0.0f, 0.0f, 2.0f / (near - far), 0.0f,
            0.0f, 0.0f, (far + near) / (near - far), 1.0f);
    }

    private static (int Framebuffer, int DepthTexture, int ColorTexture, bool Available) CreateShadowMapResources()
    {
        int[] framebuffers = new int[1];
        int[] textures = new int[2];
        GLES30.GlGenFramebuffers(1, framebuffers, 0);
        GLES30.GlGenTextures(2, textures, 0);
        int framebuffer = framebuffers[0];
        int depthTexture = textures[0];
        int colorTexture = textures[1];
        GLES30.GlBindTexture(GLES30.GlTexture2d, depthTexture);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMinFilter, GLES30.GlNearest);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMagFilter, GLES30.GlNearest);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapS, GLES30.GlClampToEdge);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapT, GLES30.GlClampToEdge);
        GLES30.GlTexImage2D(GLES30.GlTexture2d, 0, 0x81A6, ShadowMapSize, ShadowMapSize, 0, 0x1902, GLES30.GlUnsignedInt, null);
        GLES30.GlBindTexture(GLES30.GlTexture2d, colorTexture);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMinFilter, GLES30.GlNearest);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMagFilter, GLES30.GlNearest);
        GLES30.GlTexImage2D(GLES30.GlTexture2d, 0, GLES30.GlRgba, ShadowMapSize, ShadowMapSize, 0, GLES30.GlRgba, GLES30.GlUnsignedByte, null);
        GLES30.GlBindFramebuffer(GLES30.GlFramebuffer, framebuffer);
        GLES30.GlFramebufferTexture2D(GLES30.GlFramebuffer, 0x8CE0, GLES30.GlTexture2d, colorTexture, 0);
        GLES30.GlFramebufferTexture2D(GLES30.GlFramebuffer, 0x8D00, GLES30.GlTexture2d, depthTexture, 0);
        bool available = GLES30.GlCheckFramebufferStatus(GLES30.GlFramebuffer) == GLES30.GlFramebufferComplete;
        GLES30.GlBindFramebuffer(GLES30.GlFramebuffer, 0);
        if (!available)
        {
            Log.Warn(LogTag, "Android directional shadow map is unavailable on this GLES device; PMX shadows are disabled.");
        }
        return (framebuffer, depthTexture, colorTexture, available);
    }

    private static int[] GetUniformLocations(int program, string name, int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => GLES30.GlGetUniformLocation(program, $"{name}[{index}]"))
            .ToArray();
    }

    private void ClearModels()
    {
        foreach (PmxGpuModel model in _models)
        {
            model.Dispose();
        }

        _models.Clear();
        foreach (PlaneGpu plane in _planes)
        {
            plane.Dispose();
        }
        _planes.Clear();
        foreach (ParticleGpu particle in _particles)
        {
            particle.Dispose();
        }
        _particles.Clear();
        foreach (WaterGpu water in _waters)
        {
            water.Dispose();
        }
        _waters.Clear();
        foreach (RenderTargetGpu target in _renderTargets.Values.Distinct())
        {
            target.Dispose();
        }
        _renderTargets.Clear();
        _skyboxTexture = 0;
        _updateOrder.Clear();
        if (_textures.Count > 0)
        {
            GLES30.GlDeleteTextures(_textures.Count, [.. _textures.Values], 0);
            _textures.Clear();
            _softAlphaTextures.Clear();
        }
    }

    private void ResolveRelations()
    {
        Dictionary<string, PmxGpuModel> models = new(StringComparer.OrdinalIgnoreCase);
        foreach (PmxGpuModel model in _models)
        {
            models.TryAdd(model.EntityId, model);
            models.TryAdd(model.EntityName, model);
        }
        foreach (PmxGpuModel model in _models)
        {
            if (model.RelationEnabled
                && models.TryGetValue(model.RelationEntity, out PmxGpuModel? relation)
                && !ReferenceEquals(model, relation))
            {
                model.RelationTarget = relation;
                Log.Info(LogTag, $"Android PMX relation bound: '{model.EntityName}' -> '{relation.EntityName}'.");
                if (model.RelationBindLighting)
                {
                    Log.Info(LogTag, $"Android PMX relation lighting for '{model.EntityName}' resolves to the shared scene lighting state.");
                }
            }
            else if (model.RelationEnabled)
            {
                Log.Warn(LogTag, $"Android PMX relation target was not found for '{model.EntityName}': '{model.RelationEntity}'.");
            }
        }

        _updateOrder.Clear();
        HashSet<PmxGpuModel> visiting = [];
        HashSet<PmxGpuModel> visited = [];
        foreach (PmxGpuModel model in _models)
        {
            Visit(model, visiting, visited);
        }

        void Visit(PmxGpuModel model, HashSet<PmxGpuModel> active, HashSet<PmxGpuModel> complete)
        {
            if (complete.Contains(model))
            {
                return;
            }
            if (!active.Add(model))
            {
                Log.Warn(LogTag, $"Android PMX relation cycle detected at '{model.EntityName}'. The cyclic edge was ignored.");
                model.RelationTarget = null;
                return;
            }
            if (model.RelationTarget is not null)
            {
                Visit(model.RelationTarget, active, complete);
            }
            active.Remove(model);
            complete.Add(model);
            _updateOrder.Add(model);
        }
    }

    private PmxGpuModel? FindModel(string entityIdOrName)
    {
        return _models.FirstOrDefault(model =>
            string.Equals(model.EntityId, entityIdOrName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(model.EntityName, entityIdOrName, StringComparison.OrdinalIgnoreCase));
    }

    private int LoadTexture(string path)
    {
        string fullPath = ResolveCaseInsensitivePath(path);
        if (_textures.TryGetValue(fullPath, out int existing))
        {
            return existing;
        }

        if (!File.Exists(fullPath))
        {
            Log.Warn(LogTag, $"Android PMX texture was not found: {path}");
            _textures[fullPath] = 0;
            return 0;
        }

        AndroidDecodedTexture decoded;
        try
        {
            decoded = AndroidTextureDecoder.Decode(fullPath);
        }
        catch (Exception ex)
        {
            Log.Warn(LogTag, $"Android could not decode PMX texture '{fullPath}': {ex.Message}");
            _textures[fullPath] = 0;
            return 0;
        }

        int[] ids = new int[1];
        GLES30.GlGenTextures(1, ids, 0);
        int texture = ids[0];
        GLES30.GlBindTexture(GLES30.GlTexture2d, texture);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapS, GLES30.GlRepeat);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapT, GLES30.GlRepeat);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMinFilter, GLES30.GlLinearMipmapLinear);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMagFilter, GLES30.GlLinear);
        using ByteBuffer bytes = ByteBuffer.AllocateDirect(decoded.Rgba.Length)!;
        bytes.Put(decoded.Rgba);
        bytes.Position(0);
        GLES30.GlTexImage2D(
            GLES30.GlTexture2d,
            0,
            GLES30.GlRgba, // Match the current PC PMX gamma-space texture contract.
            decoded.Width,
            decoded.Height,
            0,
            GLES30.GlRgba,
            GLES30.GlUnsignedByte,
            bytes);
        GLES30.GlGenerateMipmap(GLES30.GlTexture2d);
        GLES30.GlBindTexture(GLES30.GlTexture2d, 0);
        if (decoded.HasSoftAlpha)
        {
            _softAlphaTextures.Add(texture);
        }
        _textures[fullPath] = texture;
        return texture;
    }

    private int ResolveSceneTexture(string path, string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(path)) return 0;
        string normalized = GameProjectPath.NormalizePathText(path);
        if (normalized.StartsWith("rt:", StringComparison.OrdinalIgnoreCase))
        {
            string key = normalized[3..].Trim();
            return _renderTargets.TryGetValue(key, out RenderTargetGpu? target) ? target.ColorTexture : 0;
        }
        return LoadTexture(GameProjectPath.ToAbsolute(projectDirectory, normalized));
    }

    private int LoadParticlePresetTexture(string? preset)
    {
        string normalized = (preset ?? "softCircle").Trim().ToLowerInvariant();
        string key = $"__android_particle_{normalized}";
        if (_textures.TryGetValue(key, out int existing))
        {
            return existing;
        }

        const int size = 32;
        byte[] pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size * 2.0f - 1.0f;
                float ny = (y + 0.5f) / size * 2.0f - 1.0f;
                float radius = MathF.Sqrt(nx * nx + ny * ny);
                float alpha = normalized is "streak"
                    ? Math.Clamp(1.0f - MathF.Abs(nx) * 1.35f, 0.0f, 1.0f) * Math.Clamp(1.0f - radius * 0.65f, 0.0f, 1.0f)
                    : normalized is "flame"
                        ? Math.Clamp(1.0f - radius, 0.0f, 1.0f) * Math.Clamp(1.0f - ny * 0.35f, 0.0f, 1.0f)
                        : Math.Clamp(1.0f - radius, 0.0f, 1.0f);
                int offset = (y * size + x) * 4;
                pixels[offset] = 255;
                pixels[offset + 1] = 255;
                pixels[offset + 2] = 255;
                pixels[offset + 3] = (byte)Math.Clamp(alpha * 255.0f, 0.0f, 255.0f);
            }
        }

        int[] ids = new int[1];
        GLES30.GlGenTextures(1, ids, 0);
        GLES30.GlBindTexture(GLES30.GlTexture2d, ids[0]);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapS, GLES30.GlClampToEdge);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapT, GLES30.GlClampToEdge);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMinFilter, GLES30.GlLinear);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMagFilter, GLES30.GlLinear);
        using ByteBuffer bytes = ByteBuffer.AllocateDirect(pixels.Length)!;
        bytes.Put(pixels);
        bytes.Position(0);
        GLES30.GlTexImage2D(GLES30.GlTexture2d, 0, GLES30.GlRgba, size, size, 0, GLES30.GlRgba, GLES30.GlUnsignedByte, bytes);
        GLES30.GlBindTexture(GLES30.GlTexture2d, 0);
        _textures[key] = ids[0];
        return ids[0];
    }

    private int LoadCommonToonTexture(int toonIndex)
    {
        string key = $"__android_common_toon_{Math.Clamp(toonIndex, 0, 9)}";
        if (_textures.TryGetValue(key, out int existing))
        {
            return existing;
        }
        byte[] pixels = new byte[256 * 4];
        float shadow = 0.34f + Math.Clamp(toonIndex, 0, 9) * 0.025f;
        for (int y = 0; y < 256; y++)
        {
            float t = y / 255.0f;
            byte value = (byte)Math.Clamp((shadow + (1.0f - shadow) * SmoothStep(0.35f, 0.72f, t)) * 255.0f, 0.0f, 255.0f);
            int offset = y * 4;
            pixels[offset] = value;
            pixels[offset + 1] = value;
            pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
        int[] ids = new int[1];
        GLES30.GlGenTextures(1, ids, 0);
        GLES30.GlBindTexture(GLES30.GlTexture2d, ids[0]);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapS, GLES30.GlClampToEdge);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapT, GLES30.GlClampToEdge);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMinFilter, GLES30.GlLinear);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMagFilter, GLES30.GlLinear);
        using ByteBuffer bytes = ByteBuffer.AllocateDirect(pixels.Length)!;
        bytes.Put(pixels);
        bytes.Position(0);
        GLES30.GlTexImage2D(GLES30.GlTexture2d, 0, GLES30.GlRgba, 1, 256, 0, GLES30.GlRgba, GLES30.GlUnsignedByte, bytes);
        GLES30.GlBindTexture(GLES30.GlTexture2d, 0);
        return _textures[key] = ids[0];
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = Math.Clamp((value - edge0) / Math.Max(edge1 - edge0, 1e-5f), 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }

    private static IReadOnlyList<(VmdParsing Animation, float Weight)> LoadMotionLayers(GameEntity entity, string projectDirectory)
    {
        List<(VmdParsing Animation, float Weight)> result = [];
        foreach (MotionLayerSettings layer in entity.MotionLayers.Where(layer => !string.IsNullOrWhiteSpace(layer.Path)))
        {
            string motionPath = GameProjectPath.ToAbsolute(projectDirectory, layer.Path);
            if (!File.Exists(motionPath))
            {
                Log.Warn(LogTag, $"Android VMD asset was not found: {motionPath}");
                continue;
            }
            try
            {
                VmdParsing? vmd = VmdParsing.ParsingByFile(motionPath);
                if (vmd is not null)
                {
                    result.Add((vmd, Math.Clamp(layer.Weight, 0.0f, 1.0f)));
                }
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Android VMD load failed for '{entity.Name}': {ex}");
            }
        }
        return result;
    }

    private static string ResolveCaseInsensitivePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            return fullPath;
        }

        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return fullPath;
        }

        string current = root;
        foreach (string segment in fullPath[root.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Directory.Exists(current))
            {
                return fullPath;
            }

            string? match = Directory.EnumerateFileSystemEntries(current)
                .FirstOrDefault(candidate => string.Equals(Path.GetFileName(candidate), segment, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return fullPath;
            }

            current = match;
        }

        return current;
    }

    private static bool IsPmxEntity(GameEntity entity)
    {
        return !string.IsNullOrWhiteSpace(entity.AssetPath)
            && string.Equals(entity.Type, "pmx_model", StringComparison.OrdinalIgnoreCase);
    }

    private static CameraSettings ResolveCamera(GameProjectScene scene)
    {
        SceneCameraSettings? camera = scene.Cameras.FirstOrDefault(candidate => candidate.Enabled && candidate.IsMain)
            ?? scene.Cameras.FirstOrDefault(candidate => candidate.Enabled);
        return camera?.Camera ?? scene.Camera;
    }

    private static Matrix4x4 CreateProjection(CameraSettings camera, float aspect)
    {
        float near = Math.Max(camera.NearClipPlane, 0.001f);
        float far = Math.Max(camera.FarClipPlane, near + 0.001f);
        if (string.Equals(camera.ProjectionMode, "orthographic", StringComparison.OrdinalIgnoreCase))
        {
            float height = Math.Max(camera.OrthographicSize * 2.0f, 0.001f);
            float width = height * Math.Max(aspect, 0.001f);
            return Matrix4x4.CreateOrthographic(width, height, near, far);
        }

        float fov = Math.Clamp(camera.Fov, 1.0f, 179.0f) * (MathF.PI / 180.0f);
        float y = 1.0f / MathF.Tan(fov * 0.5f);
        float x = y / Math.Max(aspect, 0.001f);
        return new Matrix4x4(
            x, 0.0f, 0.0f, 0.0f,
            0.0f, y, 0.0f, 0.0f,
            0.0f, 0.0f, (far + near) / (near - far), -1.0f,
            0.0f, 0.0f, (2.0f * far * near) / (near - far), 0.0f);
    }

    private static Vector3 NormalizeOrDefault(Vector3 value, Vector3 fallback)
    {
        return value.LengthSquared() > 1e-8f ? Vector3.Normalize(value) : fallback;
    }

    private static float[] ToGlArray(Matrix4x4 matrix)
    {
        return
        [
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44
        ];
    }

    private static int CreateProgram(string vertexSource, string fragmentSource)
    {
        int vertexShader = CompileShader(GLES30.GlVertexShader, vertexSource);
        int fragmentShader = CompileShader(GLES30.GlFragmentShader, fragmentSource);
        int program = GLES30.GlCreateProgram();
        GLES30.GlAttachShader(program, vertexShader);
        GLES30.GlAttachShader(program, fragmentShader);
        GLES30.GlLinkProgram(program);

        int[] linked = new int[1];
        GLES30.GlGetProgramiv(program, GLES30.GlLinkStatus, linked, 0);
        GLES30.GlDeleteShader(vertexShader);
        GLES30.GlDeleteShader(fragmentShader);
        if (linked[0] == 0)
        {
            string log = GLES30.GlGetProgramInfoLog(program) ?? "unknown link error";
            GLES30.GlDeleteProgram(program);
            throw new InvalidOperationException($"Android PMX shader link failed: {log}");
        }

        return program;
    }

    private static int CompileShader(int type, string source)
    {
        int shader = GLES30.GlCreateShader(type);
        GLES30.GlShaderSource(shader, source);
        GLES30.GlCompileShader(shader);
        int[] compiled = new int[1];
        GLES30.GlGetShaderiv(shader, GLES30.GlCompileStatus, compiled, 0);
        if (compiled[0] == 0)
        {
            string log = GLES30.GlGetShaderInfoLog(shader) ?? "unknown compile error";
            GLES30.GlDeleteShader(shader);
            throw new InvalidOperationException($"Android PMX shader compilation failed: {log}");
        }

        return shader;
    }

    private sealed class RenderTargetGpu : IDisposable
    {
        private bool _disposed;

        private RenderTargetGpu(int framebuffer, int colorTexture, int depthBuffer, int width, int height, Vector4 clearColor, string refreshMode, float refreshInterval)
        {
            Framebuffer = framebuffer;
            ColorTexture = colorTexture;
            DepthBuffer = depthBuffer;
            Width = width;
            Height = height;
            ClearColor = clearColor;
            RefreshMode = refreshMode;
            RefreshInterval = refreshInterval;
            LastRenderedSeconds = double.NegativeInfinity;
        }

        public int Framebuffer { get; }
        public int ColorTexture { get; }
        public int DepthBuffer { get; }
        public int Width { get; }
        public int Height { get; }
        public Vector4 ClearColor { get; }
        public string RefreshMode { get; }
        public float RefreshInterval { get; }
        private double LastRenderedSeconds { get; set; }

        public bool ShouldRefresh(double timeSeconds)
        {
            if (string.Equals(RefreshMode, "manual", StringComparison.OrdinalIgnoreCase))
            {
                return double.IsNegativeInfinity(LastRenderedSeconds);
            }
            if (string.Equals(RefreshMode, "interval", StringComparison.OrdinalIgnoreCase)
                || string.Equals(RefreshMode, "timed", StringComparison.OrdinalIgnoreCase))
            {
                return double.IsNegativeInfinity(LastRenderedSeconds)
                    || timeSeconds - LastRenderedSeconds >= Math.Max(RefreshInterval, 0.001f);
            }
            return true;
        }

        public void MarkRendered(double timeSeconds) => LastRenderedSeconds = timeSeconds;

        public static RenderTargetGpu Create(RenderTextureSettings settings)
        {
            int width = Math.Clamp(settings.Width, 16, 2048);
            int height = Math.Clamp(settings.Height, 16, 2048);
            int[] framebuffers = new int[1];
            int[] textures = new int[1];
            int[] renderbuffers = new int[1];
            GLES30.GlGenFramebuffers(1, framebuffers, 0);
            GLES30.GlGenTextures(1, textures, 0);
            GLES30.GlGenRenderbuffers(1, renderbuffers, 0);
            GLES30.GlBindTexture(GLES30.GlTexture2d, textures[0]);
            GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMinFilter, GLES30.GlLinear);
            GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMagFilter, GLES30.GlLinear);
            GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapS, GLES30.GlClampToEdge);
            GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapT, GLES30.GlClampToEdge);
            GLES30.GlTexImage2D(GLES30.GlTexture2d, 0, GLES30.GlRgba, width, height, 0, GLES30.GlRgba, GLES30.GlUnsignedByte, null);
            GLES30.GlBindRenderbuffer(GLES30.GlRenderbuffer, renderbuffers[0]);
            GLES30.GlRenderbufferStorage(GLES30.GlRenderbuffer, 0x81A5, width, height); // GL_DEPTH_COMPONENT24
            GLES30.GlBindFramebuffer(GLES30.GlFramebuffer, framebuffers[0]);
            GLES30.GlFramebufferTexture2D(GLES30.GlFramebuffer, 0x8CE0, GLES30.GlTexture2d, textures[0], 0);
            GLES30.GlFramebufferRenderbuffer(GLES30.GlFramebuffer, 0x8D00, GLES30.GlRenderbuffer, renderbuffers[0]);
            bool complete = GLES30.GlCheckFramebufferStatus(GLES30.GlFramebuffer) == GLES30.GlFramebufferComplete;
            GLES30.GlBindFramebuffer(GLES30.GlFramebuffer, 0);
            GLES30.GlBindTexture(GLES30.GlTexture2d, 0);
            GLES30.GlBindRenderbuffer(GLES30.GlRenderbuffer, 0);
            if (!complete)
            {
                GLES30.GlDeleteFramebuffers(1, framebuffers, 0);
                GLES30.GlDeleteTextures(1, textures, 0);
                GLES30.GlDeleteRenderbuffers(1, renderbuffers, 0);
                throw new InvalidOperationException("GLES RenderTexture framebuffer is incomplete.");
            }
            return new RenderTargetGpu(
                framebuffers[0],
                textures[0],
                renderbuffers[0],
                width,
                height,
                settings.ClearColor.ToVector4(),
                settings.RefreshMode,
                settings.RefreshIntervalSeconds);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GLES30.GlDeleteFramebuffers(1, [Framebuffer], 0);
            GLES30.GlDeleteTextures(1, [ColorTexture], 0);
            GLES30.GlDeleteRenderbuffers(1, [DepthBuffer], 0);
        }
    }

    private sealed class WaterGpu : IDisposable
    {
        private const int VertexFloatCount = 8;
        private const int VertexStride = VertexFloatCount * sizeof(float);
        private readonly RuntimeEntity _runtimeEntity;
        private readonly WaterSurfaceSettings _settings;
        private readonly int _vao;
        private readonly int _vbo;
        private readonly int _resolution;
        private readonly float[] _vertices;
        private readonly ByteBuffer _vertexBytes;
        private readonly FloatBuffer _vertexData;
        private bool _disposed;

        private WaterGpu(RuntimeEntity runtimeEntity, int vao, int vbo, int resolution, ByteBuffer bytes, FloatBuffer data)
        {
            _runtimeEntity = runtimeEntity;
            _settings = runtimeEntity.Definition.Water;
            _vao = vao;
            _vbo = vbo;
            _resolution = resolution;
            _vertices = new float[(resolution - 1) * (resolution - 1) * 6 * VertexFloatCount];
            _vertexBytes = bytes;
            _vertexData = data;
        }

        public float Alpha => _settings.Alpha;
        public Vector3 DeepColor => _settings.DeepColor.ToVector3();
        public Vector3 ReflectionTint => _settings.ReflectionTint.ToVector3();
        public float SurfaceY => _runtimeEntity.Position.Y;
        public bool UnderwaterEffectEnabled => _settings.UnderwaterEffectEnabled;
        public float UnderwaterFogDensity => _settings.UnderwaterFogDensity;
        public float UnderwaterVisibilityDistance => _settings.UnderwaterVisibilityDistance;
        public Vector3 UnderwaterFogColor => _settings.UnderwaterFogColor.ToVector3();

        public static WaterGpu Create(RuntimeEntity runtimeEntity)
        {
            int resolution = Math.Clamp(runtimeEntity.Definition.Water.GerstnerMeshResolution, 8, 48);
            int vertexCount = (resolution - 1) * (resolution - 1) * 6;
            int[] arrays = new int[1];
            int[] buffers = new int[1];
            GLES30.GlGenVertexArrays(1, arrays, 0);
            GLES30.GlGenBuffers(1, buffers, 0);
            GLES30.GlBindVertexArray(arrays[0]);
            GLES30.GlBindBuffer(GLES30.GlArrayBuffer, buffers[0]);
            ByteBuffer bytes = ByteBuffer.AllocateDirect(vertexCount * VertexStride)!;
            bytes.Order(ByteOrder.NativeOrder()!);
            FloatBuffer data = bytes.AsFloatBuffer();
            GLES30.GlBufferData(GLES30.GlArrayBuffer, vertexCount * VertexStride, data, GLES30.GlDynamicDraw);
            GLES30.GlEnableVertexAttribArray(0);
            GLES30.GlVertexAttribPointer(0, 3, GLES30.GlFloat, false, VertexStride, 0);
            GLES30.GlEnableVertexAttribArray(1);
            GLES30.GlVertexAttribPointer(1, 3, GLES30.GlFloat, false, VertexStride, 3 * sizeof(float));
            GLES30.GlEnableVertexAttribArray(2);
            GLES30.GlVertexAttribPointer(2, 2, GLES30.GlFloat, false, VertexStride, 6 * sizeof(float));
            GLES30.GlBindVertexArray(0);
            return new WaterGpu(runtimeEntity, arrays[0], buffers[0], resolution, bytes, data);
        }

        public void Draw(double timeSeconds)
        {
            float size = Math.Max(MathF.Abs(_settings.Size), 0.001f);
            float time = (float)Math.Max(timeSeconds, 0.0) * Math.Max(_settings.AnimationSpeed, 0.0f);
            Matrix4x4 transform = _runtimeEntity.TransformMatrix;
            int offset = 0;
            for (int z = 0; z < _resolution - 1; z++)
            {
                for (int x = 0; x < _resolution - 1; x++)
                {
                    float x0 = (x / (float)(_resolution - 1) - 0.5f) * size;
                    float x1 = ((x + 1) / (float)(_resolution - 1) - 0.5f) * size;
                    float z0 = (z / (float)(_resolution - 1) - 0.5f) * size;
                    float z1 = ((z + 1) / (float)(_resolution - 1) - 0.5f) * size;
                    WriteGerstnerVertex(ref offset, x0, z0, x / (float)(_resolution - 1), z / (float)(_resolution - 1), time, transform);
                    WriteGerstnerVertex(ref offset, x1, z0, (x + 1) / (float)(_resolution - 1), z / (float)(_resolution - 1), time, transform);
                    WriteGerstnerVertex(ref offset, x1, z1, (x + 1) / (float)(_resolution - 1), (z + 1) / (float)(_resolution - 1), time, transform);
                    WriteGerstnerVertex(ref offset, x0, z0, x / (float)(_resolution - 1), z / (float)(_resolution - 1), time, transform);
                    WriteGerstnerVertex(ref offset, x1, z1, (x + 1) / (float)(_resolution - 1), (z + 1) / (float)(_resolution - 1), time, transform);
                    WriteGerstnerVertex(ref offset, x0, z1, x / (float)(_resolution - 1), (z + 1) / (float)(_resolution - 1), time, transform);
                }
            }

            _vertexData.Position(0);
            _vertexData.Put(_vertices);
            _vertexData.Position(0);
            GLES30.GlBindBuffer(GLES30.GlArrayBuffer, _vbo);
            GLES30.GlBufferSubData(GLES30.GlArrayBuffer, 0, _vertices.Length * sizeof(float), _vertexData);
            GLES30.GlBindBuffer(GLES30.GlArrayBuffer, 0);
            GLES30.GlBindVertexArray(_vao);
            GLES30.GlDrawArrays(GLES30.GlTriangles, 0, _vertices.Length / VertexFloatCount);
        }

        private void WriteGerstnerVertex(ref int offset, float x, float z, float u, float v, float time, Matrix4x4 transform)
        {
            EvaluateGerstner(x, z, time, out Vector3 position, out Vector3 normal);
            position = Vector3.Transform(position, transform);
            normal = Vector3.Normalize(Vector3.TransformNormal(normal, transform));
            _vertices[offset++] = position.X;
            _vertices[offset++] = position.Y;
            _vertices[offset++] = position.Z;
            _vertices[offset++] = normal.X;
            _vertices[offset++] = normal.Y;
            _vertices[offset++] = normal.Z;
            _vertices[offset++] = u * Math.Max(_settings.NormalTiling, 0.001f);
            _vertices[offset++] = v * Math.Max(_settings.NormalTiling, 0.001f);
        }

        private void EvaluateGerstner(float x, float z, float time, out Vector3 displacement, out Vector3 normal)
        {
            float baseAngle = _settings.GerstnerDirectionDegrees * MathF.PI / 180.0f;
            Vector2 baseDirection = new(MathF.Cos(baseAngle), MathF.Sin(baseAngle));
            displacement = Vector3.Zero;
            Vector2 gradient = Vector2.Zero;
            int waveCount = Math.Clamp(_settings.GerstnerWaveCount, 1, 4);
            for (int i = 0; i < waveCount; i++)
            {
                float angle = (i - 1.5f) * 0.75f;
                Vector2 direction = Vector2.Normalize(new Vector2(
                    baseDirection.X * MathF.Cos(angle) - baseDirection.Y * MathF.Sin(angle),
                    baseDirection.X * MathF.Sin(angle) + baseDirection.Y * MathF.Cos(angle)));
                float amplitude = Math.Max(_settings.GerstnerAmplitude, 0.0f) * MathF.Pow(0.55f, i);
                float wavelength = Math.Max(_settings.GerstnerWavelength, 0.1f) / (1.0f + i * 0.55f);
                float speed = _settings.GerstnerSpeed * (1.0f + i * 0.18f);
                float waveNumber = 2.0f * MathF.PI / wavelength;
                float phase = waveNumber * (direction.X * x + direction.Y * z - speed * time);
                float sine = MathF.Sin(phase);
                float cosine = MathF.Cos(phase);
                float steepness = Math.Clamp(_settings.GerstnerSteepness, 0.0f, 1.0f);
                displacement += new Vector3(direction.X * steepness * amplitude * cosine, amplitude * sine, direction.Y * steepness * amplitude * cosine);
                gradient += direction * amplitude * waveNumber * cosine;
            }
            normal = Vector3.Normalize(new Vector3(-gradient.X, 1.0f, -gradient.Y));
            if (!_settings.GerstnerWavesEnabled)
            {
                displacement = Vector3.Zero;
                normal = Vector3.UnitY;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GLES30.GlDeleteVertexArrays(1, [_vao], 0);
            GLES30.GlDeleteBuffers(1, [_vbo], 0);
            _vertexData.Dispose();
            _vertexBytes.Dispose();
        }
    }

    private sealed class ParticleGpu : IDisposable
    {
        private const int VertexFloatCount = 9;
        private const int VertexStride = VertexFloatCount * sizeof(float);
        private readonly RuntimeEntity _runtimeEntity;
        private readonly ParticleEntitySettings _settings;
        private readonly int _vao;
        private readonly int _vbo;
        private readonly float[] _vertices;
        private readonly ByteBuffer _vertexBytes;
        private readonly FloatBuffer _vertexData;
        private bool _disposed;

        private ParticleGpu(RuntimeEntity runtimeEntity, int textureId, int vao, int vbo, int count, ByteBuffer bytes, FloatBuffer data)
        {
            _runtimeEntity = runtimeEntity;
            _settings = runtimeEntity.Definition.Particle;
            TextureId = textureId;
            _vao = vao;
            _vbo = vbo;
            Count = count;
            _vertices = new float[count * 6 * VertexFloatCount];
            _vertexBytes = bytes;
            _vertexData = data;
        }

        public int TextureId { get; }
        public int Count { get; }
        public bool Additive => string.Equals(_settings.BlendMode, "additive", StringComparison.OrdinalIgnoreCase);
        public bool UseTextureColor => _settings.UseTextureColor;
        public float Opacity => _settings.Opacity;

        public static ParticleGpu Create(RuntimeEntity runtimeEntity, int textureId)
        {
            int count = Math.Clamp(runtimeEntity.Definition.Particle.ParticleCount, 1, 2000);
            int[] arrays = new int[1];
            int[] buffers = new int[1];
            GLES30.GlGenVertexArrays(1, arrays, 0);
            GLES30.GlGenBuffers(1, buffers, 0);
            GLES30.GlBindVertexArray(arrays[0]);
            GLES30.GlBindBuffer(GLES30.GlArrayBuffer, buffers[0]);
            int byteCount = count * 6 * VertexStride;
            ByteBuffer bytes = ByteBuffer.AllocateDirect(byteCount)!;
            bytes.Order(ByteOrder.NativeOrder()!);
            FloatBuffer data = bytes.AsFloatBuffer();
            GLES30.GlBufferData(GLES30.GlArrayBuffer, byteCount, data, GLES30.GlDynamicDraw);
            GLES30.GlEnableVertexAttribArray(0);
            GLES30.GlVertexAttribPointer(0, 3, GLES30.GlFloat, false, VertexStride, 0);
            GLES30.GlEnableVertexAttribArray(1);
            GLES30.GlVertexAttribPointer(1, 2, GLES30.GlFloat, false, VertexStride, 3 * sizeof(float));
            GLES30.GlEnableVertexAttribArray(2);
            GLES30.GlVertexAttribPointer(2, 4, GLES30.GlFloat, false, VertexStride, 5 * sizeof(float));
            GLES30.GlBindVertexArray(0);
            return new ParticleGpu(runtimeEntity, textureId, arrays[0], buffers[0], count, bytes, data);
        }

        public void Draw(double timeSeconds, Vector3 right, Vector3 up)
        {
            float speed = Math.Max(_settings.SimulationSpeed, 0.0f);
            Matrix4x4 transform = _runtimeEntity.TransformMatrix;
            Vector3 spawnExtents = Vector3.Max(_settings.SpawnBoxHalfExtents.ToVector3(), Vector3.Zero);
            Vector3 baseVelocity = _settings.BaseVelocity.ToVector3();
            Vector3 velocityJitter = _settings.VelocityJitter.ToVector3();
            Vector3 acceleration = _settings.Acceleration.ToVector3();
            Vector4 startColor = _settings.StartColor.ToVector4();
            Vector4 endColor = _settings.EndColor.ToVector4();
            int offset = 0;
            float time = (float)Math.Max(timeSeconds, 0.0) * speed;
            for (int i = 0; i < Count; i++)
            {
                float r0 = Random01(i, 11);
                float r1 = Random01(i, 23);
                float r2 = Random01(i, 37);
                float r3 = Random01(i, 53);
                float lifetime = Lerp(Math.Max(_settings.MinLifetime, 0.05f), Math.Max(_settings.MaxLifetime, 0.05f), r0);
                float age = (time + (r1 * lifetime)) % lifetime;
                float normalizedAge = Math.Clamp(age / lifetime, 0.0f, 1.0f);
                Vector3 spawn = new(
                    (r1 * 2.0f - 1.0f) * spawnExtents.X,
                    (r2 * 2.0f - 1.0f) * spawnExtents.Y,
                    (r3 * 2.0f - 1.0f) * spawnExtents.Z);
                Vector3 velocity = baseVelocity + new Vector3(
                    (Random01(i, 67) * 2.0f - 1.0f) * velocityJitter.X,
                    (Random01(i, 71) * 2.0f - 1.0f) * velocityJitter.Y,
                    (Random01(i, 79) * 2.0f - 1.0f) * velocityJitter.Z);
                Vector3 localPosition = spawn + velocity * age + acceleration * (0.5f * age * age);
                Vector3 worldPosition = Vector3.Transform(localPosition, transform);
                float size = Lerp(Math.Max(_settings.MinSize, 0.001f) * _settings.StartSizeScale,
                    Math.Max(_settings.MaxSize, 0.001f) * _settings.EndSizeScale, normalizedAge);
                float width = size * Math.Max(_settings.WidthScale, 0.001f);
                float height = size * Math.Max(_settings.HeightScale, 0.001f);
                Vector4 color = Vector4.Lerp(startColor, endColor, normalizedAge);
                float rotation = (r2 * 2.0f - 1.0f) * MathF.PI + age * Lerp(_settings.MinRotationSpeedRadians, _settings.MaxRotationSpeedRadians, r3);
                float cos = MathF.Cos(rotation);
                float sin = MathF.Sin(rotation);
                WriteVertex(ref offset, worldPosition + right * (-width * cos - -height * sin) + up * (-width * sin + -height * cos), 0.0f, 1.0f, color);
                WriteVertex(ref offset, worldPosition + right * ( width * cos - -height * sin) + up * ( width * sin + -height * cos), 1.0f, 1.0f, color);
                WriteVertex(ref offset, worldPosition + right * ( width * cos -  height * sin) + up * ( width * sin +  height * cos), 1.0f, 0.0f, color);
                WriteVertex(ref offset, worldPosition + right * (-width * cos - -height * sin) + up * (-width * sin + -height * cos), 0.0f, 1.0f, color);
                WriteVertex(ref offset, worldPosition + right * ( width * cos -  height * sin) + up * ( width * sin +  height * cos), 1.0f, 0.0f, color);
                WriteVertex(ref offset, worldPosition + right * (-width * cos -  height * sin) + up * (-width * sin +  height * cos), 0.0f, 0.0f, color);
            }

            _vertexData.Position(0);
            _vertexData.Put(_vertices);
            _vertexData.Position(0);
            GLES30.GlBindBuffer(GLES30.GlArrayBuffer, _vbo);
            GLES30.GlBufferSubData(GLES30.GlArrayBuffer, 0, _vertices.Length * sizeof(float), _vertexData);
            GLES30.GlBindBuffer(GLES30.GlArrayBuffer, 0);
            GLES30.GlBindVertexArray(_vao);
            GLES30.GlDrawArrays(GLES30.GlTriangles, 0, Count * 6);
        }

        private void WriteVertex(ref int offset, Vector3 position, float u, float v, Vector4 color)
        {
            _vertices[offset++] = position.X;
            _vertices[offset++] = position.Y;
            _vertices[offset++] = position.Z;
            _vertices[offset++] = u;
            _vertices[offset++] = v;
            _vertices[offset++] = color.X;
            _vertices[offset++] = color.Y;
            _vertices[offset++] = color.Z;
            _vertices[offset++] = color.W;
        }

        private static float Random01(int index, int salt)
        {
            uint value = unchecked((uint)(index * 1103515245 + salt * 12345 + 0x13579BDF));
            value ^= value >> 16;
            value *= 2246822519u;
            value ^= value >> 13;
            return (value & 0x00FFFFFF) / 16777215.0f;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            GLES30.GlDeleteVertexArrays(1, [_vao], 0);
            GLES30.GlDeleteBuffers(1, [_vbo], 0);
            _vertexData.Dispose();
            _vertexBytes.Dispose();
        }
    }

    private sealed class PlaneGpu : IDisposable
    {
        private readonly ByteBuffer _vertexBytes;
        private readonly FloatBuffer _vertexData;
        private bool _disposed;

        private PlaneGpu(
            int vertexArrayObject,
            int vertexBuffer,
            int textureId,
            RuntimeEntity runtimeEntity,
            float width,
            float height,
            Vector4 tint,
            ByteBuffer vertexBytes,
            FloatBuffer vertexData)
        {
            VertexArrayObject = vertexArrayObject;
            VertexBuffer = vertexBuffer;
            TextureId = textureId;
            RuntimeEntity = runtimeEntity;
            Width = width;
            Height = height;
            Tint = tint;
            ReceivesShadows = runtimeEntity.Definition.Plane.ReceiveShadow;
            Billboard = runtimeEntity.Definition.Plane.Billboard;
            _vertexBytes = vertexBytes;
            _vertexData = vertexData;
        }

        public int VertexArrayObject { get; }
        public int VertexBuffer { get; }
        public int TextureId { get; }
        public RuntimeEntity RuntimeEntity { get; }
        public float Width { get; }
        public float Height { get; }
        public Vector4 Tint { get; }
        public bool ReceivesShadows { get; }
        public bool Billboard { get; }

        public static PlaneGpu Create(RuntimeEntity runtimeEntity, int textureId)
        {
            TexturedPlaneSettings settings = runtimeEntity.Definition.Plane;
            float width = Math.Max(MathF.Abs(settings.Width), 0.001f);
            float height = Math.Max(MathF.Abs(settings.Height), 0.001f);
            Vector4 sourceTint = settings.Tint.ToVector4();
            Vector4 tint = new(
                Math.Clamp(sourceTint.X, 0.0f, 1.0f),
                Math.Clamp(sourceTint.Y, 0.0f, 1.0f),
                Math.Clamp(sourceTint.Z, 0.0f, 1.0f),
                Math.Clamp(sourceTint.W * settings.Opacity, 0.0f, 1.0f));
            float[] vertices =
            [
                -0.5f, -0.5f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 1.0f, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0,
                 0.5f, -0.5f, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f, 1.0f, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0,
                 0.5f,  0.5f, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f, 0.0f, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0,
                -0.5f, -0.5f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 1.0f, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0,
                 0.5f,  0.5f, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f, 0.0f, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0,
                -0.5f,  0.5f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0,
            ];

            int[] arrays = new int[1];
            int[] buffers = new int[1];
            GLES30.GlGenVertexArrays(1, arrays, 0);
            GLES30.GlGenBuffers(1, buffers, 0);
            GLES30.GlBindVertexArray(arrays[0]);
            GLES30.GlBindBuffer(GLES30.GlArrayBuffer, buffers[0]);
            ByteBuffer bytes = ByteBuffer.AllocateDirect(vertices.Length * sizeof(float))!;
            bytes.Order(ByteOrder.NativeOrder()!);
            FloatBuffer data = bytes.AsFloatBuffer();
            data.Put(vertices);
            data.Position(0);
            GLES30.GlBufferData(GLES30.GlArrayBuffer, vertices.Length * sizeof(float), data, GLES30.GlStaticDraw);
            GLES30.GlEnableVertexAttribArray(0);
            GLES30.GlVertexAttribPointer(0, 3, GLES30.GlFloat, false, VertexStride, 0);
            GLES30.GlEnableVertexAttribArray(1);
            GLES30.GlVertexAttribPointer(1, 3, GLES30.GlFloat, false, VertexStride, 3 * sizeof(float));
            GLES30.GlEnableVertexAttribArray(2);
            GLES30.GlVertexAttribPointer(2, 2, GLES30.GlFloat, false, VertexStride, 6 * sizeof(float));
            GLES30.GlEnableVertexAttribArray(3);
            GLES30.GlVertexAttribPointer(3, 4, GLES30.GlFloat, false, VertexStride, 8 * sizeof(float));
            GLES30.GlEnableVertexAttribArray(4);
            GLES30.GlVertexAttribPointer(4, 4, GLES30.GlFloat, false, VertexStride, 12 * sizeof(float));
            GLES30.GlEnableVertexAttribArray(5);
            GLES30.GlVertexAttribPointer(5, 1, GLES30.GlFloat, false, VertexStride, 16 * sizeof(float));
            GLES30.GlEnableVertexAttribArray(6);
            GLES30.GlVertexAttribPointer(6, 4, GLES30.GlFloat, false, VertexStride, 17 * sizeof(float));
            GLES30.GlBindVertexArray(0);
            return new PlaneGpu(arrays[0], buffers[0], textureId, runtimeEntity, width, height, tint, bytes, data);
        }

        public Matrix4x4 CreateWorld(RuntimeCamera camera)
        {
            if (!Billboard)
            {
                return Matrix4x4.CreateScale(Width, Height, 1.0f) * RuntimeEntity.TransformMatrix;
            }

            Vector3 position = RuntimeEntity.Position;
            Matrix4x4 billboard = Matrix4x4.CreateBillboard(
                position,
                camera.Settings.Position.ToVector3(),
                Vector3.UnitY,
                -Vector3.UnitZ);
            billboard.Translation = Vector3.Zero;
            Vector3 scale = RuntimeEntity.Scale;
            return Matrix4x4.CreateScale(Width * scale.X, Height * scale.Y, scale.Z)
                * billboard
                * Matrix4x4.CreateTranslation(position);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            GLES30.GlDeleteVertexArrays(1, [VertexArrayObject], 0);
            GLES30.GlDeleteBuffers(1, [VertexBuffer], 0);
            _vertexData.Dispose();
            _vertexBytes.Dispose();
        }
    }

    private sealed class PmxGpuModel : IDisposable
    {
        private readonly int _vao;
        private readonly int _vertexBuffer;
        private readonly int _indexBuffer;
        private readonly MaterialRange[] _materials;
        private readonly float[] _vertices;
        private readonly ByteBuffer _vertexBytes;
        private readonly FloatBuffer _vertexData;
        private readonly PmxPoseEvaluator? _animator;
        private readonly float _playbackSpeed;
        private readonly bool _loopMotion;
        private readonly bool _gpuSkinning;
        private readonly bool _enableEdge;
        private readonly bool _relationBindComponentTransform;
        private readonly RuntimeEntity _runtimeEntity;
        private readonly float[] _boneMatrices = new float[MaxGpuBones * 16];
        private bool _disposed;

        private PmxGpuModel(
            int vao,
            int vertexBuffer,
            int indexBuffer,
            MaterialRange[] materials,
            Matrix4x4 transform,
            float[] vertices,
            ByteBuffer vertexBytes,
            FloatBuffer vertexData,
            PmxPoseEvaluator? animator,
            float playbackSpeed,
            bool loopMotion,
            bool gpuSkinning,
            bool enableEdge,
            string entityId,
            string entityName,
            PmxRelationSettings relation,
            RuntimeEntity runtimeEntity)
        {
            _vao = vao;
            _vertexBuffer = vertexBuffer;
            _indexBuffer = indexBuffer;
            _materials = materials;
            _vertices = vertices;
            _vertexBytes = vertexBytes;
            _vertexData = vertexData;
            _animator = animator;
            _playbackSpeed = playbackSpeed;
            _loopMotion = loopMotion;
            _gpuSkinning = gpuSkinning;
            _enableEdge = enableEdge;
            _relationBindComponentTransform = relation.BindComponentTransform;
            _runtimeEntity = runtimeEntity;
            Transform = transform;
            EntityId = entityId;
            EntityName = entityName;
            RelationEnabled = relation.Enabled;
            RelationEntity = relation.RelationEntity;
            RelationBindLighting = relation.BindLighting;
        }

        public Matrix4x4 Transform { get; private set; }
        public string EntityId { get; }
        public string EntityName { get; }
        public bool RelationEnabled { get; }
        public string RelationEntity { get; }
        public bool RelationBindLighting { get; }
        public PmxGpuModel? RelationTarget { get; set; }
        public string SkinningBackend => _gpuSkinning ? "GPU BDEF" : "CPU fallback";
        public string PhysicsBackend => _animator?.PhysicsBackend ?? "disabled";
        public bool CastsShadows => _runtimeEntity.Definition.EnableShadow;
        public bool ReceivesShadows => _runtimeEntity.Definition.ReceiveShadow;
        public bool UsesToonReceivedShadow => string.Equals(_runtimeEntity.Definition.ReceiveShadowMode, "toon", StringComparison.OrdinalIgnoreCase);

        public void SyncTransform()
        {
            if (RelationTarget is null || !_relationBindComponentTransform)
                Transform = _runtimeEntity.TransformMatrix;
        }

        public bool TrySetMotionLayerState(
            int layerIndex,
            float? frame,
            bool? playing,
            bool? loop,
            float? playbackSpeed,
            float? weight)
        {
            if (_animator is null || (uint)layerIndex >= (uint)_animator.MotionLayerCount)
            {
                return false;
            }
            if (frame.HasValue) _animator.SetMotionLayerFrame(layerIndex, frame.Value);
            if (playing.HasValue) _animator.SetMotionLayerPlaying(layerIndex, playing.Value);
            if (loop.HasValue) _animator.SetMotionLayerLoop(layerIndex, loop.Value);
            if (playbackSpeed.HasValue) _animator.SetMotionLayerPlaybackSpeed(layerIndex, playbackSpeed.Value);
            if (weight.HasValue) _animator.SetMotionLayerWeight(layerIndex, weight.Value);
            return true;
        }

        public string CreatePoseSnapshot()
        {
            return _animator is null
                ? string.Empty
                : PmxRuntimeDiagnostics.FormatPoseSnapshot(
                    _animator.BoneNames,
                    _animator.GlobalTransforms,
                    _animator.SkinTransforms,
                    _animator.MorphWeights);
        }

        public static PmxGpuModel Create(
            PmxParsing pmx,
            TransformSettings transformSettings,
            string modelPath,
            Func<string, int> loadTexture,
            Func<int, int> loadCommonToonTexture,
            Func<int, bool> textureHasSoftAlpha,
            IReadOnlyList<(VmdParsing Animation, float Weight)> motions,
            float playbackSpeed,
            bool loopMotion,
            bool physicsEnabled,
            Vector3 gravity,
            bool resetPhysicsOnLoop,
            bool enableEdge,
            string entityId,
            string entityName,
            PmxRelationSettings relation,
            RuntimeEntity runtimeEntity)
        {
            bool gpuSkinningCandidate = pmx.Bones.Length <= MaxGpuBones
                && motions.All(layer => layer.Animation.Morphs.Length == 0)
                && pmx.Vertices.All(vertex => vertex.WeightType is PmxVertexWeight.BDEF1 or PmxVertexWeight.BDEF2 or PmxVertexWeight.BDEF4);
            float[] vertices = new float[pmx.Vertices.Length * VertexFloatCount];
            for (int i = 0; i < pmx.Vertices.Length; i++)
            {
                PmxVertex vertex = pmx.Vertices[i];
                int offset = i * VertexFloatCount;
                vertices[offset] = vertex.Position.X;
                vertices[offset + 1] = vertex.Position.Y;
                vertices[offset + 2] = -vertex.Position.Z;
                vertices[offset + 3] = vertex.Normal.X;
                vertices[offset + 4] = vertex.Normal.Y;
                vertices[offset + 5] = -vertex.Normal.Z;
                vertices[offset + 6] = vertex.UV.X;
                vertices[offset + 7] = vertex.UV.Y;
                for (int bone = 0; bone < 4; bone++)
                {
                    vertices[offset + 8 + bone] = Math.Max(vertex.BoneIndices[bone], 0);
                    vertices[offset + 12 + bone] = vertex.BoneWeights[bone];
                }
                if (vertex.WeightType == PmxVertexWeight.BDEF1)
                {
                    vertices[offset + 12] = 1.0f;
                }
                else if (vertex.WeightType == PmxVertexWeight.BDEF2)
                {
                    vertices[offset + 13] = 1.0f - vertices[offset + 12];
                }
                vertices[offset + 16] = vertex.EdgeScale;
                Vector4 additionalUv = vertex.AdditionalUV[0];
                vertices[offset + 17] = additionalUv.X;
                vertices[offset + 18] = additionalUv.Y;
                vertices[offset + 19] = additionalUv.Z;
                vertices[offset + 20] = additionalUv.W;
            }

            int[] indices = new int[pmx.Faces.Length * 3];
            for (int i = 0; i < pmx.Faces.Length; i++)
            {
                indices[i * 3] = unchecked((int)pmx.Faces[i].Vertices[2]);
                indices[i * 3 + 1] = unchecked((int)pmx.Faces[i].Vertices[1]);
                indices[i * 3 + 2] = unchecked((int)pmx.Faces[i].Vertices[0]);
            }

            MaterialRange[] materials = CreateMaterialRanges(
                pmx.Materials,
                pmx.Textures,
                indices.Length,
                Path.GetDirectoryName(modelPath) ?? string.Empty,
                loadTexture,
                loadCommonToonTexture,
                textureHasSoftAlpha);
            int[] vertexArrays = new int[1];
            int[] buffers = new int[2];
            GLES30.GlGenVertexArrays(1, vertexArrays, 0);
            GLES30.GlGenBuffers(2, buffers, 0);
            GLES30.GlBindVertexArray(vertexArrays[0]);
            GLES30.GlBindBuffer(GLES30.GlArrayBuffer, buffers[0]);
            ByteBuffer vertexBytes = ByteBuffer.AllocateDirect(vertices.Length * sizeof(float))!;
            vertexBytes.Order(ByteOrder.NativeOrder()!);
            FloatBuffer vertexData = vertexBytes.AsFloatBuffer();
            vertexData.Put(vertices);
            vertexData.Position(0);
            int vertexUsage = motions.Count == 0 ? GLES30.GlStaticDraw : GLES30.GlDynamicDraw;
            GLES30.GlBufferData(GLES30.GlArrayBuffer, vertices.Length * sizeof(float), vertexData, vertexUsage);

            GLES30.GlBindBuffer(GLES30.GlElementArrayBuffer, buffers[1]);
            using ByteBuffer indexBytes = ByteBuffer.AllocateDirect(indices.Length * sizeof(int))!;
            indexBytes.Order(ByteOrder.NativeOrder()!);
            using IntBuffer indexData = indexBytes.AsIntBuffer();
            indexData.Put(indices);
            indexData.Position(0);
            GLES30.GlBufferData(GLES30.GlElementArrayBuffer, indices.Length * sizeof(int), indexData, GLES30.GlStaticDraw);

            GLES30.GlEnableVertexAttribArray(0);
            GLES30.GlVertexAttribPointer(0, 3, GLES30.GlFloat, false, VertexStride, 0);
            GLES30.GlEnableVertexAttribArray(1);
            GLES30.GlVertexAttribPointer(1, 3, GLES30.GlFloat, false, VertexStride, 3 * sizeof(float));
            GLES30.GlEnableVertexAttribArray(2);
            GLES30.GlVertexAttribPointer(2, 2, GLES30.GlFloat, false, VertexStride, 6 * sizeof(float));
            GLES30.GlEnableVertexAttribArray(3);
            GLES30.GlVertexAttribPointer(3, 4, GLES30.GlFloat, false, VertexStride, 8 * sizeof(float));
            GLES30.GlEnableVertexAttribArray(4);
            GLES30.GlVertexAttribPointer(4, 4, GLES30.GlFloat, false, VertexStride, 12 * sizeof(float));
            GLES30.GlEnableVertexAttribArray(5);
            GLES30.GlVertexAttribPointer(5, 1, GLES30.GlFloat, false, VertexStride, 16 * sizeof(float));
            GLES30.GlEnableVertexAttribArray(6);
            GLES30.GlVertexAttribPointer(6, 4, GLES30.GlFloat, false, VertexStride, 17 * sizeof(float));
            GLES30.GlBindVertexArray(0);

            _ = transformSettings;
            Matrix4x4 transform = runtimeEntity.TransformMatrix;
            IPmxPhysicsBridge? physicsBridge = null;
            if (physicsEnabled && pmx.RigidBodies.Length != 0)
            {
                try
                {
                    physicsBridge = new AndroidPmxBulletPhysics(pmx, gravity);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Android Bullet initialization failed; using lightweight fallback: {ex.Message}");
                }
            }
            PmxPoseEvaluator animator = new(pmx, motions, physicsEnabled, gravity, resetPhysicsOnLoop, physicsBridge);
            bool gpuSkinning = gpuSkinningCandidate && animator is not null;
            return new PmxGpuModel(
                vertexArrays[0],
                buffers[0],
                buffers[1],
                materials,
                transform,
                vertices,
                vertexBytes,
                vertexData,
                animator,
                playbackSpeed,
                loopMotion,
                gpuSkinning,
                enableEdge,
                entityId,
                entityName,
                relation,
                runtimeEntity);
        }

        public void UpdateAnimation(double timeSeconds)
        {
            if (_animator is not { RequiresUpdate: true })
            {
                return;
            }

            _animator.Update(timeSeconds, _playbackSpeed, _loopMotion, _vertices, !_gpuSkinning);
            if (!_gpuSkinning)
            {
                UploadCpuVertices();
            }
        }

        public void ApplyRelation()
        {
            if (RelationTarget is null)
            {
                return;
            }
            if (_relationBindComponentTransform)
            {
                Transform = RelationTarget.Transform;
            }
            if (_animator is null || RelationTarget._animator is null)
            {
                return;
            }

            _animator.ApplyRelation(RelationTarget._animator);
            if (!_gpuSkinning)
            {
                _animator.WriteSkinnedVertices(_vertices);
                UploadCpuVertices();
            }
        }

        private void UploadCpuVertices()
        {
            _vertexData.Clear();
            _vertexData.Put(_vertices);
            _vertexData.Position(0);
            GLES30.GlBindBuffer(GLES30.GlArrayBuffer, _vertexBuffer);
            GLES30.GlBufferSubData(GLES30.GlArrayBuffer, 0, _vertices.Length * sizeof(float), _vertexData);
        }

        public void BindSkinning(int useGpuSkinningLocation, int bonesLocation)
        {
            BindGpuSkinning(useGpuSkinningLocation, bonesLocation);
        }

        public void Draw(
            int diffuseLocation,
            int ambientLocation,
            int specularLocation,
            int specularPowerLocation,
            int hasTextureLocation,
            int sphereModeLocation,
            int hasSphereLocation,
            int hasToonLocation,
            int textureMultiplyLocation,
            int textureAddLocation,
            int sphereMultiplyLocation,
            int sphereAddLocation,
            int toonMultiplyLocation,
            int toonAddLocation)
        {
            GLES30.GlBindVertexArray(_vao);
            foreach (MaterialRange material in _materials.OrderBy(material =>
                material.RequiresBlending(GetMaterialState(material)) ? 1 : 0))
            {
                PmxPoseEvaluator.MaterialState state = GetMaterialState(material);
                GLES30.GlDepthMask(!material.RequiresBlending(state));
                GLES30.GlUniform4f(diffuseLocation, state.Diffuse.X, state.Diffuse.Y, state.Diffuse.Z, state.Diffuse.W);
                GLES30.GlUniform3f(ambientLocation, state.Ambient.X, state.Ambient.Y, state.Ambient.Z);
                GLES30.GlUniform3f(specularLocation, state.Specular.X, state.Specular.Y, state.Specular.Z);
                GLES30.GlUniform1f(specularPowerLocation, Math.Max(state.SpecularPower, 0.0f));
                GLES30.GlUniform4f(textureMultiplyLocation, state.TextureMultiply.X, state.TextureMultiply.Y, state.TextureMultiply.Z, state.TextureMultiply.W);
                GLES30.GlUniform4f(textureAddLocation, state.TextureAdd.X, state.TextureAdd.Y, state.TextureAdd.Z, state.TextureAdd.W);
                GLES30.GlUniform4f(sphereMultiplyLocation, state.SphereTextureMultiply.X, state.SphereTextureMultiply.Y, state.SphereTextureMultiply.Z, state.SphereTextureMultiply.W);
                GLES30.GlUniform4f(sphereAddLocation, state.SphereTextureAdd.X, state.SphereTextureAdd.Y, state.SphereTextureAdd.Z, state.SphereTextureAdd.W);
                GLES30.GlUniform4f(toonMultiplyLocation, state.ToonTextureMultiply.X, state.ToonTextureMultiply.Y, state.ToonTextureMultiply.Z, state.ToonTextureMultiply.W);
                GLES30.GlUniform4f(toonAddLocation, state.ToonTextureAdd.X, state.ToonTextureAdd.Y, state.ToonTextureAdd.Z, state.ToonTextureAdd.W);
                if (material.DrawMode.HasFlag(PmxDrawModeFlags.BothFace))
                {
                    GLES30.GlDisable(0x0B44); // GL_CULL_FACE
                }
                else
                {
                    GLES30.GlEnable(0x0B44); // GL_CULL_FACE
                    GLES30.GlCullFace(GLES30.GlBack);
                }
                GLES30.GlActiveTexture(GLES30.GlTexture0);
                GLES30.GlBindTexture(GLES30.GlTexture2d, material.TextureId);
                GLES30.GlUniform1i(hasTextureLocation, material.TextureId == 0 ? 0 : 1);
                GLES30.GlActiveTexture(GLES30.GlTexture1);
                GLES30.GlBindTexture(GLES30.GlTexture2d, material.SphereTextureId);
                GLES30.GlUniform1i(hasSphereLocation, material.SphereTextureId == 0 ? 0 : 1);
                GLES30.GlUniform1i(sphereModeLocation, material.SphereMode);
                GLES30.GlActiveTexture(GLES30.GlTexture2);
                GLES30.GlBindTexture(GLES30.GlTexture2d, material.ToonTextureId);
                GLES30.GlUniform1i(hasToonLocation, material.ToonTextureId == 0 ? 0 : 1);
                GLES30.GlDrawElements(
                    material.PrimitiveMode,
                    material.IndexCount,
                    GLES30.GlUnsignedInt,
                    material.FirstIndex * sizeof(int));
            }
            GLES30.GlDepthMask(true);
            GLES30.GlDisable(0x0B44); // GL_CULL_FACE
        }

        public void DrawEdges(
            int useGpuSkinningLocation,
            int bonesLocation,
            int edgeSizeLocation,
            int edgeColorLocation)
        {
            if (!_enableEdge)
            {
                return;
            }

            BindGpuSkinning(useGpuSkinningLocation, bonesLocation);
            GLES30.GlBindVertexArray(_vao);
            GLES30.GlEnable(0x0B44); // GL_CULL_FACE
            GLES30.GlCullFace(GLES30.GlFront);
            GLES30.GlDepthMask(false);
            foreach (MaterialRange material in _materials.Where(material => material.DrawMode.HasFlag(PmxDrawModeFlags.DrawEdge)))
            {
                PmxPoseEvaluator.MaterialState state = GetMaterialState(material);
                if (state.EdgeSize <= 0.0f || state.EdgeColor.W <= 0.0f)
                {
                    continue;
                }
                GLES30.GlUniform1f(edgeSizeLocation, Math.Max(state.EdgeSize, 0.0f));
                GLES30.GlUniform4f(edgeColorLocation, state.EdgeColor.X, state.EdgeColor.Y, state.EdgeColor.Z, state.EdgeColor.W);
                GLES30.GlDrawElements(
                    GLES30.GlTriangles,
                    material.IndexCount,
                    GLES30.GlUnsignedInt,
                    material.FirstIndex * sizeof(int));
            }
            GLES30.GlDepthMask(true);
            GLES30.GlDisable(0x0B44); // GL_CULL_FACE
        }

        public void DrawDepth()
        {
            GLES30.GlBindVertexArray(_vao);
            foreach (MaterialRange material in _materials)
            {
                GLES30.GlDrawElements(
                    material.PrimitiveMode,
                    material.IndexCount,
                    GLES30.GlUnsignedInt,
                    material.FirstIndex * sizeof(int));
            }
            GLES30.GlBindVertexArray(0);
        }

        private PmxPoseEvaluator.MaterialState GetMaterialState(MaterialRange material)
        {
            return _animator is not null && material.SourceMaterialIndex >= 0
                ? _animator.GetMaterialState(material.SourceMaterialIndex)
                : material.ToMaterialState();
        }

        private void BindGpuSkinning(int useGpuSkinningLocation, int bonesLocation)
        {
            GLES30.GlUniform1i(useGpuSkinningLocation, _gpuSkinning ? 1 : 0);
            if (!_gpuSkinning || _animator is null)
            {
                return;
            }

            ReadOnlySpan<Matrix4x4> transforms = _animator.SkinTransforms;
            for (int i = 0; i < transforms.Length; i++)
            {
                float[] matrix = ToGlArray(transforms[i]);
                Array.Copy(matrix, 0, _boneMatrices, i * 16, 16);
            }
            GLES30.GlUniformMatrix4fv(bonesLocation, transforms.Length, false, _boneMatrices, 0);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            GLES30.GlDeleteBuffers(2, [_vertexBuffer, _indexBuffer], 0);
            GLES30.GlDeleteVertexArrays(1, [_vao], 0);
            _animator?.Dispose();
            _vertexData.Dispose();
            _vertexBytes.Dispose();
        }

        private static MaterialRange[] CreateMaterialRanges(
            IReadOnlyList<PmxMaterial> materials,
            IReadOnlyList<PmxTexture> textures,
            int totalIndexCount,
            string modelDirectory,
            Func<string, int> loadTexture,
            Func<int, int> loadCommonToonTexture,
            Func<int, bool> textureHasSoftAlpha)
        {
            List<MaterialRange> ranges = [];
            int firstIndex = 0;
            for (int materialIndex = 0; materialIndex < materials.Count; materialIndex++)
            {
                PmxMaterial material = materials[materialIndex];
                int count = Math.Clamp(material.FaceVerticesCount, 0, totalIndexCount - firstIndex);
                if (count > 0 && material.Diffuse.W > 0.0f)
                {
                    int textureId = 0;
                    if (material.TextureIndex >= 0 && material.TextureIndex < textures.Count)
                    {
                        string textureName = textures[material.TextureIndex].Name
                            .Replace('\\', Path.DirectorySeparatorChar)
                            .Replace('/', Path.DirectorySeparatorChar);
                        string texturePath = Path.IsPathRooted(textureName)
                            ? textureName
                            : Path.Combine(modelDirectory, textureName);
                        textureId = loadTexture(texturePath);
                    }
                    int sphereTextureId = 0;
                    if (material.SphereTextureIndex >= 0 && material.SphereTextureIndex < textures.Count && material.SphereMode != PmxSphereMode.None)
                    {
                        string spherePath = Path.Combine(modelDirectory, textures[material.SphereTextureIndex].Name.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
                        sphereTextureId = loadTexture(spherePath);
                    }
                    int toonTextureId = 0;
                    if (material.ToonMode == PmxToonMode.Separate && material.ToonTextureIndex >= 0 && material.ToonTextureIndex < textures.Count)
                    {
                        toonTextureId = loadTexture(Path.Combine(modelDirectory, textures[material.ToonTextureIndex].Name.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
                    }
                    else if (material.ToonMode == PmxToonMode.Common)
                    {
                        toonTextureId = loadCommonToonTexture(material.ToonTextureIndex);
                    }

                    int primitiveMode = material.DrawMode.HasFlag(PmxDrawModeFlags.DrawPoint)
                        ? GLES30.GlPoints
                        : material.DrawMode.HasFlag(PmxDrawModeFlags.DrawLine)
                            ? GLES30.GlLines
                            : GLES30.GlTriangles;
                    ranges.Add(new MaterialRange(
                        materialIndex,
                        firstIndex,
                        count,
                        material.Diffuse,
                        material.Ambient,
                        material.Specular,
                        material.SpecularPower,
                        material.EdgeColor,
                        material.EdgeSize,
                        material.DrawMode,
                        primitiveMode,
                        textureHasSoftAlpha(textureId),
                        textureId,
                        sphereTextureId,
                        toonTextureId,
                        (int)material.SphereMode));
                }

                firstIndex += count;
                if (firstIndex >= totalIndexCount)
                {
                    break;
                }
            }

            if (firstIndex < totalIndexCount)
            {
                ranges.Add(new MaterialRange(
                    -1,
                    firstIndex,
                    totalIndexCount - firstIndex,
                    Vector4.One,
                    Vector3.One,
                    Vector3.Zero,
                    0.0f,
                    Vector4.Zero,
                    0.0f,
                    0,
                    GLES30.GlTriangles,
                    false,
                    0,
                    0,
                    0,
                    0));
            }

            return [.. ranges];
        }
    }

    private readonly record struct MaterialRange(
        int SourceMaterialIndex,
        int FirstIndex,
        int IndexCount,
        Vector4 Color,
        Vector3 Ambient,
        Vector3 Specular,
        float SpecularPower,
        Vector4 EdgeColor,
        float EdgeSize,
        PmxDrawModeFlags DrawMode,
        int PrimitiveMode,
        bool TextureHasSoftAlpha,
        int TextureId,
        int SphereTextureId,
        int ToonTextureId,
        int SphereMode)
    {
        public bool RequiresBlending(PmxPoseEvaluator.MaterialState state) => TextureHasSoftAlpha || state.Diffuse.W < 0.999f;

        public PmxPoseEvaluator.MaterialState ToMaterialState() => new(
            Color,
            Ambient,
            Specular,
            SpecularPower,
            EdgeColor,
            EdgeSize,
            Vector4.One,
            Vector4.Zero,
            Vector4.One,
            Vector4.Zero,
            Vector4.One,
            Vector4.Zero);
    }

    private const string VertexShaderSource = """
        #version 300 es
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aTexCoord;
        layout(location = 3) in vec4 aBoneIndices;
        layout(location = 4) in vec4 aBoneWeights;
        layout(location = 6) in vec4 aAdditionalUv1;
        uniform mat4 uMvp;
        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uLightViewProjection;
        uniform int uUseGpuSkinning;
        uniform mat4 uBones[96];
        out vec3 vNormal;
        out vec2 vTexCoord;
        out vec3 vViewNormal;
        out vec3 vWorldPosition;
        out vec2 vSphereSubTextureCoord;
        out vec4 vShadowPosition;

        void main()
        {
            vec3 position = aPosition;
            vec3 normal = aNormal;
            if (uUseGpuSkinning != 0)
            {
                mat4 skin = uBones[int(aBoneIndices.x)] * aBoneWeights.x
                    + uBones[int(aBoneIndices.y)] * aBoneWeights.y
                    + uBones[int(aBoneIndices.z)] * aBoneWeights.z
                    + uBones[int(aBoneIndices.w)] * aBoneWeights.w;
                position = (skin * vec4(aPosition, 1.0)).xyz;
                normal = normalize(mat3(skin) * aNormal);
            }
            gl_Position = uMvp * vec4(position, 1.0);
            mat3 worldNormalMatrix = transpose(inverse(mat3(uModel)));
            vNormal = normalize(worldNormalMatrix * normal);
            vTexCoord = vec2(aTexCoord.x, -aTexCoord.y);
            vViewNormal = normalize(transpose(inverse(mat3(uView * uModel))) * normal);
            vWorldPosition = (uModel * vec4(position, 1.0)).xyz;
            vSphereSubTextureCoord = aAdditionalUv1.xy;
            vShadowPosition = uLightViewProjection * vec4(vWorldPosition, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 300 es
        precision highp float;
        in vec3 vNormal;
        in vec2 vTexCoord;
        in vec3 vViewNormal;
        in vec3 vWorldPosition;
        in vec2 vSphereSubTextureCoord;
        in vec4 vShadowPosition;
        uniform vec4 uDiffuse;
        uniform vec3 uMaterialAmbient;
        uniform vec3 uSpecular;
        uniform float uSpecularPower;
        uniform vec3 uCameraPosition;
        uniform sampler2D uTexture;
        uniform sampler2D uSphereTexture;
        uniform sampler2D uToonTexture;
        uniform int uHasTexture;
        uniform int uHasSphereTexture;
        uniform int uHasToonTexture;
        uniform int uSphereMode;
        uniform vec4 uTextureMultiply;
        uniform vec4 uTextureAdd;
        uniform vec4 uSphereMultiply;
        uniform vec4 uSphereAdd;
        uniform vec4 uToonMultiply;
        uniform vec4 uToonAdd;
        uniform vec3 uLightDirection;
        uniform vec3 uLightColor;
        uniform vec3 uAmbientColor;
        uniform float uAmbientStrength;
        uniform int uPointLightCount;
        uniform vec4 uPointLightPositionRange[8];
        uniform vec4 uPointLightColorIntensity[8];
        uniform int uSpotLightCount;
        uniform vec4 uSpotLightPositionRange[8];
        uniform vec4 uSpotLightDirectionOuter[8];
        uniform vec4 uSpotLightColorIntensity[8];
        uniform vec4 uSpotLightCone[8];
        uniform sampler2D uShadowMap;
        uniform int uHasShadowMap;
        uniform int uReceiveShadow;
        uniform int uShadowMode;
        uniform vec4 uShadowColor;
        out vec4 outColor;

        vec3 applyMultiply(vec3 color, vec4 factor)
        {
            return mix(vec3(1.0), color * factor.rgb, factor.a);
        }

        vec3 applyAdd(vec3 color, vec4 factor)
        {
            return clamp(color + (color - vec3(1.0)) * factor.a, vec3(0.0), vec3(1.0)) + factor.rgb;
        }

        void main()
        {
            vec3 normal = normalize(vNormal);
            float diffuseLight = max(dot(normal, normalize(-uLightDirection)), 0.0);
            vec4 textureColor = uHasTexture != 0 ? texture(uTexture, vTexCoord) : vec4(1.0);
            textureColor.rgb = applyAdd(applyMultiply(textureColor.rgb, uTextureMultiply), uTextureAdd);
            float alpha = uDiffuse.a * textureColor.a;
            if (alpha <= 0.001)
            {
                discard;
            }
            vec3 base = uDiffuse.rgb * textureColor.rgb;
            if (uHasSphereTexture != 0 && uSphereMode != 0)
            {
                vec2 sphereUv = uSphereMode == 3
                    ? vSphereSubTextureCoord
                    : vec2(normalize(vViewNormal).x * 0.5 + 0.5, 1.0 - (normalize(vViewNormal).y * 0.5 + 0.5));
                vec3 sphere = texture(uSphereTexture, sphereUv).rgb;
                sphere = applyAdd(applyMultiply(sphere, uSphereMultiply), uSphereAdd);
                base = uSphereMode == 1 || uSphereMode == 3 ? base * sphere : base + sphere;
            }
            vec3 directional = base * uLightColor * diffuseLight;
            if (uHasToonTexture != 0)
            {
                float toonCoordinate = clamp(diffuseLight * 0.5 + 0.5, 0.0, 1.0);
                vec3 toon = texture(uToonTexture, vec2(0.0, toonCoordinate)).rgb;
                toon = applyAdd(applyMultiply(toon, uToonMultiply), uToonAdd);
                directional *= toon;
            }
            float shadowVisibility = 1.0;
            if (uHasShadowMap != 0 && uReceiveShadow != 0 && vShadowPosition.w > 0.0)
            {
                vec3 shadowCoord = vShadowPosition.xyz / vShadowPosition.w;
                shadowCoord = shadowCoord * 0.5 + 0.5;
                if (all(greaterThanEqual(shadowCoord.xy, vec2(0.0)))
                    && all(lessThanEqual(shadowCoord.xy, vec2(1.0)))
                    && shadowCoord.z >= 0.0 && shadowCoord.z <= 1.0)
                {
                    vec2 texel = vec2(1.0 / 1024.0);
                    float lit = 0.0;
                    for (int y = -1; y <= 0; y++)
                    {
                        for (int x = -1; x <= 0; x++)
                        {
                            float stored = texture(uShadowMap, shadowCoord.xy + vec2(x, y) * texel).r;
                            lit += shadowCoord.z - 0.0015 <= stored ? 1.0 : 0.0;
                        }
                    }
                    shadowVisibility = lit * 0.25;
                    if (uShadowMode != 0)
                    {
                        shadowVisibility = shadowVisibility >= 0.5 ? 1.0 : 0.0;
                    }
                }
            }
            vec3 shadowTint = mix(vec3(1.0), uShadowColor.rgb, (1.0 - shadowVisibility) * uShadowColor.a);
            directional *= shadowTint;
            vec3 viewDirection = normalize(uCameraPosition - vWorldPosition);
            vec3 halfDirection = normalize(viewDirection + normalize(-uLightDirection));
            float specularAmount = diffuseLight > 0.0 && uSpecularPower > 0.0
                ? pow(max(dot(normalize(vNormal), halfDirection), 0.0), uSpecularPower)
                : 0.0;
            vec3 ambient = base * uMaterialAmbient * uAmbientColor * uAmbientStrength;
            vec3 specular = uSpecular * uLightColor * specularAmount;
            vec3 local = vec3(0.0);
            for (int i = 0; i < 8; i++)
            {
                if (i >= uPointLightCount) break;
                vec3 toLight = uPointLightPositionRange[i].xyz - vWorldPosition;
                float distanceToLight = length(toLight);
                float range = max(uPointLightPositionRange[i].w, 0.001);
                float attenuation = pow(clamp(1.0 - distanceToLight / range, 0.0, 1.0), 2.0);
                float ndotl = max(dot(normal, normalize(toLight)), 0.0);
                local += base * uPointLightColorIntensity[i].rgb * uPointLightColorIntensity[i].a * ndotl * attenuation;
            }
            for (int i = 0; i < 8; i++)
            {
                if (i >= uSpotLightCount) break;
                vec3 toLight = uSpotLightPositionRange[i].xyz - vWorldPosition;
                float distanceToLight = length(toLight);
                float range = max(uSpotLightPositionRange[i].w, 0.001);
                float attenuation = pow(clamp(1.0 - distanceToLight / range, 0.0, 1.0), 2.0);
                vec3 lightToFragment = normalize(-toLight);
                float cone = smoothstep(uSpotLightDirectionOuter[i].w, uSpotLightCone[i].x,
                    dot(lightToFragment, normalize(uSpotLightDirectionOuter[i].xyz)));
                float ndotl = max(dot(normal, normalize(toLight)), 0.0);
                local += base * uSpotLightColorIntensity[i].rgb * uSpotLightColorIntensity[i].a * ndotl * attenuation * cone;
            }
            outColor = vec4(clamp(ambient + directional + local + specular, 0.0, 1.0), alpha);
        }
        """;

    private const string ShadowVertexShaderSource = """
        #version 300 es
        layout(location = 0) in vec3 aPosition;
        layout(location = 3) in vec4 aBoneIndices;
        layout(location = 4) in vec4 aBoneWeights;
        uniform mat4 uMvp;
        uniform int uUseGpuSkinning;
        uniform mat4 uBones[96];
        void main()
        {
            vec3 position = aPosition;
            if (uUseGpuSkinning != 0)
            {
                mat4 skin = uBones[int(aBoneIndices.x)] * aBoneWeights.x
                    + uBones[int(aBoneIndices.y)] * aBoneWeights.y
                    + uBones[int(aBoneIndices.z)] * aBoneWeights.z
                    + uBones[int(aBoneIndices.w)] * aBoneWeights.w;
                position = (skin * vec4(position, 1.0)).xyz;
            }
            gl_Position = uMvp * vec4(position, 1.0);
        }
        """;

    private const string ShadowFragmentShaderSource = """
        #version 300 es
        precision highp float;
        out vec4 outColor;
        void main() { outColor = vec4(1.0); }
        """;

    private const string SkyboxVertexShaderSource = """
        #version 300 es
        layout(location = 0) in vec3 aPosition;
        uniform mat4 uMvp;
        out vec3 vDirection;
        void main()
        {
            vDirection = aPosition;
            gl_Position = uMvp * vec4(aPosition, 1.0);
        }
        """;

    private const string SkyboxFragmentShaderSource = """
        #version 300 es
        precision highp float;
        uniform sampler2D uTexture;
        uniform vec3 uTint;
        uniform float uExposure;
        in vec3 vDirection;
        out vec4 outColor;
        const float Pi = 3.14159265359;
        void main()
        {
            vec3 direction = normalize(vDirection);
            vec2 uv = vec2(atan(direction.z, direction.x) / (2.0 * Pi) + 0.5,
                           asin(clamp(direction.y, -1.0, 1.0)) / Pi + 0.5);
            vec3 color = texture(uTexture, uv).rgb * uTint * uExposure;
            outColor = vec4(max(color, vec3(0.0)), 1.0);
        }
        """;

    private const string ParticleVertexShaderSource = """
        #version 300 es
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec2 aTexCoord;
        layout(location = 2) in vec4 aColor;
        uniform mat4 uViewProjection;
        out vec2 vTexCoord;
        out vec4 vColor;
        void main()
        {
            gl_Position = uViewProjection * vec4(aPosition, 1.0);
            vTexCoord = aTexCoord;
            vColor = aColor;
        }
        """;

    private const string ParticleFragmentShaderSource = """
        #version 300 es
        precision mediump float;
        uniform sampler2D uTexture;
        uniform float uOpacity;
        uniform int uUseTextureColor;
        in vec2 vTexCoord;
        in vec4 vColor;
        out vec4 outColor;
        void main()
        {
            vec4 textureColor = texture(uTexture, vTexCoord);
            vec3 rgb = uUseTextureColor != 0 ? vColor.rgb * textureColor.rgb : vColor.rgb;
            float alpha = vColor.a * textureColor.a * uOpacity;
            if (alpha <= 0.001) discard;
            outColor = vec4(rgb, alpha);
        }
        """;

    private const string WaterVertexShaderSource = """
        #version 300 es
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aTexCoord;
        uniform mat4 uViewProjection;
        out vec3 vNormal;
        out vec2 vTexCoord;
        void main() { gl_Position = uViewProjection * vec4(aPosition, 1.0); vNormal = aNormal; vTexCoord = aTexCoord; }
        """;

    private const string WaterFragmentShaderSource = """
        #version 300 es
        precision mediump float;
        uniform vec3 uLightDirection;
        uniform vec3 uLightColor;
        uniform vec3 uAmbientColor;
        uniform vec3 uDeepColor;
        uniform vec3 uReflectionTint;
        uniform float uAlpha;
        uniform sampler2D uSkyTexture;
        uniform int uHasSkyTexture;
        in vec3 vNormal;
        in vec2 vTexCoord;
        out vec4 outColor;
        void main()
        {
            vec3 normal = normalize(vNormal);
            float diffuse = max(dot(normal, normalize(-uLightDirection)), 0.0);
            float horizon = clamp(1.0 - abs(normal.y), 0.0, 1.0);
            vec3 base = mix(uDeepColor, uReflectionTint, 0.25 + horizon * 0.45);
            if (uHasSkyTexture != 0)
            {
                vec3 reflectionDirection = normalize(vec3(normal.x, abs(normal.y), normal.z));
                vec2 skyUv = vec2(atan(reflectionDirection.z, reflectionDirection.x) / 6.2831853 + 0.5,
                                  asin(clamp(reflectionDirection.y, -1.0, 1.0)) / 3.1415926 + 0.5);
                base = mix(base, texture(uSkyTexture, skyUv).rgb, 0.28 + horizon * 0.42);
            }
            vec3 color = base * (uAmbientColor + uLightColor * (0.25 + diffuse * 0.75));
            float ripple = 0.96 + 0.04 * sin(vTexCoord.x * 6.2831 + vTexCoord.y * 4.7123);
            outColor = vec4(max(color * ripple, vec3(0.0)), clamp(uAlpha, 0.0, 1.0));
        }
        """;

    private const string PostVertexShaderSource = """
        #version 300 es
        void main()
        {
            vec2 position = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
            gl_Position = vec4(position * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string PostFragmentShaderSource = """
        #version 300 es
        precision mediump float;
        uniform vec3 uTint;
        uniform float uAlpha;
        out vec4 outColor;
        void main()
        {
            outColor = vec4(max(uTint, vec3(0.0)), clamp(uAlpha, 0.0, 1.0));
        }
        """;

    private const string EdgeVertexShaderSource = """
        #version 300 es
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 3) in vec4 aBoneIndices;
        layout(location = 4) in vec4 aBoneWeights;
        layout(location = 5) in float aEdgeScale;
        uniform mat4 uMvp;
        uniform mat4 uModelView;
        uniform vec2 uScreenSize;
        uniform float uEdgeSize;
        uniform int uUseGpuSkinning;
        uniform mat4 uBones[96];

        void main()
        {
            vec3 position = aPosition;
            vec3 normal = aNormal;
            if (uUseGpuSkinning != 0)
            {
                mat4 skin = uBones[int(aBoneIndices.x)] * aBoneWeights.x
                    + uBones[int(aBoneIndices.y)] * aBoneWeights.y
                    + uBones[int(aBoneIndices.z)] * aBoneWeights.z
                    + uBones[int(aBoneIndices.w)] * aBoneWeights.w;
                position = (skin * vec4(position, 1.0)).xyz;
                normal = normalize(mat3(skin) * normal);
            }

            vec4 clip = uMvp * vec4(position, 1.0);
            vec2 viewNormal = normalize((transpose(inverse(mat3(uModelView))) * normal).xy);
            if (length(viewNormal) > 0.0001)
            {
                clip.xy += viewNormal * uEdgeSize * aEdgeScale * 2.0 * clip.w / max(uScreenSize, vec2(1.0));
            }
            gl_Position = clip;
        }
        """;

    private const string EdgeFragmentShaderSource = """
        #version 300 es
        precision highp float;
        uniform vec4 uEdgeColor;
        out vec4 outColor;

        void main()
        {
            outColor = uEdgeColor;
        }
        """;
}

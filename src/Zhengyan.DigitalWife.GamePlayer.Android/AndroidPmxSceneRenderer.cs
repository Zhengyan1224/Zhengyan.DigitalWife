using Android.Opengl;
using Android.Util;
using Java.Nio;
using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd;
using Bitmap = Android.Graphics.Bitmap;
using BitmapFactory = Android.Graphics.BitmapFactory;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidPmxSceneRenderer : IDisposable
{
    private const string LogTag = "ZhengyanGamePlayer";
    private const int VertexStride = 16 * sizeof(float);
    private const int MaxGpuBones = 96;

    private readonly List<PmxGpuModel> _models = [];
    private readonly Dictionary<string, int> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _program;
    private readonly int _mvpLocation;
    private readonly int _modelLocation;
    private readonly int _diffuseLocation;
    private readonly int _textureLocation;
    private readonly int _hasTextureLocation;
    private readonly int _lightDirectionLocation;
    private readonly int _lightColorLocation;
    private readonly int _ambientColorLocation;
    private readonly int _ambientStrengthLocation;
    private readonly int _sphereLocation;
    private readonly int _toonLocation;
    private readonly int _sphereModeLocation;
    private readonly int _hasSphereLocation;
    private readonly int _hasToonLocation;
    private readonly int _useGpuSkinningLocation;
    private readonly int _bonesLocation;
    private bool _disposed;

    public AndroidPmxSceneRenderer()
    {
        _program = CreateProgram(VertexShaderSource, FragmentShaderSource);
        _mvpLocation = GLES30.GlGetUniformLocation(_program, "uMvp");
        _modelLocation = GLES30.GlGetUniformLocation(_program, "uModel");
        _diffuseLocation = GLES30.GlGetUniformLocation(_program, "uDiffuse");
        _textureLocation = GLES30.GlGetUniformLocation(_program, "uTexture");
        _hasTextureLocation = GLES30.GlGetUniformLocation(_program, "uHasTexture");
        _lightDirectionLocation = GLES30.GlGetUniformLocation(_program, "uLightDirection");
        _lightColorLocation = GLES30.GlGetUniformLocation(_program, "uLightColor");
        _ambientColorLocation = GLES30.GlGetUniformLocation(_program, "uAmbientColor");
        _ambientStrengthLocation = GLES30.GlGetUniformLocation(_program, "uAmbientStrength");
        _sphereLocation = GLES30.GlGetUniformLocation(_program, "uSphereTexture");
        _toonLocation = GLES30.GlGetUniformLocation(_program, "uToonTexture");
        _sphereModeLocation = GLES30.GlGetUniformLocation(_program, "uSphereMode");
        _hasSphereLocation = GLES30.GlGetUniformLocation(_program, "uHasSphereTexture");
        _hasToonLocation = GLES30.GlGetUniformLocation(_program, "uHasToonTexture");
        _useGpuSkinningLocation = GLES30.GlGetUniformLocation(_program, "uUseGpuSkinning");
        _bonesLocation = GLES30.GlGetUniformLocation(_program, "uBones[0]");
    }

    public int ModelCount => _models.Count;

    public void Load(GameProject? project, string? projectDirectory)
    {
        ClearModels();
        if (project is null || string.IsNullOrWhiteSpace(projectDirectory))
        {
            return;
        }

        foreach (GameEntity entity in project.Scene.Entities.Where(IsPmxEntity))
        {
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

                IReadOnlyList<(VmdParsing Animation, float Weight)> motions = LoadMotionLayers(entity, projectDirectory);
                PmxGpuModel gpuModel = PmxGpuModel.Create(
                    pmx,
                    entity.Transform,
                    modelPath,
                    LoadTexture,
                    LoadCommonToonTexture,
                    motions,
                    entity.IsPlaying ? entity.PlaybackSpeed : 0.0f,
                    entity.LoopMotion,
                    entity.EnablePhysics,
                    entity.PhysicsGravity,
                    entity.ResetPhysicsOnMotionLoop);
                _models.Add(gpuModel);
                Log.Info(LogTag, $"Android GLES uploaded PMX '{entity.Name}': vertices={pmx.Vertices.Length}; faces={pmx.Faces.Length}; materials={pmx.Materials.Length}; skinning={gpuModel.SkinningBackend}; layers={motions.Count}; physics={gpuModel.PhysicsBackend}");
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Android PMX upload failed for '{entity.Name}': {ex}");
            }
        }
    }

    public void Draw(GameProject? project, int width, int height, double timeSeconds)
    {
        if (project is null || _models.Count == 0)
        {
            return;
        }

        CameraSettings camera = ResolveCamera(project.Scene);
        Vector3 position = camera.Position.ToVector3();
        Vector3 target = camera.Target.ToVector3();
        Vector3 up = camera.VmdHasUp ? camera.VmdUp.ToVector3() : Vector3.UnitY;
        if (Vector3.DistanceSquared(position, target) < 1e-8f)
        {
            target = position - Vector3.UnitZ;
        }

        Matrix4x4 view = Matrix4x4.CreateLookAt(position, target, NormalizeOrDefault(up, Vector3.UnitY));
        Matrix4x4 projection = CreateProjection(camera, Math.Max(width, 1) / (float)Math.Max(height, 1));
        LightingSettings lighting = project.Scene.Lighting;
        Vector3 lightDirection = NormalizeOrDefault(lighting.LightDirection.ToVector3(), new Vector3(-0.5f, -1.0f, -0.5f));
        Vector3 lightColor = lighting.LightColor.ToVector3();
        Vector3 ambientColor = lighting.AmbientColor.ToVector3();

        GLES30.GlEnable(GLES30.GlDepthTest);
        GLES30.GlDepthFunc(GLES30.GlLequal);
        GLES30.GlEnable(GLES30.GlBlend);
        GLES30.GlBlendFunc(GLES30.GlSrcAlpha, GLES30.GlOneMinusSrcAlpha);
        GLES30.GlDisable(0x0B44); // GL_CULL_FACE
        GLES30.GlUseProgram(_program);
        GLES30.GlUniform3f(_lightDirectionLocation, lightDirection.X, lightDirection.Y, lightDirection.Z);
        GLES30.GlUniform3f(_lightColorLocation, lightColor.X, lightColor.Y, lightColor.Z);
        GLES30.GlUniform3f(_ambientColorLocation, ambientColor.X, ambientColor.Y, ambientColor.Z);
        GLES30.GlUniform1f(_ambientStrengthLocation, Math.Max(lighting.AmbientStrength, 0.0f));
        GLES30.GlUniform1i(_textureLocation, 0);
        GLES30.GlUniform1i(_sphereLocation, 1);
        GLES30.GlUniform1i(_toonLocation, 2);

        foreach (PmxGpuModel model in _models)
        {
            model.UpdateAnimation(timeSeconds, _useGpuSkinningLocation, _bonesLocation);
            Matrix4x4 mvp = model.Transform * view * projection;
            GLES30.GlUniformMatrix4fv(_mvpLocation, 1, false, ToGlArray(mvp), 0);
            GLES30.GlUniformMatrix4fv(_modelLocation, 1, false, ToGlArray(model.Transform), 0);
            model.Draw(_diffuseLocation, _hasTextureLocation, _sphereModeLocation, _hasSphereLocation, _hasToonLocation);
        }

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
        GLES30.GlDeleteProgram(_program);
    }

    private void ClearModels()
    {
        foreach (PmxGpuModel model in _models)
        {
            model.Dispose();
        }

        _models.Clear();
        if (_textures.Count > 0)
        {
            GLES30.GlDeleteTextures(_textures.Count, [.. _textures.Values], 0);
            _textures.Clear();
        }
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

        using BitmapFactory.Options options = new()
        {
            InScaled = false,
            InPremultiplied = false
        };
        using Bitmap? bitmap = BitmapFactory.DecodeFile(fullPath, options);
        if (bitmap is null)
        {
            Log.Warn(LogTag, $"Android could not decode PMX texture '{fullPath}'. Convert unsupported DDS/TGA textures to PNG for the current Android renderer.");
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
        GLUtils.TexImage2D(GLES30.GlTexture2d, 0, bitmap, 0);
        GLES30.GlGenerateMipmap(GLES30.GlTexture2d);
        GLES30.GlBindTexture(GLES30.GlTexture2d, 0);
        _textures[fullPath] = texture;
        return texture;
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

    private sealed class PmxGpuModel : IDisposable
    {
        private readonly int _vao;
        private readonly int _vertexBuffer;
        private readonly int _indexBuffer;
        private readonly MaterialRange[] _materials;
        private readonly float[] _vertices;
        private readonly ByteBuffer _vertexBytes;
        private readonly FloatBuffer _vertexData;
        private readonly AndroidPmxAnimator? _animator;
        private readonly float _playbackSpeed;
        private readonly bool _loopMotion;
        private readonly bool _gpuSkinning;
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
            AndroidPmxAnimator? animator,
            float playbackSpeed,
            bool loopMotion,
            bool gpuSkinning)
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
            Transform = transform;
        }

        public Matrix4x4 Transform { get; }
        public string SkinningBackend => _gpuSkinning ? "GPU BDEF" : "CPU fallback";
        public string PhysicsBackend => _animator?.PhysicsBackend ?? "disabled";

        public static PmxGpuModel Create(
            PmxParsing pmx,
            TransformSettings transformSettings,
            string modelPath,
            Func<string, int> loadTexture,
            Func<int, int> loadCommonToonTexture,
            IReadOnlyList<(VmdParsing Animation, float Weight)> motions,
            float playbackSpeed,
            bool loopMotion,
            bool physicsEnabled,
            Vector3 gravity,
            bool resetPhysicsOnLoop)
        {
            bool gpuSkinningCandidate = pmx.Bones.Length <= MaxGpuBones
                && motions.All(layer => layer.Animation.Morphs.Length == 0)
                && pmx.Vertices.All(vertex => vertex.WeightType is PmxVertexWeight.BDEF1 or PmxVertexWeight.BDEF2 or PmxVertexWeight.BDEF4);
            float[] vertices = new float[pmx.Vertices.Length * 16];
            for (int i = 0; i < pmx.Vertices.Length; i++)
            {
                PmxVertex vertex = pmx.Vertices[i];
                int offset = i * 16;
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
                loadCommonToonTexture);
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
            GLES30.GlBindVertexArray(0);

            Vector3 position = transformSettings.Position.ToVector3();
            Vector3 rotation = transformSettings.RotationDegrees.ToVector3() * (MathF.PI / 180.0f);
            Vector3 scale = transformSettings.Scale.ToVector3();
            Matrix4x4 transform = Matrix4x4.CreateScale(scale)
                * Matrix4x4.CreateRotationX(rotation.X)
                * Matrix4x4.CreateRotationY(rotation.Y)
                * Matrix4x4.CreateRotationZ(rotation.Z)
                * Matrix4x4.CreateTranslation(position);
            AndroidPmxAnimator? animator = motions.Count == 0 && !physicsEnabled
                ? null
                : new AndroidPmxAnimator(pmx, motions, physicsEnabled, gravity, resetPhysicsOnLoop);
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
                gpuSkinning);
        }

        public void UpdateAnimation(double timeSeconds, int useGpuSkinningLocation, int bonesLocation)
        {
            GLES30.GlUniform1i(useGpuSkinningLocation, _gpuSkinning ? 1 : 0);
            if (_animator is not { RequiresUpdate: true })
            {
                return;
            }

            _animator.Update(timeSeconds, _playbackSpeed, _loopMotion, _vertices, !_gpuSkinning);
            if (_gpuSkinning)
            {
                ReadOnlySpan<Matrix4x4> transforms = _animator.SkinTransforms;
                for (int i = 0; i < transforms.Length; i++)
                {
                    float[] matrix = ToGlArray(transforms[i]);
                    Array.Copy(matrix, 0, _boneMatrices, i * 16, 16);
                }
                GLES30.GlUniformMatrix4fv(bonesLocation, transforms.Length, false, _boneMatrices, 0);
                return;
            }

            _vertexData.Clear();
            _vertexData.Put(_vertices);
            _vertexData.Position(0);
            GLES30.GlBindBuffer(GLES30.GlArrayBuffer, _vertexBuffer);
            GLES30.GlBufferSubData(GLES30.GlArrayBuffer, 0, _vertices.Length * sizeof(float), _vertexData);
        }

        public void Draw(int diffuseLocation, int hasTextureLocation, int sphereModeLocation, int hasSphereLocation, int hasToonLocation)
        {
            GLES30.GlBindVertexArray(_vao);
            foreach (MaterialRange material in _materials)
            {
                GLES30.GlUniform4f(diffuseLocation, material.Color.X, material.Color.Y, material.Color.Z, material.Color.W);
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
                    GLES30.GlTriangles,
                    material.IndexCount,
                    GLES30.GlUnsignedInt,
                    material.FirstIndex * sizeof(int));
            }
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
            Func<int, int> loadCommonToonTexture)
        {
            List<MaterialRange> ranges = [];
            int firstIndex = 0;
            foreach (PmxMaterial material in materials)
            {
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

                    ranges.Add(new MaterialRange(firstIndex, count, material.Diffuse, textureId, sphereTextureId, toonTextureId, (int)material.SphereMode));
                }

                firstIndex += count;
                if (firstIndex >= totalIndexCount)
                {
                    break;
                }
            }

            if (firstIndex < totalIndexCount)
            {
                ranges.Add(new MaterialRange(firstIndex, totalIndexCount - firstIndex, Vector4.One, 0, 0, 0, 0));
            }

            return [.. ranges];
        }
    }

    private readonly record struct MaterialRange(int FirstIndex, int IndexCount, Vector4 Color, int TextureId, int SphereTextureId, int ToonTextureId, int SphereMode);

    private const string VertexShaderSource = """
        #version 300 es
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aTexCoord;
        layout(location = 3) in vec4 aBoneIndices;
        layout(location = 4) in vec4 aBoneWeights;
        uniform mat4 uMvp;
        uniform mat4 uModel;
        uniform int uUseGpuSkinning;
        uniform mat4 uBones[96];
        out vec3 vNormal;
        out vec2 vTexCoord;
        out vec3 vViewNormal;

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
            vNormal = normalize(mat3(uModel) * normal);
            vTexCoord = vec2(aTexCoord.x, -aTexCoord.y);
            vViewNormal = normalize(aNormal);
        }
        """;

    private const string FragmentShaderSource = """
        #version 300 es
        precision highp float;
        in vec3 vNormal;
        in vec2 vTexCoord;
        in vec3 vViewNormal;
        uniform vec4 uDiffuse;
        uniform sampler2D uTexture;
        uniform sampler2D uSphereTexture;
        uniform sampler2D uToonTexture;
        uniform int uHasTexture;
        uniform int uHasSphereTexture;
        uniform int uHasToonTexture;
        uniform int uSphereMode;
        uniform vec3 uLightDirection;
        uniform vec3 uLightColor;
        uniform vec3 uAmbientColor;
        uniform float uAmbientStrength;
        out vec4 outColor;

        void main()
        {
            float diffuseLight = max(dot(normalize(vNormal), normalize(-uLightDirection)), 0.0);
            vec3 lighting = uAmbientColor * uAmbientStrength + uLightColor * diffuseLight;
            vec4 textureColor = uHasTexture != 0 ? texture(uTexture, vTexCoord) : vec4(1.0);
            vec3 base = uDiffuse.rgb * textureColor.rgb;
            if (uHasSphereTexture != 0 && uSphereMode != 0)
            {
                vec2 sphereUv = normalize(vViewNormal).xy * 0.5 + 0.5;
                vec3 sphere = texture(uSphereTexture, sphereUv).rgb;
                base = uSphereMode == 1 ? base * sphere : base + sphere;
            }
            if (uHasToonTexture != 0)
            {
                float level = clamp(diffuseLight, 0.0, 1.0);
                vec3 toon = texture(uToonTexture, vec2(0.5, level)).rgb;
                lighting = mix(uAmbientColor * uAmbientStrength, toon * uLightColor, step(0.001, diffuseLight));
            }
            outColor = vec4(base * lighting, uDiffuse.a * textureColor.a);
        }
        """;
}

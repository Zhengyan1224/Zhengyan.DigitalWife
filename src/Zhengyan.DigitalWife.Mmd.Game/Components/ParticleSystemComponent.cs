using System.Numerics;
using System.Runtime.InteropServices;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Components;

public enum ParticleBlendMode
{
    Alpha = 0,
    Additive = 1
}

public enum ParticleOrientationMode
{
    Billboard = 0,
    VelocityAligned = 1
}

public enum ParticleTexturePreset
{
    SoftCircle = 0,
    Streak = 1,
    Flame = 2
}

public sealed class ParticleSystemSettings
{
    public string Name { get; set; } = "Particles";

    public int ParticleCount { get; set; } = 512;

    public Vector3 SpawnBoxHalfExtents { get; set; } = new(8.0f, 4.0f, 8.0f);

    public Vector3 BaseVelocity { get; set; } = new(0.0f, -2.0f, 0.0f);

    public Vector3 VelocityJitter { get; set; } = new(1.0f, 0.5f, 1.0f);

    public Vector3 Acceleration { get; set; } = Vector3.Zero;

    public float MinLifetime { get; set; } = 1.0f;

    public float MaxLifetime { get; set; } = 3.0f;

    public float MinSize { get; set; } = 0.1f;

    public float MaxSize { get; set; } = 0.5f;

    public float StartSizeScale { get; set; } = 1.0f;

    public float EndSizeScale { get; set; } = 1.0f;

    public float WidthScale { get; set; } = 1.0f;

    public float HeightScale { get; set; } = 1.0f;

    public float MinRotationSpeedRadians { get; set; } = -1.2f;

    public float MaxRotationSpeedRadians { get; set; } = 1.2f;

    public Vector4 StartColor { get; set; } = Vector4.One;

    public Vector4 EndColor { get; set; } = Vector4.One;

    public bool RandomizeInitialAge { get; set; } = true;

    public ParticleBlendMode BlendMode { get; set; } = ParticleBlendMode.Alpha;

    public ParticleOrientationMode OrientationMode { get; set; } = ParticleOrientationMode.Billboard;

    public ParticleTexturePreset TexturePreset { get; set; } = ParticleTexturePreset.SoftCircle;

    public string? TexturePath { get; set; }

    public bool UseTextureColor { get; set; } = true;

    public bool PreventDarkening { get; set; }

    public void Validate()
    {
        if (ParticleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ParticleCount), "ParticleCount must be greater than zero.");
        }

        if (MinLifetime <= 0.0f || MaxLifetime <= 0.0f || MaxLifetime < MinLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxLifetime), "Lifetime range is invalid.");
        }

        if (MinSize <= 0.0f || MaxSize <= 0.0f || MaxSize < MinSize)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSize), "Size range is invalid.");
        }
    }

    public ParticleSystemSettings Clone()
    {
        return new ParticleSystemSettings
        {
            Name = Name,
            ParticleCount = ParticleCount,
            SpawnBoxHalfExtents = SpawnBoxHalfExtents,
            BaseVelocity = BaseVelocity,
            VelocityJitter = VelocityJitter,
            Acceleration = Acceleration,
            MinLifetime = MinLifetime,
            MaxLifetime = MaxLifetime,
            MinSize = MinSize,
            MaxSize = MaxSize,
            StartSizeScale = StartSizeScale,
            EndSizeScale = EndSizeScale,
            WidthScale = WidthScale,
            HeightScale = HeightScale,
            MinRotationSpeedRadians = MinRotationSpeedRadians,
            MaxRotationSpeedRadians = MaxRotationSpeedRadians,
            StartColor = StartColor,
            EndColor = EndColor,
            RandomizeInitialAge = RandomizeInitialAge,
            BlendMode = BlendMode,
            OrientationMode = OrientationMode,
            TexturePreset = TexturePreset,
            TexturePath = TexturePath,
            UseTextureColor = UseTextureColor,
            PreventDarkening = PreventDarkening
        };
    }
}

public static class ParticleSystemPresets
{
    public static ParticleSystemSettings Rain(string? texturePath = null)
    {
        return new ParticleSystemSettings
        {
            Name = "Rain",
            ParticleCount = 900,
            SpawnBoxHalfExtents = new Vector3(24.0f, 2.0f, 24.0f),
            BaseVelocity = new Vector3(-0.8f, -16.0f, -0.3f),
            VelocityJitter = new Vector3(0.5f, 2.0f, 0.5f),
            Acceleration = new Vector3(0.0f, -1.0f, 0.0f),
            MinLifetime = 0.8f,
            MaxLifetime = 1.6f,
            MinSize = 0.55f,
            MaxSize = 0.95f,
            WidthScale = 0.06f,
            HeightScale = 1.85f,
            StartColor = new Vector4(0.78f, 0.88f, 1.0f, 0.62f),
            EndColor = new Vector4(0.58f, 0.73f, 0.94f, 0.0f),
            OrientationMode = ParticleOrientationMode.VelocityAligned,
            BlendMode = ParticleBlendMode.Alpha,
            TexturePreset = ParticleTexturePreset.Streak,
            TexturePath = texturePath
        };
    }

    public static ParticleSystemSettings Snow(string? texturePath = null)
    {
        return new ParticleSystemSettings
        {
            Name = "Snow",
            ParticleCount = 520,
            SpawnBoxHalfExtents = new Vector3(20.0f, 4.0f, 20.0f),
            BaseVelocity = new Vector3(-0.2f, -1.6f, -0.1f),
            VelocityJitter = new Vector3(0.8f, 0.5f, 0.8f),
            Acceleration = new Vector3(0.0f, -0.08f, 0.0f),
            MinLifetime = 5.0f,
            MaxLifetime = 9.0f,
            MinSize = 0.18f,
            MaxSize = 0.38f,
            MinRotationSpeedRadians = -0.6f,
            MaxRotationSpeedRadians = 0.6f,
            StartColor = new Vector4(0.95f, 0.97f, 1.0f, 0.85f),
            EndColor = new Vector4(0.95f, 0.97f, 1.0f, 0.3f),
            OrientationMode = ParticleOrientationMode.Billboard,
            BlendMode = ParticleBlendMode.Alpha,
            TexturePreset = ParticleTexturePreset.SoftCircle,
            TexturePath = texturePath
        };
    }

    public static ParticleSystemSettings Sakura(string? texturePath = "Sakura.dds")
    {
        return new ParticleSystemSettings
        {
            Name = "Sakura",
            ParticleCount = 420,
            SpawnBoxHalfExtents = new Vector3(20.0f, 4.0f, 20.0f),
            BaseVelocity = new Vector3(-0.25f, -1.15f, 0.1f),
            VelocityJitter = new Vector3(0.75f, 0.4f, 0.75f),
            Acceleration = new Vector3(0.0f, -0.1f, 0.0f),
            MinLifetime = 6.0f,
            MaxLifetime = 10.0f,
            MinSize = 0.16f,
            MaxSize = 0.34f,
            MinRotationSpeedRadians = -2.5f,
            MaxRotationSpeedRadians = 2.5f,
            StartColor = new Vector4(1.0f, 0.88f, 0.94f, 0.88f),
            EndColor = new Vector4(1.0f, 0.72f, 0.84f, 0.22f),
            OrientationMode = ParticleOrientationMode.Billboard,
            BlendMode = ParticleBlendMode.Alpha,
            TexturePreset = ParticleTexturePreset.SoftCircle,
            TexturePath = texturePath
        };
    }

    public static ParticleSystemSettings Cloud(string? texturePath = null)
    {
        return new ParticleSystemSettings
        {
            Name = "Cloud",
            ParticleCount = 260,
            SpawnBoxHalfExtents = new Vector3(26.0f, 1.8f, 16.0f),
            BaseVelocity = new Vector3(0.08f, 0.0f, 0.03f),
            VelocityJitter = new Vector3(0.05f, 0.015f, 0.05f),
            Acceleration = Vector3.Zero,
            MinLifetime = 24.0f,
            MaxLifetime = 52.0f,
            MinSize = 3.6f,
            MaxSize = 7.8f,
            StartSizeScale = 0.9f,
            EndSizeScale = 1.3f,
            WidthScale = 1.6f,
            HeightScale = 0.85f,
            MinRotationSpeedRadians = -0.08f,
            MaxRotationSpeedRadians = 0.08f,
            StartColor = new Vector4(0.95f, 0.97f, 1.0f, 0.2f),
            EndColor = new Vector4(0.95f, 0.97f, 1.0f, 0.0f),
            RandomizeInitialAge = true,
            OrientationMode = ParticleOrientationMode.Billboard,
            BlendMode = ParticleBlendMode.Alpha,
            TexturePreset = ParticleTexturePreset.SoftCircle,
            TexturePath = texturePath,
            UseTextureColor = false,
            PreventDarkening = false
        };
    }

    public static ParticleSystemSettings Waterfall(string? texturePath = null)
    {
        return new ParticleSystemSettings
        {
            Name = "Waterfall",
            ParticleCount = 760,
            SpawnBoxHalfExtents = new Vector3(0.95f, 0.15f, 0.3f),
            BaseVelocity = new Vector3(0.0f, -9.0f, -0.6f),
            VelocityJitter = new Vector3(0.55f, 2.1f, 0.45f),
            Acceleration = new Vector3(0.0f, -8.5f, 0.0f),
            MinLifetime = 0.9f,
            MaxLifetime = 1.55f,
            MinSize = 0.22f,
            MaxSize = 0.35f,
            WidthScale = 0.28f,
            HeightScale = 1.85f,
            StartColor = new Vector4(0.62f, 0.85f, 1.0f, 0.72f),
            EndColor = new Vector4(0.58f, 0.74f, 0.95f, 0.0f),
            OrientationMode = ParticleOrientationMode.VelocityAligned,
            BlendMode = ParticleBlendMode.Alpha,
            TexturePreset = ParticleTexturePreset.Streak,
            TexturePath = texturePath
        };
    }

    public static ParticleSystemSettings Stream(string? texturePath = null)
    {
        return new ParticleSystemSettings
        {
            Name = "Stream",
            ParticleCount = 640,
            SpawnBoxHalfExtents = new Vector3(0.6f, 0.15f, 0.6f),
            BaseVelocity = new Vector3(2.2f, 0.05f, 0.0f),
            VelocityJitter = new Vector3(1.0f, 0.2f, 0.65f),
            Acceleration = new Vector3(0.0f, -0.18f, 0.0f),
            MinLifetime = 1.0f,
            MaxLifetime = 2.1f,
            MinSize = 0.1f,
            MaxSize = 0.24f,
            WidthScale = 0.32f,
            HeightScale = 1.45f,
            StartColor = new Vector4(0.48f, 0.76f, 1.0f, 0.52f),
            EndColor = new Vector4(0.44f, 0.68f, 0.95f, 0.0f),
            OrientationMode = ParticleOrientationMode.VelocityAligned,
            BlendMode = ParticleBlendMode.Alpha,
            TexturePreset = ParticleTexturePreset.Streak,
            TexturePath = texturePath
        };
    }

    public static ParticleSystemSettings Fire(string? texturePath = null)
    {
        return new ParticleSystemSettings
        {
            Name = "Fire",
            ParticleCount = 380,
            SpawnBoxHalfExtents = new Vector3(0.45f, 0.15f, 0.45f),
            BaseVelocity = new Vector3(0.0f, 2.2f, 0.0f),
            VelocityJitter = new Vector3(0.85f, 1.2f, 0.85f),
            Acceleration = new Vector3(0.0f, 0.95f, 0.0f),
            MinLifetime = 0.6f,
            MaxLifetime = 1.35f,
            MinSize = 0.22f,
            MaxSize = 0.46f,
            StartSizeScale = 0.6f,
            EndSizeScale = 1.45f,
            StartColor = new Vector4(1.0f, 0.72f, 0.24f, 0.95f),
            EndColor = new Vector4(0.9f, 0.2f, 0.05f, 0.0f),
            OrientationMode = ParticleOrientationMode.Billboard,
            BlendMode = ParticleBlendMode.Additive,
            TexturePreset = ParticleTexturePreset.Flame,
            TexturePath = texturePath
        };
    }
}

public sealed unsafe class ParticleSystemComponent : DrawableGameComponent
{
    private readonly OrbitCamera _camera;
    private readonly Random _random = new();
    private ParticleSystemSettings _settings;
    private ParticleState[] _particles;
    private ParticleVertex[] _vertices;

    private Texture2D? _texture;
    private uint _program;
    private uint _vao;
    private uint _vertexBuffer;
    private int _uniformViewProjection = -1;
    private int _uniformTexture = -1;
    private int _uniformOpacity = -1;
    private int _uniformStartColor = -1;
    private int _uniformEndColor = -1;
    private int _uniformUseTextureColor = -1;
    private Vector3 _position = Vector3.Zero;

    public ParticleSystemComponent(OrbitCamera camera, ParticleSystemSettings settings)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _settings = settings?.Clone() ?? throw new ArgumentNullException(nameof(settings));
        _settings.Validate();

        _particles = new ParticleState[_settings.ParticleCount];
        _vertices = new ParticleVertex[_settings.ParticleCount * 6];
        DrawOrder = 130;
    }

    public string Name => _settings.Name;

    public Vector3 Position
    {
        get => _position;
        set
        {
            if (_position == value)
            {
                return;
            }

            Vector3 delta = value - _position;
            _position = value;

            // Particles are stored in world space, so keep live particles attached
            // to the emitter when the emitter transform changes.
            for (int i = 0; i < _particles.Length; i++)
            {
                _particles[i].Position += delta;
            }
        }
    }

    public float SimulationSpeed { get; set; } = 1.0f;

    public float Opacity { get; set; } = 1.0f;

    public int ParticleCount => _particles.Length;

    public ParticleSystemSettings GetSettingsSnapshot()
    {
        return _settings.Clone();
    }

    public void ApplySettings(ParticleSystemSettings settings, bool resetParticles = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        ParticleSystemSettings next = settings.Clone();
        bool particleCountChanged = next.ParticleCount != _particles.Length;
        bool textureChanged = next.TexturePreset != _settings.TexturePreset
            || !TexturePathEquals(next.TexturePath, _settings.TexturePath);

        _settings = next;

        if (particleCountChanged)
        {
            _particles = new ParticleState[_settings.ParticleCount];
            _vertices = new ParticleVertex[_settings.ParticleCount * 6];

            if (Game is not null && _vertexBuffer != 0)
            {
                GL gl = Game.GraphicsDevice.Gl;
                gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
                gl.BufferData(GLEnum.ArrayBuffer, (uint)(_vertices.Length * sizeof(ParticleVertex)), null, GLEnum.DynamicDraw);
                gl.BindBuffer(GLEnum.ArrayBuffer, 0);
            }
        }

        if (textureChanged && Game is not null)
        {
            GL gl = Game.GraphicsDevice.Gl;
            Texture2D? previous = _texture;
            _texture = CreateTexture(gl, _settings);
            previous?.Dispose();
        }

        if (resetParticles || particleCountChanged)
        {
            ResetParticles(_settings.RandomizeInitialAge);
        }
    }

    public void ResetParticles(bool randomizeInitialAge = true)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            RespawnParticle(i, randomizeInitialAge);
        }
    }

    protected override void Initialize()
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        if (sizeof(ParticleVertex) != ParticleVertex.StrideInBytes)
        {
            throw new InvalidOperationException($"ParticleVertex size mismatch: sizeof={sizeof(ParticleVertex)}, stride={ParticleVertex.StrideInBytes}");
        }

        GL gl = Game.GraphicsDevice.Gl;
        _program = gl.CreateShaderProgramFromSource(VertexShaderSource, FragmentShaderSource);
        _vao = gl.GenVertexArray();
        _vertexBuffer = gl.GenBuffer();
        _texture = CreateTexture(gl, _settings);

        gl.BindVertexArray(_vao);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        gl.BufferData(GLEnum.ArrayBuffer, (uint)(_vertices.Length * sizeof(ParticleVertex)), null, GLEnum.DynamicDraw);

        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, ParticleVertex.StrideInBytes, (void*)ParticleVertex.PositionOffset);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, ParticleVertex.StrideInBytes, (void*)ParticleVertex.UvOffset);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 1, GLEnum.Float, false, ParticleVertex.StrideInBytes, (void*)ParticleVertex.LifeTOffset);
        gl.EnableVertexAttribArray(2);

        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        gl.BindVertexArray(0);

        _uniformViewProjection = gl.GetUniformLocation(_program, "u_ViewProjection");
        _uniformTexture = gl.GetUniformLocation(_program, "u_Texture");
        _uniformOpacity = gl.GetUniformLocation(_program, "u_Opacity");
        _uniformStartColor = gl.GetUniformLocation(_program, "u_StartColor");
        _uniformEndColor = gl.GetUniformLocation(_program, "u_EndColor");
        _uniformUseTextureColor = gl.GetUniformLocation(_program, "u_UseTextureColor");

        ResetParticles(_settings.RandomizeInitialAge);
    }

    public override void Update(GameTime gameTime)
    {
        float deltaSeconds = Math.Max(0.0f, (float)gameTime.ElapsedSeconds) * MathF.Max(0.0f, SimulationSpeed);
        if (deltaSeconds <= 0.0f)
        {
            return;
        }

        for (int i = 0; i < _particles.Length; i++)
        {
            ref ParticleState particle = ref _particles[i];
            particle.Age += deltaSeconds;
            if (particle.Age >= particle.Lifetime)
            {
                RespawnParticle(i, false);
                continue;
            }

            particle.Velocity += _settings.Acceleration * deltaSeconds;
            particle.Position += particle.Velocity * deltaSeconds;
            particle.Rotation += particle.RotationSpeed * deltaSeconds;
        }
    }

    public override void Draw(GameTime gameTime)
    {
        _ = gameTime;

        if (Game is null || _texture is null || _program == 0 || _vao == 0)
        {
            return;
        }

        int vertexCount = BuildVertices();
        if (vertexCount <= 0)
        {
            return;
        }

        GL gl = Game.GraphicsDevice.Gl;
        // Reset fixed-function state explicitly to avoid inheriting unexpected blend/stencil modes
        // from other render passes (model shadow, UI overlay, etc.).
        gl.Disable(GLEnum.StencilTest);
        gl.Disable(GLEnum.PolygonOffsetFill);
        gl.Disable(GLEnum.CullFace);
        gl.Disable(GLEnum.SampleAlphaToCoverage);
        gl.ColorMask(true, true, true, true);
        gl.Enable(GLEnum.DepthTest);
        gl.DepthMask(false);
        gl.Enable(GLEnum.Blend);
        gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);
        if (_settings.PreventDarkening)
        {
            gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.One, GLEnum.One, GLEnum.OneMinusSrcAlpha);
        }
        else if (_settings.BlendMode == ParticleBlendMode.Additive)
        {
            gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.One, GLEnum.One, GLEnum.OneMinusSrcAlpha);
        }
        else
        {
            gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);
        }

        gl.UseProgram(_program);
        gl.BindVertexArray(_vao);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(GLEnum.Texture2D, _texture.Id);

        Matrix4x4 viewProjection = _camera.View * _camera.Projection;
        gl.SetUniform(_uniformViewProjection, viewProjection);
        gl.Uniform1(_uniformTexture, 0);
        gl.Uniform1(_uniformOpacity, Math.Clamp(Opacity, 0.0f, 1.0f));
        gl.Uniform4(_uniformStartColor, _settings.StartColor.X, _settings.StartColor.Y, _settings.StartColor.Z, _settings.StartColor.W);
        gl.Uniform4(_uniformEndColor, _settings.EndColor.X, _settings.EndColor.Y, _settings.EndColor.Z, _settings.EndColor.W);
        gl.Uniform1(_uniformUseTextureColor, _settings.UseTextureColor ? 1.0f : 0.0f);

        fixed (ParticleVertex* vertexPtr = _vertices)
        {
            gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
            gl.BufferSubData(GLEnum.ArrayBuffer, 0, (uint)(vertexCount * sizeof(ParticleVertex)), vertexPtr);
            gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        }

        gl.DrawArrays(GLEnum.Triangles, 0, (uint)vertexCount);

        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.BindVertexArray(0);
        gl.UseProgram(0);
        gl.DepthMask(true);
        gl.Disable(GLEnum.Blend);
    }

    public override void Dispose()
    {
        _texture?.Dispose();
        _texture = null;

        if (Game is not null)
        {
            GL gl = Game.GraphicsDevice.Gl;
            gl.DeleteBuffer(_vertexBuffer);
            gl.DeleteVertexArray(_vao);
            gl.DeleteProgram(_program);
        }

        _vertexBuffer = 0;
        _vao = 0;
        _program = 0;

        base.Dispose();
    }

    private int BuildVertices()
    {
        int cursor = 0;

        for (int i = 0; i < _particles.Length; i++)
        {
            ref ParticleState particle = ref _particles[i];
            float lifeT = particle.Lifetime <= 0.0f ? 1.0f : Math.Clamp(particle.Age / particle.Lifetime, 0.0f, 1.0f);
            float alpha = Lerp(_settings.StartColor.W, _settings.EndColor.W, lifeT);
            if (alpha <= 0.001f)
            {
                continue;
            }

            float sizeScale = Lerp(_settings.StartSizeScale, _settings.EndSizeScale, lifeT);
            float size = particle.Size * sizeScale;
            if (size <= 0.0001f)
            {
                continue;
            }

            Vector3 right;
            Vector3 up;
            if (_settings.OrientationMode == ParticleOrientationMode.VelocityAligned && particle.Velocity.LengthSquared() > 0.00001f)
            {
                up = Vector3.Normalize(particle.Velocity);
                right = Vector3.Cross(_camera.Front, up);
                if (right.LengthSquared() <= 0.00001f)
                {
                    right = _camera.Right;
                }
                else
                {
                    right = Vector3.Normalize(right);
                }
            }
            else
            {
                right = _camera.Right;
                up = _camera.Up;

                if (MathF.Abs(particle.Rotation) > 0.0001f)
                {
                    float cos = MathF.Cos(particle.Rotation);
                    float sin = MathF.Sin(particle.Rotation);
                    Vector3 rotatedRight = (right * cos) + (up * sin);
                    Vector3 rotatedUp = (-right * sin) + (up * cos);
                    right = rotatedRight;
                    up = rotatedUp;
                }
            }

            float halfWidth = size * 0.5f * _settings.WidthScale;
            float halfHeight = size * 0.5f * _settings.HeightScale;
            Vector3 rightOffset = right * halfWidth;
            Vector3 upOffset = up * halfHeight;
            Vector3 center = particle.Position;

            Vector3 bottomLeft = center - rightOffset - upOffset;
            Vector3 bottomRight = center + rightOffset - upOffset;
            Vector3 topLeft = center - rightOffset + upOffset;
            Vector3 topRight = center + rightOffset + upOffset;

            if (cursor + 6 > _vertices.Length)
            {
                break;
            }

            _vertices[cursor++] = new ParticleVertex(bottomLeft, new Vector2(0.0f, 1.0f), lifeT);
            _vertices[cursor++] = new ParticleVertex(bottomRight, new Vector2(1.0f, 1.0f), lifeT);
            _vertices[cursor++] = new ParticleVertex(topLeft, new Vector2(0.0f, 0.0f), lifeT);

            _vertices[cursor++] = new ParticleVertex(topLeft, new Vector2(0.0f, 0.0f), lifeT);
            _vertices[cursor++] = new ParticleVertex(bottomRight, new Vector2(1.0f, 1.0f), lifeT);
            _vertices[cursor++] = new ParticleVertex(topRight, new Vector2(1.0f, 0.0f), lifeT);
        }

        return cursor;
    }

    private void RespawnParticle(int index, bool randomizeAge)
    {
        ref ParticleState particle = ref _particles[index];
        float lifetime = NextRange(_settings.MinLifetime, _settings.MaxLifetime);
        float age = randomizeAge ? NextRange(0.0f, lifetime) : 0.0f;

        particle.Lifetime = lifetime;
        particle.Age = age;
        particle.Size = NextRange(_settings.MinSize, _settings.MaxSize);
        particle.Rotation = NextRange(-MathF.PI, MathF.PI);
        particle.RotationSpeed = NextRange(_settings.MinRotationSpeedRadians, _settings.MaxRotationSpeedRadians);

        Vector3 half = _settings.SpawnBoxHalfExtents;
        Vector3 spawnOffset = new(
            NextRange(-half.X, half.X),
            NextRange(-half.Y, half.Y),
            NextRange(-half.Z, half.Z));
        particle.Position = Position + spawnOffset;

        Vector3 jitter = new(
            NextRange(-_settings.VelocityJitter.X, _settings.VelocityJitter.X),
            NextRange(-_settings.VelocityJitter.Y, _settings.VelocityJitter.Y),
            NextRange(-_settings.VelocityJitter.Z, _settings.VelocityJitter.Z));
        particle.Velocity = _settings.BaseVelocity + jitter;

        if (age > 0.0f)
        {
            particle.Position += (particle.Velocity * age) + (_settings.Acceleration * (0.5f * age * age));
            particle.Velocity += _settings.Acceleration * age;
        }
    }

    private float NextRange(float min, float max)
    {
        return min + ((max - min) * (float)_random.NextDouble());
    }

    private static Texture2D CreateTexture(GL gl, ParticleSystemSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.TexturePath))
        {
            string? resolvedPath = TryResolveTexturePath(settings.TexturePath);
            if (resolvedPath is not null)
            {
                Texture2D fileTexture = new(gl, GLEnum.ClampToEdge);
                fileTexture.LoadFromFile(resolvedPath);
                return fileTexture;
            }
        }

        Texture2D texture = new(gl, GLEnum.ClampToEdge);
        byte[] bytes = settings.TexturePreset switch
        {
            ParticleTexturePreset.Streak => CreateStreakTexture(96, 96),
            ParticleTexturePreset.Flame => CreateFlameTexture(96, 96),
            _ => CreateSoftCircleTexture(96, 96)
        };
        texture.Upload(bytes, 96, 96, TextureAlphaMode.Blend);
        return texture;
    }

    private static string? TryResolveTexturePath(string pathOrFileName)
    {
        if (string.IsNullOrWhiteSpace(pathOrFileName))
        {
            return null;
        }

        if (Path.IsPathRooted(pathOrFileName))
        {
            string absolute = Path.GetFullPath(pathOrFileName);
            return File.Exists(absolute) ? absolute : null;
        }

        string fileName = pathOrFileName;
        return BundledAssetPathResolver.TryResolveFile("Resources", "Particles", fileName)
            ?? BundledAssetPathResolver.TryResolveFile(fileName);
    }

    private static byte[] CreateSoftCircleTexture(int width, int height)
    {
        byte[] bytes = new byte[width * height * 4];
        Vector2 center = new(width * 0.5f, height * 0.5f);
        float radius = MathF.Min(width, height) * 0.48f;
        float invRadius = radius <= 0.0f ? 0.0f : 1.0f / radius;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = (x + 0.5f) - center.X;
                float dy = (y + 0.5f) - center.Y;
                float distance = MathF.Sqrt((dx * dx) + (dy * dy)) * invRadius;
                float alpha = Math.Clamp(1.0f - distance, 0.0f, 1.0f);
                alpha = alpha * alpha;

                int index = ((y * width) + x) * 4;
                bytes[index + 0] = 255;
                bytes[index + 1] = 255;
                bytes[index + 2] = 255;
                bytes[index + 3] = (byte)(alpha * 255.0f);
            }
        }

        return bytes;
    }

    private static byte[] CreateStreakTexture(int width, int height)
    {
        byte[] bytes = new byte[width * height * 4];
        float halfWidth = width * 0.5f;
        float sigma = MathF.Max(halfWidth * 0.22f, 1.0f);
        float twoSigmaSq = 2.0f * sigma * sigma;

        for (int y = 0; y < height; y++)
        {
            float yT = y / (float)(height - 1);
            float verticalFade = MathF.Pow(1.0f - MathF.Abs((yT * 2.0f) - 1.0f), 0.35f);

            for (int x = 0; x < width; x++)
            {
                float dx = x - halfWidth;
                float gaussian = MathF.Exp(-(dx * dx) / twoSigmaSq);
                float alpha = Math.Clamp(gaussian * verticalFade, 0.0f, 1.0f);

                int index = ((y * width) + x) * 4;
                bytes[index + 0] = 255;
                bytes[index + 1] = 255;
                bytes[index + 2] = 255;
                bytes[index + 3] = (byte)(alpha * 255.0f);
            }
        }

        return bytes;
    }

    private static byte[] CreateFlameTexture(int width, int height)
    {
        byte[] bytes = new byte[width * height * 4];
        float halfWidth = width * 0.5f;

        for (int y = 0; y < height; y++)
        {
            float yT = y / (float)(height - 1);
            float coneWidth = Lerp(0.08f, 0.48f, 1.0f - yT);
            float feather = Lerp(0.05f, 0.24f, 1.0f - yT);
            float centerGlow = MathF.Pow(1.0f - yT, 1.25f);

            for (int x = 0; x < width; x++)
            {
                float xNorm = (x - halfWidth) / halfWidth;
                float distance = MathF.Abs(xNorm) - coneWidth;
                float edge = 1.0f - Math.Clamp(distance / MathF.Max(feather, 0.0001f), 0.0f, 1.0f);
                float alpha = edge * centerGlow;
                alpha = MathF.Pow(Math.Clamp(alpha, 0.0f, 1.0f), 1.15f);

                int index = ((y * width) + x) * 4;
                bytes[index + 0] = (byte)Math.Clamp(255.0f, 0.0f, 255.0f);
                bytes[index + 1] = (byte)Math.Clamp(200.0f + (55.0f * (1.0f - yT)), 0.0f, 255.0f);
                bytes[index + 2] = (byte)Math.Clamp(110.0f + (60.0f * (1.0f - yT)), 0.0f, 255.0f);
                bytes[index + 3] = (byte)(alpha * 255.0f);
            }
        }

        return bytes;
    }

    private static float Lerp(float from, float to, float t)
    {
        return from + ((to - from) * t);
    }

    private static bool TexturePathEquals(string? left, string? right)
    {
        string normalizedLeft = string.IsNullOrWhiteSpace(left) ? string.Empty : left.Trim();
        string normalizedRight = string.IsNullOrWhiteSpace(right) ? string.Empty : right.Trim();
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private struct ParticleState
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Age;
        public float Lifetime;
        public float Size;
        public float Rotation;
        public float RotationSpeed;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct ParticleVertex
    {
        public const uint StrideInBytes = 6u * sizeof(float);
        public const int PositionOffset = 0;
        public const int UvOffset = 12;
        public const int LifeTOffset = 20;

        [FieldOffset(0)]
        public float PosX;
        [FieldOffset(4)]
        public float PosY;
        [FieldOffset(8)]
        public float PosZ;
        [FieldOffset(12)]
        public float UvX;
        [FieldOffset(16)]
        public float UvY;
        [FieldOffset(20)]
        public float LifeT;

        public ParticleVertex(Vector3 position, Vector2 uv, float lifeT)
        {
            PosX = position.X;
            PosY = position.Y;
            PosZ = position.Z;
            UvX = uv.X;
            UvY = uv.Y;
            LifeT = lifeT;
        }
    }

    private const string VertexShaderSource = """
#version 300 es

layout (location = 0) in vec3 in_Pos;
layout (location = 1) in vec2 in_Uv;
layout (location = 2) in float in_LifeT;

uniform mat4 u_ViewProjection;

out vec2 vs_Uv;
out float vs_LifeT;

void main()
{
    vs_Uv = in_Uv;
    vs_LifeT = in_LifeT;
    gl_Position = u_ViewProjection * vec4(in_Pos, 1.0);
}
""";

    private const string FragmentShaderSource = """
#version 300 es

precision highp float;

in vec2 vs_Uv;
in float vs_LifeT;

uniform sampler2D u_Texture;
uniform float u_Opacity;
uniform vec4 u_StartColor;
uniform vec4 u_EndColor;
uniform float u_UseTextureColor;

out vec4 out_Color;

void main()
{
    vec4 texColor = texture(u_Texture, vs_Uv);
    vec4 particleColor = mix(u_StartColor, u_EndColor, clamp(vs_LifeT, 0.0, 1.0));
    vec3 textureRgb = mix(vec3(1.0), texColor.rgb, clamp(u_UseTextureColor, 0.0, 1.0));
    vec4 color = vec4(particleColor.rgb * textureRgb, particleColor.a * texColor.a * u_Opacity);
    if (color.a <= 0.001) {
        discard;
    }
    out_Color = color;
}
""";
}


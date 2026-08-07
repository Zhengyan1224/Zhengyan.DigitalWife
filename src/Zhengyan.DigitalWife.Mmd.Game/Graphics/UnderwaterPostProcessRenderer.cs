using System.Numerics;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public readonly record struct UnderwaterPostProcessSettings(
    Vector3 Tint,
    Vector3 FogColor,
    float FogDensity,
    float VisibilityDistance,
    float DistortionStrength,
    float CausticsStrength,
    float BubbleStrength,
    float SurfaceDepth);

public interface IUnderwaterPostProcessRenderer : IDisposable
{
    void BeginCapture(int width, int height, Vector4 clearColor);
    void ResumeCapture();
    void Draw(OrbitCamera camera, UnderwaterPostProcessSettings settings, double timeSeconds, int viewportWidth, int viewportHeight);
}

public sealed unsafe class UnderwaterPostProcessRenderer : IUnderwaterPostProcessRenderer
{
    private readonly GL _gl;
    private readonly SceneColorDepthRenderTarget _captureTarget;
    private readonly uint _program;
    private readonly uint _vao;
    private readonly uint _vertexBuffer;
    private bool _disposed;

    private readonly int _uniformColorTex;
    private readonly int _uniformDepthTex;
    private readonly int _uniformTime;
    private readonly int _uniformNear;
    private readonly int _uniformFar;
    private readonly int _uniformIsOrthographic;
    private readonly int _uniformViewportSize;
    private readonly int _uniformTint;
    private readonly int _uniformFogColor;
    private readonly int _uniformFogDensity;
    private readonly int _uniformVisibilityDistance;
    private readonly int _uniformDistortionStrength;
    private readonly int _uniformCausticsStrength;
    private readonly int _uniformBubbleStrength;
    private readonly int _uniformSurfaceDepth;

    public UnderwaterPostProcessRenderer(GL gl, string name)
    {
        _gl = gl;
        _captureTarget = new SceneColorDepthRenderTarget(gl, $"{name}-Capture");
        _program = _gl.CreateShaderProgramFromSource(VertexShaderSource, FragmentShaderSource);
        _vao = _gl.GenVertexArray();
        _vertexBuffer = _gl.GenBuffer();

        float[] vertices =
        [
            -1.0f, -1.0f, 0.0f, 0.0f,
             1.0f, -1.0f, 1.0f, 0.0f,
            -1.0f,  1.0f, 0.0f, 1.0f,
            -1.0f,  1.0f, 0.0f, 1.0f,
             1.0f, -1.0f, 1.0f, 0.0f,
             1.0f,  1.0f, 1.0f, 1.0f
        ];

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        fixed (float* vertexPtr = vertices)
        {
            _gl.BufferData(GLEnum.ArrayBuffer, (uint)(vertices.Length * sizeof(float)), vertexPtr, GLEnum.StaticDraw);
        }

        _gl.VertexAttribPointer(0, 2, GLEnum.Float, false, (uint)(4 * sizeof(float)), (void*)0);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(1, 2, GLEnum.Float, false, (uint)(4 * sizeof(float)), (void*)(2 * sizeof(float)));
        _gl.EnableVertexAttribArray(1);
        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        _gl.BindVertexArray(0);

        _uniformColorTex = _gl.GetUniformLocation(_program, "u_ColorTex");
        _uniformDepthTex = _gl.GetUniformLocation(_program, "u_DepthTex");
        _uniformTime = _gl.GetUniformLocation(_program, "u_Time");
        _uniformNear = _gl.GetUniformLocation(_program, "u_Near");
        _uniformFar = _gl.GetUniformLocation(_program, "u_Far");
        _uniformIsOrthographic = _gl.GetUniformLocation(_program, "u_IsOrthographic");
        _uniformViewportSize = _gl.GetUniformLocation(_program, "u_ViewportSize");
        _uniformTint = _gl.GetUniformLocation(_program, "u_Tint");
        _uniformFogColor = _gl.GetUniformLocation(_program, "u_FogColor");
        _uniformFogDensity = _gl.GetUniformLocation(_program, "u_FogDensity");
        _uniformVisibilityDistance = _gl.GetUniformLocation(_program, "u_VisibilityDistance");
        _uniformDistortionStrength = _gl.GetUniformLocation(_program, "u_DistortionStrength");
        _uniformCausticsStrength = _gl.GetUniformLocation(_program, "u_CausticsStrength");
        _uniformBubbleStrength = _gl.GetUniformLocation(_program, "u_BubbleStrength");
        _uniformSurfaceDepth = _gl.GetUniformLocation(_program, "u_SurfaceDepth");
    }

    public SceneColorDepthRenderTarget CaptureTarget => _captureTarget;

    public void BeginCapture(int width, int height)
    {
        _captureTarget.EnsureSize(width, height);
        _captureTarget.Bind();
    }

    public void BeginCapture(int width, int height, Vector4 clearColor)
    {
        BeginCapture(width, height);
        _gl.Disable(GLEnum.ScissorTest);
        _gl.Disable(GLEnum.StencilTest);
        _gl.ColorMask(true, true, true, true);
        _gl.DepthMask(true);
        _gl.StencilMask(0xFF);
        _gl.ClearColor(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
    }

    public void ResumeCapture() => _captureTarget.Bind();

    public void Draw(OrbitCamera camera, UnderwaterPostProcessSettings settings, double timeSeconds, int viewportWidth, int viewportHeight)
    {
        _gl.Disable(GLEnum.DepthTest);
        _gl.Disable(GLEnum.StencilTest);
        _gl.Disable(GLEnum.Blend);
        _gl.DepthMask(false);

        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vao);

        _gl.SetUniform(_uniformColorTex, 0);
        _gl.SetUniform(_uniformDepthTex, 1);
        _gl.SetUniform(_uniformTime, (float)timeSeconds);
        _gl.SetUniform(_uniformNear, camera.NearClipPlane);
        _gl.SetUniform(_uniformFar, camera.FarClipPlane);
        _gl.SetUniform(_uniformIsOrthographic, camera.ProjectionMode == CameraProjectionMode.Orthographic ? 1.0f : 0.0f);
        _gl.SetUniform(_uniformViewportSize, new Vector2(Math.Max(viewportWidth, 1), Math.Max(viewportHeight, 1)));
        _gl.SetUniform(_uniformTint, Clamp(settings.Tint, 0.0f, 2.0f));
        _gl.SetUniform(_uniformFogColor, Clamp(settings.FogColor, 0.0f, 2.0f));
        _gl.SetUniform(_uniformFogDensity, Math.Clamp(settings.FogDensity, 0.0f, 8.0f));
        _gl.SetUniform(_uniformVisibilityDistance, Math.Max(settings.VisibilityDistance, 0.001f));
        _gl.SetUniform(_uniformDistortionStrength, Math.Clamp(settings.DistortionStrength, 0.0f, 0.12f));
        _gl.SetUniform(_uniformCausticsStrength, Math.Clamp(settings.CausticsStrength, 0.0f, 2.0f));
        _gl.SetUniform(_uniformBubbleStrength, Math.Clamp(settings.BubbleStrength, 0.0f, 2.0f));
        _gl.SetUniform(_uniformSurfaceDepth, Math.Max(settings.SurfaceDepth, 0.0f));

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(GLEnum.Texture2D, _captureTarget.ColorTextureId);
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(GLEnum.Texture2D, _captureTarget.DepthTextureId);

        _gl.DrawArrays(GLEnum.Triangles, 0, 6);

        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(GLEnum.Texture2D, 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(GLEnum.Texture2D, 0);
        _gl.BindVertexArray(0);
        _gl.UseProgram(0);

        _gl.DepthMask(true);
        _gl.Enable(GLEnum.DepthTest);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _captureTarget.Dispose();
        _gl.DeleteBuffer(_vertexBuffer);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
        GC.SuppressFinalize(this);
    }

    private static Vector3 Clamp(Vector3 value, float min, float max)
    {
        return new Vector3(
            Math.Clamp(value.X, min, max),
            Math.Clamp(value.Y, min, max),
            Math.Clamp(value.Z, min, max));
    }

    private const string VertexShaderSource = """
        #version 300 es
        layout (location = 0) in vec2 a_Position;
        layout (location = 1) in vec2 a_TexCoord;

        out vec2 v_TexCoord;

        void main()
        {
            v_TexCoord = a_TexCoord;
            gl_Position = vec4(a_Position, 0.0, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 300 es
        precision highp float;

        in vec2 v_TexCoord;
        out vec4 out_Color;

        uniform sampler2D u_ColorTex;
        uniform sampler2D u_DepthTex;
        uniform float u_Time;
        uniform float u_Near;
        uniform float u_Far;
        uniform float u_IsOrthographic;
        uniform vec2 u_ViewportSize;
        uniform vec3 u_Tint;
        uniform vec3 u_FogColor;
        uniform float u_FogDensity;
        uniform float u_VisibilityDistance;
        uniform float u_DistortionStrength;
        uniform float u_CausticsStrength;
        uniform float u_BubbleStrength;
        uniform float u_SurfaceDepth;

        float hash(vec2 p)
        {
            vec3 p3 = fract(vec3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return fract((p3.x + p3.y) * p3.z);
        }

        float noise(vec2 p)
        {
            vec2 i = floor(p);
            vec2 f = fract(p);
            vec2 u = f * f * (3.0 - 2.0 * f);
            return mix(
                mix(hash(i + vec2(0.0, 0.0)), hash(i + vec2(1.0, 0.0)), u.x),
                mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x),
                u.y);
        }

        float linearDepth(float depth)
        {
            if (u_IsOrthographic > 0.5)
            {
                return mix(u_Near, u_Far, depth);
            }

            float z = depth * 2.0 - 1.0;
            return (2.0 * u_Near * u_Far) / max(u_Far + u_Near - z * (u_Far - u_Near), 0.0001);
        }

        float caustics(vec2 uv, float time)
        {
            vec2 p = uv * vec2(18.0, 13.0);
            float a = sin(p.x + sin(p.y * 1.7 + time * 0.85) + time * 0.65);
            float b = sin((p.x * 1.35 - p.y * 0.75) + time * 1.15);
            float c = sin(length(p - vec2(9.0, 6.0)) * 1.35 - time * 1.8);
            float bands = (a + b + c) * 0.333 + 0.5;
            return smoothstep(0.73, 1.0, bands);
        }

        float bubbleLayer(vec2 uv, float time, float scale, float speed, float seed)
        {
            vec2 p = uv * scale;
            p.y -= time * speed;
            vec2 cell = floor(p);
            vec2 f = fract(p);
            float rnd = hash(cell + seed);
            vec2 center = vec2(hash(cell + seed + 17.0), hash(cell + seed + 31.0));
            center.y = fract(center.y + time * speed * 0.13);
            float radius = mix(0.035, 0.085, hash(cell + seed + 47.0));
            float d = length((f - center) * vec2(1.0, 1.25));
            float outer = 1.0 - smoothstep(radius * 0.72, radius, d);
            float inner = smoothstep(radius * 0.35, radius * 0.58, d);
            float ring = outer * inner;
            return ring * smoothstep(0.78, 0.98, rnd);
        }

        float bubbles(vec2 uv, float time)
        {
            float b = 0.0;
            b += bubbleLayer(uv + vec2(0.03, 0.01), time, 8.0, 0.10, 3.0);
            b += bubbleLayer(uv + vec2(0.41, 0.22), time, 13.0, 0.16, 19.0);
            b += bubbleLayer(uv + vec2(0.77, 0.37), time, 21.0, 0.22, 41.0);
            return clamp(b, 0.0, 1.0);
        }

        void main()
        {
            vec2 uv = v_TexCoord;
            float rawDepth = texture(u_DepthTex, uv).r;
            float skyMask = smoothstep(0.9985, 1.0, rawDepth);
            float sceneDepth = linearDepth(rawDepth);
            float depthForWater = mix(sceneDepth, u_Far * 0.32, skyMask);
            float entryStrength = smoothstep(0.0, 0.45, u_SurfaceDepth);

            vec2 wave = vec2(
                sin(uv.y * 32.0 + u_Time * 0.9) + sin((uv.x + uv.y) * 22.0 - u_Time * 1.35),
                cos(uv.x * 28.0 - u_Time * 0.75) + sin((uv.x - uv.y) * 18.0 + u_Time * 1.1));
            float shimmer = noise(uv * 14.0 + vec2(u_Time * 0.05, -u_Time * 0.08));
            vec2 distortion = wave * (0.5 + shimmer * 0.5) * u_DistortionStrength * entryStrength;
            distortion *= mix(1.0, 0.35, skyMask);

            vec4 source = texture(u_ColorTex, clamp(uv + distortion, vec2(0.001), vec2(0.999)));
            vec3 color = source.rgb;

            float distanceFog = max(depthForWater - u_Near, 0.0) / max(u_VisibilityDistance, 0.001);
            float density = max(u_FogDensity, 0.0);
            float fogFactor = 1.0 - exp(-distanceFog * density);
            fogFactor = clamp(fogFactor + clamp(u_SurfaceDepth * 0.045, 0.0, 0.38), 0.0, 0.96) * entryStrength;

            vec3 absorption = vec3(
                exp(-depthForWater * 0.018 * density),
                exp(-depthForWater * 0.007 * density),
                exp(-depthForWater * 0.0035 * density));
            color *= mix(vec3(1.0), absorption, 0.55 * entryStrength);
            color *= mix(vec3(1.0), u_Tint, 0.34 * entryStrength);

            vec3 fogged = mix(color, u_FogColor, fogFactor);

            float nearSurface = (1.0 - fogFactor) * (1.0 - skyMask);
            float caustic = caustics(uv + distortion * 4.0, u_Time) * nearSurface * u_CausticsStrength * entryStrength;
            fogged += vec3(0.16, 0.26, 0.23) * caustic;

            float bubble = bubbles(uv, u_Time) * u_BubbleStrength * entryStrength;
            fogged = mix(fogged, vec3(0.72, 0.92, 0.95), bubble * 0.35);

            float vignette = 1.0 - smoothstep(0.18, 0.82, distance(uv, vec2(0.5)));
            fogged *= mix(0.72, 1.0, vignette * entryStrength + (1.0 - entryStrength));

            out_Color = vec4(clamp(fogged, 0.0, 1.0), source.a);
        }
        """;
}

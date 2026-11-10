using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

/// <summary>One cached fullscreen triangle; no per-frame vertex uploads or sprite allocations.</summary>
internal sealed class OpenGlScenePresenter : IDisposable
{
    private readonly GL _gl;
    private readonly uint _program, _vao;
    private readonly int _texture;
    public OpenGlScenePresenter(GL gl)
    {
        _gl=gl;
        _program=gl.CreateShaderProgramFromSource("""
            #version 300 es
            precision highp float;
            out vec2 uv;
            void main(){ vec2 p=gl_VertexID==0?vec2(0,0):gl_VertexID==1?vec2(2,0):vec2(0,2); uv=p; gl_Position=vec4(p*2.0-1.0,0,1); }
            ""","""
            #version 300 es
            precision highp float;
            in vec2 uv;
            uniform sampler2D scene;
            out vec4 color;
            void main(){ color=texture(scene,uv); }
            """);
        _vao=gl.GenVertexArray(); _texture=gl.GetUniformLocation(_program,"scene");
    }
    public void Draw(uint texture,int width,int height)
    {
        _gl.BindFramebuffer(GLEnum.Framebuffer,0); _gl.Viewport(0,0,(uint)width,(uint)height);
        _gl.Disable(GLEnum.DepthTest); _gl.Disable(GLEnum.StencilTest); _gl.Disable(GLEnum.ScissorTest);
        _gl.Disable(GLEnum.CullFace); _gl.Disable(GLEnum.Blend); _gl.ColorMask(true,true,true,true); _gl.DepthMask(false);
        _gl.UseProgram(_program); _gl.BindVertexArray(_vao); _gl.ActiveTexture(GLEnum.Texture0);
        _gl.BindSampler(0,0); _gl.BindTexture(GLEnum.Texture2D,texture); _gl.SetUniform(_texture,0); _gl.DrawArrays(GLEnum.Triangles,0,3);
        _gl.BindTexture(GLEnum.Texture2D,0); _gl.BindVertexArray(0); _gl.UseProgram(0); _gl.DepthMask(true);
    }
    public void Dispose(){ _gl.DeleteVertexArray(_vao); _gl.DeleteProgram(_program); }
}

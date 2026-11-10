using System.Numerics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Silk.NET.Windowing;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using GL = Silk.NET.OpenGLES.GL;
using GLEnum = Silk.NET.OpenGLES.GLEnum;

internal static unsafe class AndroidGlesPresentationRegression
{
    public static void HostOrder()
    {
        string repo=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
        string source=File.ReadAllText(Path.Combine(repo,"src/Zhengyan.DigitalWife.GamePlayer.Android/AndroidEglRenderHost.cs"));
        var render=CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method=>method.Identifier.ValueText=="Render");
        string[] calls=render.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(call=>(call.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText??"").ToArray();
        int draw=Array.IndexOf(calls,"RenderHostedWithoutPresent"),present=Array.IndexOf(calls,"PresentHosted"),swap=Array.IndexOf(calls,"EglSwapBuffers");
        Check(draw>=0 && present>draw && swap>present,"Android GLES swaps the EGL window before resolving/presenting its scene.");
        Check(calls.Count(call=>call=="PresentHosted")==1,"Android GLES must present the scene exactly once per frame.");
    }

    public static void AnimatedFrames()
    {
        WindowOptions options=WindowOptions.Default;
        RendererFactory.ConfigureWindow(ref options,GraphicsBackend.OpenGL);
        options.IsVisible=false;options.WindowBorder=WindowBorder.Hidden;options.Size=new(64,64);options.Samples=8;
        using IWindow window=Window.Create(options);window.Initialize();window.MakeCurrent();
        GL gl=GL.GetApi(window);
        OpenGlRenderer renderer=new();renderer.Initialize(gl,new(64,64),8);
        using AnimatedHostedGame game=new(renderer);
        game.InitializeHosted();
        Vector4[] colors=[new(1,0,0,1),new(0,1,0,1),new(0,0,1,1),new(1,1,0,1),new(1,0,1,1),new(0,1,1,1)];
        int frames=0;
        foreach(int samples in new[]{1,2,4})
        {
            renderer.SetAntiAliasingSamples(samples);
            for(int frame=0;frame<8;frame++)
            {
                Vector4 expected=colors[frames++%colors.Length];
                game.GraphicsDevice.ClearColor=expected;
                game.UpdateHosted(1.0/60);
                game.RenderHostedWithoutPresent(1.0/60);
                // The explicit presentation step matches AndroidEglRenderHost.
                game.PresentHosted();
                gl.BindFramebuffer(GLEnum.Framebuffer,0);
                byte[] pixel=new byte[4];fixed(byte* p=pixel)gl.ReadPixels(32,32,1,1,GLEnum.Rgba,GLEnum.UnsignedByte,p);
                Check(pixel[0]==(byte)(expected.X*255) && pixel[1]==(byte)(expected.Y*255) && pixel[2]==(byte)(expected.Z*255),
                    $"Hosted GLES showed a stale frame after changing to {samples}x: frame={frame}, color={string.Join(',',pixel)}.");
                window.SwapBuffers();
            }
        }
        Check(gl.GetError()==GLEnum.NoError,"Hosted scene presentation raised a GL error.");
        Console.WriteLine($"PASS hosted GLES animated frames: {frames} frames, default 8x, dynamic 1x/2x/4x, no stale window images");
    }

    private sealed class AnimatedHostedGame(OpenGlRenderer renderer)
        : Game(new GameOptions{EnableAudio=false,UseOpenCL=false,GraphicsBackend=GraphicsBackend.OpenGL,Samples=8},renderer,new(64,64)) { }
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}

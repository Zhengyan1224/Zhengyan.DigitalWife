extern alias AndroidRuntime;
extern alias AndroidProjects;

using System.Numerics;
using System.Reflection;
using Silk.NET.Windowing;
using Zhengyan.DigitalWife.Scripting;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using GL = Silk.NET.OpenGLES.GL;
using GLEnum = Silk.NET.OpenGLES.GLEnum;
using CoreProject = AndroidProjects::Zhengyan.DigitalWife.GameProjects.GameProject;
using CoreStore = AndroidProjects::Zhengyan.DigitalWife.GameProjects.GameProjectStore;
using CoreScene = AndroidProjects::Zhengyan.DigitalWife.GameProjects.GameProjectScene;
using CoreSceneManager = AndroidRuntime::Zhengyan.DigitalWife.GamePlayer.Runtime.RuntimeSceneManager;
using CoreRuntimeScene = AndroidRuntime::Zhengyan.DigitalWife.GamePlayer.Runtime.RuntimeScene;
using CoreWebContext = AndroidRuntime::Zhengyan.DigitalWife.GamePlayer.Runtime.WebScriptContext;
using CoreWebScript = AndroidRuntime::Zhengyan.DigitalWife.GamePlayer.Runtime.IWebGameScript;
using Zhengyan.DigitalWife.GameEditor;
using Veldrid;
using Veldrid.SPIRV;
using GraphicsBackend = Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsBackend;

internal static unsafe class RuntimeGraphicsRegression
{
    public static void Requests()
    {
        RuntimeGraphicsQuality quality=new();
        quality.SceneSamples=1; long revision=quality.Capture().Revision;
        for(int i=0;i<1000;i++) quality.SceneSamples=1;
        Check(quality.Capture().Revision==revision,"Repeated sample assignments did not coalesce.");
        quality.Shadows.FilterSamples=25; quality.Shadows.FilterRadius=3;
        var snapshot=quality.Capture();
        Check(snapshot.ShadowsEnabled && snapshot.FilterSamples==25 && snapshot.FilterRadius==3,"Shadow setters did not activate runtime overrides.");
        quality.ReportApplied(snapshot.Revision,1,1024,512);
        Check(!quality.Pending && quality.ActualSceneSamples==1 && quality.Shadows.ActualLocalLightResolution==512,"Actual renderer results were not reported.");
        quality.SceneSamples=8;
        quality.ReportApplied(snapshot.Revision,1,1024,512);
        Check(quality.Pending && quality.SceneSamples==8,"An older render report lost a newer request.");
        var next=quality.Capture(); quality.ReportApplied(next.Revision,4,1024,512); quality.ReportApplied(snapshot.Revision,1,0,0);
        Check(quality.ActualSceneSamples==4,"Out-of-order telemetry overwrote current device results.");
    }
    public static void SceneLifetime()
    {
        string root=Path.Combine(Path.GetTempPath(),"dw-runtime-graphics-"+Guid.NewGuid().ToString("N"));
        try
        {
            CoreProject project=new(){Scenes=["scenes/a.scene.json","scenes/b.scene.json"],DefaultScene="scenes/a.scene.json",EditorScene="scenes/a.scene.json"};
            CoreStore.Save(root,project); CoreStore.SaveScene(root,"scenes/a.scene.json",new CoreScene()); CoreStore.SaveScene(root,"scenes/b.scene.json",new CoreScene());
            string authored=File.ReadAllText(Path.Combine(root,"game.project.json"));
            using CoreSceneManager manager=new(project,root);
            Check(manager.LoadInitial(),"Scene load failed."); var graphics=manager.Current!.Graphics;
            graphics.SceneSamples=2; graphics.Shadows.FilterSamples=49;
            Check(manager.LoadScene("scenes/b.scene.json") && ReferenceEquals(graphics,manager.Current!.Graphics),"Scene switching replaced project runtime graphics state.");
            Check(manager.Current!.Graphics.SceneSamples==2 && manager.Current.Graphics.Shadows.FilterSamples==49,"Scene switching reset script quality.");
            Check(File.ReadAllText(Path.Combine(root,"game.project.json"))==authored,"Runtime quality modified project JSON.");
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    public static void ScriptApis()
    {
        string root=Path.Combine(Path.GetTempPath(),"dw-quality-script-"+Guid.NewGuid().ToString("N"));
        try
        {
            var project=new Zhengyan.DigitalWife.GameProjects.GameProject();
            Zhengyan.DigitalWife.GameProjects.GameProjectStore.Save(root,project);
            string script=Path.Combine(root,"scripts","quality.csx");
            File.WriteAllText(script,"""
                if(IsStart){
                    Graphics.SceneSamples=2;
                    Graphics.Shadows.DirectionalResolution=1024;
                    Graphics.Shadows.LocalLightResolution=512;
                    Graphics.Shadows.FilterSamples=25;
                    Graphics.Shadows.FilterRadius=3;
                    Entity.ReflectionSamples=4;
                }
                """);
            project.Scene.Entities=[new(){Id="mirror",Type="textured_plane",Scripts=[new(){Path="scripts/quality.csx",Language="csharp"}]}];
            Zhengyan.DigitalWife.GameProjects.GameProjectStore.SaveScene(root,project.DefaultScene,project.Scene);
            var desktop=DesktopCSharpScriptPrecompiler.Precompile(root,project);
            var android=AndroidCSharpScriptPrecompiler.Precompile(root,project);
            var web=WebCSharpScriptPrecompiler.Precompile(root,project);
            Check(desktop.Entries.Count==1 && android.Errors.Count==0 && web.Errors.Count==0,
                "Graphics script precompile failed: "+string.Join(";",android.Errors.Concat(web.Errors)));
            Assembly assembly=Assembly.Load(File.ReadAllBytes(Path.Combine(root,web.Entries[0].Assembly)));
            CoreWebScript module=(CoreWebScript)Activator.CreateInstance(assembly.GetType("Zhengyan.GeneratedWebScripts.Script")!)!;
            var definition=new CoreScene{Entities=[new(){Id="mirror",Type="textured_plane"}]};
            using CoreRuntimeScene scene=new("main",definition,p=>p);
            module.Start(new CoreWebContext(scene,scene.GetEntity("mirror")));
            Check(scene.Graphics.SceneSamples==2 && scene.Graphics.Shadows.FilterSamples==25 && scene.GetEntity("mirror")!.ReflectionSamples==4,
                "Precompiled Web graphics setters did not reach the shared runtime.");
            var pythonType=typeof(Zhengyan.DigitalWife.GamePlayer.CSharpScriptGlobals).Assembly.GetType("Zhengyan.DigitalWife.GamePlayer.PythonScriptInstance")!;
            string worker=(string)pythonType.GetMethod("EnsureWorkerScriptPath",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;
            var start=new System.Diagnostics.ProcessStartInfo("python"){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true,CreateNoWindow=true};
            start.ArgumentList.Add("-m");start.ArgumentList.Add("py_compile");start.ArgumentList.Add(worker);
            using var process=System.Diagnostics.Process.Start(start)!;string errors=process.StandardError.ReadToEnd();process.WaitForExit();
            Check(process.ExitCode==0,"Python graphics bridge syntax error: "+errors);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    public static void OpenGlScene()
    {
        WindowOptions options=WindowOptions.Default; RendererFactory.ConfigureWindow(ref options,GraphicsBackend.OpenGL);
        options.IsVisible=false; options.Size=new(64,64); options.Samples=4;
        using IWindow window=Window.Create(options); window.Initialize(); window.MakeCurrent();
        using OpenGlRenderer renderer=new(); renderer.Initialize(GL.GetApi(window),new(64,64),4);
        GL gl=(GL)typeof(OpenGlRenderer).GetProperty("Gl",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(renderer)!;
        uint program=gl.CreateShaderProgramFromSource("""
            #version 300 es
            void main(){ vec2 p=gl_VertexID==0?vec2(-.81,-.77):gl_VertexID==1?vec2(.86,-.31):vec2(-.37,.91);gl_Position=vec4(p,0,1); }
            ""","""
            #version 300 es
            precision highp float;out vec4 color;void main(){color=vec4(1,0,0,1);}
            """);
        uint vao=gl.GenVertexArray();
        try
        {
            foreach(int requested in new[]{4,1,2,4,8,16,1})
            {
                renderer.SetAntiAliasingSamples(requested); renderer.Clear(new(0,0,0,1));
                gl.BindVertexArray(vao);gl.UseProgram(program);gl.Disable(GLEnum.Blend);gl.DrawArrays(GLEnum.Triangles,0,3);
                byte[] pixels=new byte[64*64*4];Check(renderer.TryReadBackBufferRgba(pixels),"Scene readback failed.");
                int partial=Enumerable.Range(0,64*64).Count(i=>pixels[i*4]>0 && pixels[i*4]<255);
                Check(renderer.AntiAliasingSamples==1?partial==0:partial>0,"Scene geometry coverage did not follow runtime MSAA.");
                renderer.Present(); gl.BindFramebuffer(GLEnum.Framebuffer,0);
                byte[] displayed=new byte[pixels.Length];fixed(byte* p=displayed)gl.ReadPixels(0,0,64,64,GLEnum.Rgba,GLEnum.UnsignedByte,p);
                Check(pixels.Zip(displayed).All(p=>Math.Abs(p.First-p.Second)<=2),"Scene presentation changed resolved color.");
                Check(gl.GetError()==GLEnum.NoError,"Dynamic scene MSAA raised a GL error.");
                Console.WriteLine($"PASS runtime OpenGL scene requested={requested}x actual={renderer.AntiAliasingSamples}x partial={partial}");
            }
            using SceneColorDepthRenderTarget capture=new(gl,"RuntimeMsaaDepth");
            foreach(int samples in new[]{1,2,4,1})
            {
                capture.EnsureSize(32,32,samples);capture.Bind();gl.Disable(GLEnum.ScissorTest);gl.DepthMask(true);
                gl.ClearColor(1,0,0,1);gl.ClearDepth(.375f);gl.Clear(Silk.NET.OpenGLES.ClearBufferMask.ColorBufferBit|Silk.NET.OpenGLES.ClearBufferMask.DepthBufferBit);
                capture.Resolve();Check(gl.GetError()==GLEnum.NoError,"Multisample depth capture resolve failed.");
            }
        }
        finally {gl.UseProgram(0);gl.BindVertexArray(0);gl.DeleteProgram(program);gl.DeleteVertexArray(vao);}
    }

    public static void VulkanScene()
    {
        if(!VulkanRenderer.IsSupported(out string reason)){Console.WriteLine($"SKIP runtime Vulkan: {reason}");return;}
        WindowOptions options=WindowOptions.Default;RendererFactory.ConfigureWindow(ref options,GraphicsBackend.Vulkan);
        options.IsVisible=false;options.Size=new(64,64);
        options.WindowBorder=WindowBorder.Hidden;
        using IWindow window=Window.Create(options);window.Initialize();
        using VulkanRenderer renderer=new();renderer.Initialize(window,new(64,64),4);
        var device=(Veldrid.GraphicsDevice)typeof(VulkanRenderer).GetProperty("Device",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(renderer)!;
        var commands=(CommandList)typeof(VulkanRenderer).GetProperty("CommandList",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(renderer)!;
        ResourceFactory factory=device.ResourceFactory;
        Shader[] shaders=factory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource("runtime-msaa.vert","void main(){vec2 p=gl_VertexIndex==0?vec2(-.81,-.77):gl_VertexIndex==1?vec2(.86,-.31):vec2(-.37,.91);gl_Position=vec4(p,0,1);}",ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource("runtime-msaa.frag","layout(location=0)out vec4 color;void main(){color=vec4(1,0,0,1);}",ShaderStages.Fragment));
        using Shader vertex=shaders[0],fragment=shaders[1];
        Dictionary<int,Pipeline> pipelines=[];
        try
        {
            foreach(int requested in new[]{4,1,2,4,8,16,1})
            {
                renderer.SetAntiAliasingSamples(requested);renderer.Clear(new(0,0,0,1));
                Framebuffer main=(Framebuffer)typeof(VulkanRenderer).GetProperty("MainFramebuffer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(renderer)!;
                if(!pipelines.TryGetValue(renderer.AntiAliasingSamples,out Pipeline? pipeline))
                {
                    pipeline=factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(BlendStateDescription.SingleOverrideBlend,DepthStencilStateDescription.Disabled,
                        RasterizerStateDescription.CullNone,PrimitiveTopology.TriangleList,new ShaderSetDescription([],shaders),[],main.OutputDescription));
                    pipelines.Add(renderer.AntiAliasingSamples,pipeline);
                }
                commands.SetFullViewports();commands.SetFullScissorRects();commands.SetPipeline(pipeline);commands.Draw(3);
                Texture source=main.ColorTargets[0].Target;
                Check(AntiAliasingSamples.FromVeldrid(source.SampleCount)==renderer.AntiAliasingSamples,"Reported Vulkan samples do not match the texture enum.");
                using Texture resolved=factory.CreateTexture(TextureDescription.Texture2D(source.Width,source.Height,1,1,source.Format,TextureUsage.RenderTarget));
                using Texture capture=factory.CreateTexture(TextureDescription.Texture2D(source.Width,source.Height,1,1,source.Format,TextureUsage.Staging));
                if(source.SampleCount!=TextureSampleCount.Count1)commands.ResolveTexture(source,resolved);else commands.CopyTexture(source,resolved);
                commands.CopyTexture(resolved,capture);renderer.Present();device.WaitForIdle();
                MappedResource mapped=device.Map(capture,MapMode.Read);
                int partial=0,solid=0;
                try
                {
                    int channel=source.Format==PixelFormat.B8_G8_R8_A8_UNorm || source.Format==PixelFormat.B8_G8_R8_A8_UNorm_SRgb?2:0;
                    for(int y=0;y<source.Height;y++)for(int x=0;x<source.Width;x++)
                    {
                        byte value=*((byte*)mapped.Data+y*mapped.RowPitch+x*4+channel);
                        if(value==255)solid++;else if(value>0)partial++;
                    }
                }
                finally{device.Unmap(capture);}
                Check(solid>100 && (renderer.AntiAliasingSamples==1?partial==0:partial>0),"Vulkan scene pixels did not follow runtime MSAA.");
                Check(renderer.AntiAliasingSamples<=requested,"Runtime Vulkan exceeded requested samples.");
                Console.WriteLine($"PASS runtime Vulkan scene requested={requested}x actual={renderer.AntiAliasingSamples}x partial={partial}");
            }
            var retired=new DisposalProbe();
            typeof(VulkanRenderer).GetMethod("RetireResource",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(renderer,[retired]);
            for(int frame=0;frame<3;frame++)
            {
                renderer.Clear(Vector4.Zero);
                Check(retired.Disposed==(frame==2),"Vulkan resources were retired before their frame fence, or never released.");
                renderer.Present();
            }
            using VeldridRenderTarget target=new(renderer,"DepthCapture"){ResolveDepth=true};
            foreach(int samples in new[]{2,4,1})
            {
                target.EnsureSize(16,16,samples);target.BeginPass(Vector4.One);commands.ClearDepthStencil(.375f);target.EndPass();renderer.Present();device.WaitForIdle();
                Check(target.NativeDepthResource is TextureView view && view.Target.SampleCount==TextureSampleCount.Count1,"MSAA capture depth was not resolved for sampling.");
                Console.WriteLine($"PASS Vulkan resolved depth capture samples={target.SampleCount}");
            }
        }
        finally {device.WaitForIdle();foreach(Pipeline pipeline in pipelines.Values)pipeline.Dispose();}
    }
    private sealed class DisposalProbe : IDisposable {public bool Disposed {get;private set;}public void Dispose()=>Disposed=true;}
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}

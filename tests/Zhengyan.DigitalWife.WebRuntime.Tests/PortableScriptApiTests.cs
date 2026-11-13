using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GamePlayer.Web.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd;

internal static class PortableScriptApiTests
{
    internal static async Task RunAsync()
    {
        await NetworkAsync();
        await LlmAsync();
        Model();
        Console.WriteLine("PASS portable TCP/UDP, HTTP cancellation, command results, LLM options, script tool reentry, node/morph/material/shader control");
    }
    private static async Task NetworkAsync()
    {
        using var network=new RuntimeNetwork(new HttpClient(new BlockHandler()));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        try{await network.HttpSendAsync("GET","https://fixture.invalid",cancellationToken:cancellation.Token);throw new Exception("HTTP cancellation was ignored.");}catch(OperationCanceledException){}
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        int port=((IPEndPoint)listener.LocalEndpoint).Port;
        Task server=Task.Run(async()=>{using var socket=await listener.AcceptTcpClientAsync();var stream=socket.GetStream();byte[] bytes=new byte[64];int n=await stream.ReadAsync(bytes);await stream.WriteAsync(bytes.AsMemory(0,n));});
        Check(await network.TcpSendTextAsync("127.0.0.1",port,"hello")=="hello","TCP response was lost.");await server;listener.Stop();
        using(var udp=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))
        {
            port=((IPEndPoint)udp.Client.LocalEndPoint!).Port;
            server=Task.Run(async()=>{var packet=await udp.ReceiveAsync();await udp.SendAsync(packet.Buffer,packet.RemoteEndPoint);});
            Check(await network.UdpSendTextAsync("127.0.0.1",port,"hello")=="hello","UDP response was lost.");await server;
        }
        var runtime=new RuntimeProjectControl();var result=runtime.ExecuteShellCommand("echo portable-api");
        Check(result.Success&&result.StandardOutput.Contains("portable-api"),"Shell output or exit status was lost.");
    }
    private static async Task LlmAsync()
    {
        using var scene=new RuntimeScene("fixture",new(){Entities=[new(){Id="actor"}]},p=>p);
        var handler=new LlmHandler();using var network=new RuntimeNetwork(new HttpClient(handler));using var llm=new RuntimeLlm(scene.Save,network);
        llm.Configure(new(){Enabled=true,BaseUrl="https://fixture.invalid",Model="default",DefaultTemperature=.1f});
        var actor=scene.GetEntity("actor")!;int completed=0;
        llm.StartChat(actor,"one",model:"first",temperature:.4f,onCompleted:_=>completed++);
        llm.StartChat(actor,"two",model:"second",temperature:.8f,onCompleted:_=>completed++);
        await llm.WaitForRequestsAsync();
        Check(completed==2&&handler.Bodies.Select(b=>b.GetProperty("model").GetString()).Order().SequenceEqual(new[]{"first","second"}),"Concurrent LLM options leaked.");
        Check(handler.Bodies.Select(b=>b.GetProperty("temperature").GetSingle()).Order().SequenceEqual(new[]{.4f,.8f}),"Per-request temperature was ignored.");
        Check(await llm.ChatAsync("three",model:"third",temperature:.7f)=="ok","Direct LLM result was lost.");
        var bytes=File.ReadAllBytes(typeof(PortableToolFixture).Assembly.Location);
        var manifest=new WebAssetManifest{Assets=[new(){Path="script.dll",Size=bytes.Length},new(){Path="game.project.json",Size=2},new(){Path="scenes/main.scene.json",Size=2}],Scripts=[new(){Id="tool",Entity="actor",Assembly="script.dll",Type=typeof(PortableToolFixture).FullName!}]};
        var store=new WebMemoryAssetStore(manifest);store.Add("script.dll",bytes);store.Add("game.project.json",Encoding.UTF8.GetBytes("{}"));store.Add("scenes/main.scene.json",Encoding.UTF8.GetBytes("{}"));
        await using var scripts=new WebScriptRuntime();await scripts.LoadAsync(store,manifest,scene);await scripts.StartAsync();
        scene.ScriptToolInvoker=scripts.InvokeLlmToolAsync;
        scripts.DispatchEvent(new("gui"));await scripts.Scripts.Single().EventTask.WaitAsync(TimeSpan.FromSeconds(5));
        Check(scene.Save.ReadText("tool.txt")=="{\"answer\":42}","A script awaiting its tool callback deadlocked or lost its return value.");
        Check(scripts.Diagnostics.Count==0,"Tool fixture failed to load.");
    }
    private static void Model()
    {
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
        string modelPath=Directory.GetFiles(Path.Combine(root,"assets/mmd/samples/GameData/Character/Body"),"*.pmx").First();
        var parsing=PmxParsing.ParsingByFile(modelPath)!;
        var definition=new GameEntity{Id="actor",EnablePhysics=false,IsPlaying=false};
        using var asset=WebPmxAsset.Create(definition,parsing,[],preferGpuSkinning:true);
        using var scene=new RuntimeScene("fixture",new(){Entities=[definition]},p=>p);
        var actor=scene.GetEntity("actor")!;actor.PmxScriptApi=asset;
        string node=actor.NodeNames.First();var original=actor.GetNodeState(node);
        var offset=new Vector3(.4f,.2f,.3f);actor.SetNodeTranslate(node,original.Translate+offset);asset.Update(0,0);
        Check(Vector3.Distance(actor.GetNodeState(node).Translate,original.Translate+offset)<1e-5f,"Node translation did not reach the pose.");
        actor.SetNodeAnimTranslate(node,new Vector3(.1f,.2f,.3f));asset.Update(0,0);
        Check(Vector3.Distance(actor.GetNodeState(node).AnimTranslate,new(.1f,.2f,.3f))<1e-4f,"Animated node translation was ignored.");
        actor.SetNodeScale(node,new Vector3(1.1f));asset.Update(0,0);Check(actor.GetNodeState(node).Scale==new Vector3(1.1f),"Node scale was ignored.");
        string morph=actor.MorphNames.First();actor.SetMorphWeight(morph,.7f);asset.Update(0,0);Check(Math.Abs(actor.GetMorphWeight(morph)-.7f)<1e-5f,"Morph override was ignored.");
        actor.SaveMorphAnimWeight(morph);Check(Math.Abs(actor.GetMorphSaveAnimWeight(morph)-.7f)<1e-5f,"Saved morph state was lost.");
        asset.ScriptTextureResolver=p=>p;Check(actor.SetMaterialRenderTexture(0,"mirror")&&asset.Mesh.Materials[0].TextureUri=="rt:mirror","Material render texture binding was ignored.");
        actor.ClearMaterialTextureOverride(0);Check(asset.Mesh.Materials[0].TextureUri!="rt:mirror","Material texture clearing was ignored.");
        asset.ScriptShaderResolver=_=>"#version 330 core\nvoid main(){}";Check(actor.SetCustomShader("vertex.glsl","fragment.glsl"),"Custom shader was rejected.");
        actor.SetCustomShaderColor("tint",1,0,0);Check(!asset.GpuSkinning&&asset.CustomShader!.Uniforms["tint"].Values.SequenceEqual(new[]{1f,0f,0f,1f}),"Custom shader skinning or uniforms were ignored.");
        actor.ClearCustomShader();Check(asset.CustomShader is null,"Custom shader clearing was ignored.");
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private sealed class BlockHandler:HttpMessageHandler
    {protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){await Task.Delay(Timeout.Infinite,token);return new(HttpStatusCode.OK);}}
    private sealed class LlmHandler:HttpMessageHandler
    {
        internal List<JsonElement> Bodies=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {var body=JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(token));Bodies.Add(body);await Task.Delay(20,token);return new(HttpStatusCode.OK){Content=new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\ndata: [DONE]\n\n",Encoding.UTF8,"text/event-stream")};}
    }
}
public sealed class PortableToolFixture:IWebGameScript
{
    public void Start(WebScriptContext context){}
    public void Update(WebScriptContext context,float deltaSeconds){}
    public async ValueTask OnEventAsync(WebScriptContext context,WebScriptEvent gameEvent)
    {
        if(gameEvent.IsScriptToolCall){context.ReturnValue=new{answer=42};return;}
        if(gameEvent.IsGuiEvent)context.Scene.Save.WriteText("tool.txt",await context.Scene.InvokeLlmToolAsync(context.Entity!,"answer",new("call","answer","{}"))??"");
    }
}

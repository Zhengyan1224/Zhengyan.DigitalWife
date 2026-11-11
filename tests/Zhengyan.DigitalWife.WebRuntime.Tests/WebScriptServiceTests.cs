using System.Net;
using System.Text;
using System.Text.Json;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

internal static class WebScriptServiceTests
{
    public static async Task RunAsync()
    {
        RuntimeSaveStore save = new();
        save.WriteJson("state.json", new[] { 1, 2, 3 });
        Check(save.ReadJson<int[]>("state.json")!.SequenceEqual(new[] {1,2,3}), "Synchronous save reads lost a queued write.");
        long revision = save.Revision; save.WriteText("memory/test.md", "hello"); save.MarkPersisted(revision);
        Check(save.HasChanges, "Acknowledging an older IndexedDB write discarded a newer save.");
        RuntimeSaveStore restored = new(); restored.Initialize(save.Snapshot());
        Check(restored.ReadText("memory/test.md") == "hello" && !restored.HasChanges, "Persisted saves did not hydrate before script Start.");
        foreach (string path in new[] { "../outside", "C:/outside", "/outside", "valid/../outside" })
        { try { save.WriteText(path, "bad"); throw new Exception("Save path escape was accepted."); } catch (ArgumentException) { } }

        var handler = new Handler();
        handler.Responses.Enqueue(Sse(new { choices = new[] {new {delta = new { tool_calls = new[] {new {index=0,id="call1",function=new {name="echo",arguments="{"}}}}}}},
            new { choices = new[] {new {delta = new {tool_calls = new[] {new {index=0,function=new {arguments="\"value\":\"OK\"}"}}}}}}}));
        handler.Responses.Enqueue(Sse(new {choices=new[]{new {delta=new {content="你"}}}},new {choices=new[]{new {delta=new {content="好"}}}}));
        using RuntimeNetwork network = new(new HttpClient(handler));
        using RuntimeLlm llm = new(save, network);
        llm.Configure(new() { Enabled = true, BaseUrl = "https://fixture.invalid", Model = "fixture" });
        List<RuntimeLlmScriptEvent> events = [];
        llm.ScriptEvent += (_, value) => events.Add(value);
        var entity = new RuntimeEntity(new() { Id="actor" });
        RuntimeLlmTool echo = new("echo","Echo a value.","{\"type\":\"object\"}",arguments => JsonDocument.Parse(arguments).RootElement.GetProperty("value").GetString()!);
        llm.StartChatWithTools(entity, [new("user","hello")], [echo], requestId:"request");
        await llm.WaitForRequestsAsync();
        Check(events.Last().EventName == "completed" && events.Last().AccumulatedText == "你好", "Streamed Unicode response or completion was lost.");
        Check(events.Count(e=>e.EventName=="tool_call")==1 && events.Single(e=>e.EventName=="tool_result").ToolResult=="OK", "Fragmented tool call was not assembled and executed once.");
        using var continuation = JsonDocument.Parse(handler.Bodies[1]);
        Check(continuation.RootElement.GetProperty("messages").EnumerateArray().Any(m=>m.GetProperty("role").GetString()=="tool" && m.GetProperty("tool_call_id").GetString()=="call1"), "Tool results did not rejoin the model conversation.");

        handler.Responses.Enqueue(new(HttpStatusCode.BadGateway) { Content=new StringContent("provider unavailable") });
        llm.StartChat(entity,[new("user","retry")],requestId:"error"); await llm.WaitForRequestsAsync();
        Check(events.Last().EventName=="error" && events.Last().Error.Contains("502"),"HTTP errors did not reach the script callback.");
        int before=events.Count; handler.Block=true;
        llm.StartChat(entity,[new("user","cancel")],requestId:"cancel"); llm.CancelRequest("cancel");
        await Task.Yield();
        Check(events.Count==before,"A cancelled request dispatched a completion or error to a replacement conversation.");

        RuntimeAsr asr = new(); asr.Configure(new(){Enabled=true}); List<RuntimeAsrCommand> commands=[];asr.Command+=commands.Add;
        asr.StartWakeWordMonitoring(entity,["小雨"],requestId:"wake");
        asr.StartStreamingRecognition(entity,"question");
        Check(commands.Select(c=>c.Operation).SequenceEqual(new[]{"wake","stop_wake","start"}),"Wake monitoring and question capture overlap.");
        asr.StopStreamingRecognition("question"); Check(!asr.IsRecording,"Stopping ASR retained the recording state.");
        using var scene = new RuntimeScene("test",new(){Entities=[new(){Id="actor"}],GuiControls=[new(){X=640,Y=360,Width=100,Height=50,LayoutMode="relative"}]},p=>p);
        scene.SetScreenSize(640,360,1280,720);scene.MainCamera.ResolveViewport(640,360,1280,720);
        Check(scene.IsScreenPointOverGui(325,185) && !scene.IsScreenPointOverGui(645,365),"GUI hit tests still use a fixed PC window size.");
        var ray=scene.MainCamera.ScreenPointToRay(320,180);
        Check(System.Numerics.Vector3.Dot(ray.Direction,System.Numerics.Vector3.Normalize(scene.MainCamera.Target-scene.MainCamera.Position))>.999f,"Canvas center picking does not follow the actual viewport.");
        scene.MainCamera.ProjectionMode="orthographic";ray=scene.MainCamera.ScreenPointToRay(320,180);
        Check(Math.Abs(System.Numerics.Vector3.Distance(ray.Origin,scene.MainCamera.Position)-scene.MainCamera.Settings.NearClipPlane)<.001f,"Orthographic picking uses the wrong depth convention.");
        string assemblyPath=typeof(AsyncWebScriptFixture).Assembly.Location;
        byte[] assembly=File.ReadAllBytes(assemblyPath);
        var manifest=new WebAssetManifest{Assets=[new(){Path="compiled/script.dll",Size=assembly.Length},new(){Path="game.project.json",Size=2},new(){Path="scenes/main.scene.json",Size=2}],
            Scripts=[new(){Id="async",Assembly="compiled/script.dll",Entity="actor",Type=typeof(AsyncWebScriptFixture).FullName!}]};
        var store=new WebMemoryAssetStore(manifest);store.Add("compiled/script.dll",assembly);store.Add("game.project.json",Encoding.UTF8.GetBytes("{}"));store.Add("scenes/main.scene.json",Encoding.UTF8.GetBytes("{}"));
        using WebScriptRuntime scripts=new();await scripts.LoadAsync(store,manifest,scene);await scripts.StartAsync();
        Check(scripts.Diagnostics.Count==0,"The async script fixture failed the real assembly loader.");
        var loaded=scripts.Scripts.Single();var type=loaded.Module!.GetType();
        var gate=(TaskCompletionSource<bool>)type.GetField("Gate")!.GetValue(loaded.Module)!;
        var observed=(List<string>)type.GetField("Events")!.GetValue(loaded.Module)!;
        scripts.Update(.016f,1,default);scripts.Update(.016f,2,default);
        Check((int)type.GetField("Updates")!.GetValue(loaded.Module)! == 1,"An awaited script update overlapped with the following frame.");
        scripts.DispatchEvent(new("gui",EventName:"first"));scripts.DispatchEvent(new("gui",EventName:"second"));
        Check(observed.Count==0,"GUI events ran before the current awaited update completed.");
        gate.SetResult(true);await loaded.EventTask;
        Check(observed.SequenceEqual(new[]{"first","second"}),"Async script events lost ordering or their event context.");
        using var targets=new RuntimeScene("targets",new(){Entities=[
            new(){Id="stage",Name="Stage",Type="pmx_model",Scripts=[new(){Path="stage.csx"}]},
            new(){Id="body",Name="Body",Type="pmx_model",Scripts=[new(){Path="body.csx"}]},
            new(){Id="clothes",Name="Clothes",Type="pmx_model",Scripts=[new(){Path="clothes.csx"}],Relation=new(){Enabled=true,RelationEntity="body"}}
        ]},p=>p);
        Check(targets.SelectScriptTarget()?.Id=="body","An unbound GUI event selected the stage instead of the related character.");
        Console.WriteLine("PASS hydrated save state, path isolation, Unicode SSE, fragmented tools, HTTP errors, cancellation and ASR lifecycle");
    }
    private static HttpResponseMessage Sse(params object[] items) => new(HttpStatusCode.OK)
    { Content=new StringContent(string.Join("\n\n",items.Select(i=>"data: "+JsonSerializer.Serialize(i)))+"\n\ndata: [DONE]\n\n",Encoding.UTF8,"text/event-stream") };
    private static void Check(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    private sealed class Handler : HttpMessageHandler
    {
        public Queue<HttpResponseMessage> Responses=new();public List<string> Bodies=[];public bool Block;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        { Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));if(Block)await Task.Delay(Timeout.Infinite,cancellationToken);return Responses.Dequeue(); }
    }
}

public sealed class AsyncWebScriptFixture : IWebGameScript
{
    public readonly TaskCompletionSource<bool> Gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly List<string> Events=[];public int Updates;
    public void Start(WebScriptContext context) { }
    public void Update(WebScriptContext context,float deltaSeconds) { }
    public async ValueTask UpdateAsync(WebScriptContext context,float deltaSeconds){Updates++;await Gate.Task;}
    public ValueTask OnEventAsync(WebScriptContext context,WebScriptEvent gameEvent){Events.Add(context.Event!.EventName);return ValueTask.CompletedTask;}
}

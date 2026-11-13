using Microsoft.JSInterop;
using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed class WebPythonScript(IJSRuntime js,WebScriptEntry entry,string source,object reference,Action<string,WebPythonBridge?> register) : IWebGameScript, IAsyncDisposable
{
    private IJSObjectReference? _module;
    private readonly WebPythonBridge _bridge=new();
    public void Start(WebScriptContext context) => _=StartAsync(context);
    public void Update(WebScriptContext context,float deltaSeconds)=>_=UpdateAsync(context,deltaSeconds);
    public void OnEvent(WebScriptContext context,WebScriptEvent gameEvent)=>_=OnEventAsync(context,gameEvent);
    public async ValueTask StartAsync(WebScriptContext context)
    {
        _module=await js.InvokeAsync<IJSObjectReference>("import",WebRuntimeModuleUrl.Resolve("web-python.js"));
        register(entry.Id,_bridge);_bridge.Callback=callback=>{_=_module.InvokeVoidAsync("callCallback",entry.Id,callback);};
        _bridge.CallbackWithValue=(callback,value)=>{_=_module.InvokeVoidAsync("callCallback",entry.Id,callback,value);};
        var frame=_bridge.Frame(context,0,true,null);
        await _module.InvokeVoidAsync("createScript",entry.Id,source,reference);
        await _module.InvokeVoidAsync("executeScript",entry.Id,frame);
    }
    public async ValueTask UpdateAsync(WebScriptContext context,float deltaSeconds)
    {
        if(_module is not null)await _module.InvokeVoidAsync("executeScript",entry.Id,_bridge.Frame(context,deltaSeconds,false,null));
    }
    public async ValueTask OnEventAsync(WebScriptContext context,WebScriptEvent gameEvent)
    {
        if(_module is not null)
        {
            var frame=_bridge.Frame(context,0,false,gameEvent);
            if(gameEvent.IsScriptToolCall)context.ReturnValue=await _module.InvokeAsync<string?>("executeToolScript",entry.Id,frame);
            else await _module.InvokeVoidAsync("executeScript",entry.Id,frame);
        }
    }
    public async ValueTask DisposeAsync()
    {
        _bridge.Callback=null;
        _bridge.CallbackWithValue=null;
        if(_module is not null){await _module.InvokeVoidAsync("removeScript",entry.Id);await _module.DisposeAsync();_module=null;}
        register(entry.Id,null);
    }
}

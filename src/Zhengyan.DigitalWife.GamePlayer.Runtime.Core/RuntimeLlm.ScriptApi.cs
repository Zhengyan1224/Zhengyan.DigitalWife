using System.Runtime.CompilerServices;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed record RuntimeLlmResult(string RequestId,string Text);
public sealed record RuntimeLlmScriptTool(string Name,string Description,string ParametersJsonSchema,string CallbackName)
{
    public RuntimeLlmTool ToTool(RuntimeEntity entity,RuntimeScene scene)=>new(Name,Description,ParametersJsonSchema,
        async(call,token)=>{token.ThrowIfCancellationRequested();return await scene.InvokeLlmToolAsync(entity,CallbackName,call).WaitAsync(token)??"{}";});
}

public sealed partial class RuntimeLlm
{
    private sealed record ScriptOptions(string? Model,float? Temperature,Action<RuntimeLlmStreamUpdate>? Delta=null,Action<RuntimeLlmResult>? Completed=null,Action<Exception>? Error=null);
    private readonly AsyncLocal<ScriptOptions?> _scriptOptions=new();
    public string Provider=>_settings.Provider;
    public string ChatCompletionsPath=>_settings.ChatCompletionsPath;
    public float? DefaultTemperature=>_settings.DefaultTemperature;
    private T WithScriptOptions<T>(Func<T> action,string? model,float? temperature,Action<RuntimeLlmStreamUpdate>? delta=null,Action<RuntimeLlmResult>? completed=null,Action<Exception>? error=null)
    {
        var previous=_scriptOptions.Value;_scriptOptions.Value=new(model??Model,temperature??DefaultTemperature,delta,completed,error);
        try{return action();}finally{_scriptOptions.Value=previous;}
    }
    public async Task<string> ChatAsync(string userText,string? systemPrompt=null,string? model=null,float? temperature=null,CancellationToken cancellationToken=default)
    {string text="";await foreach(var update in StreamChatAsync(userText,systemPrompt,model,temperature,cancellationToken))text=update.AccumulatedText;return text;}
    public async Task<string> ChatWithToolsAsync(string userText,IEnumerable<RuntimeLlmTool> tools,string? systemPrompt=null,string? model=null,float? temperature=null,int maxToolRounds=8,CancellationToken cancellationToken=default)
    {string text="";await foreach(var update in StreamChatWithToolsAsync(Messages(userText,systemPrompt),tools,model,temperature,maxToolRounds,cancellationToken))text=update.AccumulatedText;return text;}
    public string StartChat(RuntimeEntity callbackTarget,string userText,string? systemPrompt=null,string? model=null,float? temperature=null,
        Action<RuntimeLlmStreamUpdate>? onDelta=null,Action<RuntimeLlmResult>? onCompleted=null,Action<Exception>? onError=null,string? requestId=null,
        string? onDeltaCallback="llm_delta",string? onCompletedCallback="llm_completed",string? onErrorCallback="llm_error")
        =>StartChat(callbackTarget,Messages(userText,systemPrompt),model,temperature,onDelta,onCompleted,onError,requestId,onDeltaCallback,onCompletedCallback,onErrorCallback);
    public string StartChat(RuntimeEntity callbackTarget,IEnumerable<RuntimeLlmChatMessage> messages,string? model=null,float? temperature=null,
        Action<RuntimeLlmStreamUpdate>? onDelta=null,Action<RuntimeLlmResult>? onCompleted=null,Action<Exception>? onError=null,string? requestId=null,
        string? onDeltaCallback="llm_delta",string? onCompletedCallback="llm_completed",string? onErrorCallback="llm_error")
        =>WithScriptOptions(()=>StartChatWithTools(callbackTarget,messages.ToArray(),Array.Empty<RuntimeLlmTool>(),requestId,onDeltaCallback,onCompletedCallback,onErrorCallback,"llm_tool_call","llm_tool_result",8),model,temperature,onDelta,onCompleted,onError);
    public string StartChatWithTools(RuntimeEntity callbackTarget,string userText,IEnumerable<RuntimeLlmTool> tools,string? systemPrompt=null,string? model=null,float? temperature=null,
        string? requestId=null,string? onDeltaCallback="llm_delta",string? onCompletedCallback="llm_completed",string? onErrorCallback="llm_error",string? onToolCallCallback="llm_tool_call",string? onToolResultCallback="llm_tool_result",int maxToolRounds=8)
        =>StartChatWithTools(callbackTarget,Messages(userText,systemPrompt),tools,model,temperature,requestId,onDeltaCallback,onCompletedCallback,onErrorCallback,onToolCallCallback,onToolResultCallback,maxToolRounds);
    public string StartChatWithTools(RuntimeEntity callbackTarget,IEnumerable<RuntimeLlmChatMessage> messages,IEnumerable<RuntimeLlmTool> tools,string? model=null,float? temperature=null,
        string? requestId=null,string? onDeltaCallback="llm_delta",string? onCompletedCallback="llm_completed",string? onErrorCallback="llm_error",string? onToolCallCallback="llm_tool_call",string? onToolResultCallback="llm_tool_result",int maxToolRounds=8)
        =>WithScriptOptions(()=>StartChatWithTools(callbackTarget,messages.ToArray(),tools.ToArray(),requestId,onDeltaCallback,onCompletedCallback,onErrorCallback,onToolCallCallback,onToolResultCallback,maxToolRounds),model,temperature);
    public IAsyncEnumerable<RuntimeLlmStreamUpdate> StreamChatAsync(string userText,string? systemPrompt=null,string? model=null,float? temperature=null,CancellationToken cancellationToken=default)
        =>StreamChatWithToolsAsync(Messages(userText,systemPrompt),Array.Empty<RuntimeLlmTool>(),model,temperature,cancellationToken:cancellationToken);
    public IAsyncEnumerable<RuntimeLlmStreamUpdate> StreamChatAsync(IEnumerable<RuntimeLlmChatMessage> messages,string? model=null,float? temperature=null,CancellationToken cancellationToken=default)
        =>StreamChatWithToolsAsync(messages,Array.Empty<RuntimeLlmTool>(),model,temperature,cancellationToken:cancellationToken);
    public async IAsyncEnumerable<RuntimeLlmStreamUpdate> StreamChatWithToolsAsync(IEnumerable<RuntimeLlmChatMessage> messages,IEnumerable<RuntimeLlmTool> tools,
        string? model=null,float? temperature=null,int maxToolRounds=8,[EnumeratorCancellation]CancellationToken cancellationToken=default)
    {
        await using var stream=StreamChatWithToolsAsync(messages.ToArray(),tools.ToArray(),maxToolRounds,cancellationToken:cancellationToken).GetAsyncEnumerator(cancellationToken);
        while(await WithScriptOptions(()=>stream.MoveNextAsync(),model,temperature))yield return stream.Current;
    }
}

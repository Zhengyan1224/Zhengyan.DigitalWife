using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed record WebPythonRequest(int Handle,string Operation,string Name,JsonElement Arguments,JsonElement Value);

/// <summary>Object handles for Pyodide; accesses the portable engine API inside WASM.</summary>
public sealed class WebPythonBridge
{
    private readonly Dictionary<int,object?> _objects=[];
    private readonly Dictionary<string,int> _stableIds=new();
    private int _nextId=10;
    private RuntimeSaveStore? _save;
    public IReadOnlyDictionary<string,string> ProjectFiles { get; set; } = new Dictionary<string,string>();
    public Action<int>? Callback { get; set; }
    public Action<int,object?>? CallbackWithValue {get;set;}
    public object Frame(WebScriptContext context,float delta,bool start,WebScriptEvent? gameEvent)
    {
        _objects[1]=context.Scene;_objects[2]=context.Entity;_objects[3]=context.Input;_objects[4]=context.Audio;
        _save=context.Scene.Save;
        return new {Scene=new{__handle=1},Entity=context.Entity is null?null:new{__handle=2},Input=new{__handle=3},Audio=new{__handle=4},
            Graphics=Encode(context.Graphics),Time=Encode(context.Time),GameEvent=Encode(gameEvent),ElapsedSeconds=context.ElapsedSeconds,
            IsAndroid=false,IsWindows=false,IsLinux=false,IsMacOS=false,IsWeb=true,PlatformName="Web",
            IsTrayMenuEvent=false,TrayMenuItemId="",TrayMenuItemText="",TrayMenuEventName="",
            IsLoadingEvent=gameEvent?.IsLoadingEvent??false,LoadingEventName=gameEvent?.IsLoadingEvent==true?gameEvent.EventName:"",
            LoadingProgress=gameEvent?.LoadingProgress??0,LoadingMessage=gameEvent?.LoadingMessage??"",
            DeltaSeconds=delta,IsStart=start,IsUpdate=gameEvent is null && !start,IsGuiEvent=gameEvent?.IsGuiEvent??false,
            GuiControlId=gameEvent?.ControlId??"",GuiControlName=gameEvent?.ControlName??"",GuiEventName=gameEvent?.EventName??"",
            IsSpeechEvent=gameEvent?.IsSpeechEvent??false,SpeechCallbackName=gameEvent?.CallbackName??"",
            IsAsrEvent=gameEvent?.IsAsrEvent??false,AsrText=gameEvent?.Asr?.Text??"",AsrCallbackName=gameEvent?.Asr?.CallbackName??"",
            IsSpriteEvent=gameEvent?.IsSpriteEvent??false,SpriteId=gameEvent?.SpriteId??"",SpriteName=gameEvent?.SpriteName??"",SpriteEventName=gameEvent?.SpriteEventName??"",
            AsrEvent=Encode(gameEvent?.Asr),AsrRequestId=gameEvent?.Asr?.RequestId??"",AsrEventName=gameEvent?.Asr?.EventName??"",AsrError=gameEvent?.Asr?.Error??"",AsrWakeWord=gameEvent?.Asr?.WakeWord??"",AsrIsFinal=gameEvent?.Asr?.IsFinal??false,
            AsrOffsetSeconds=gameEvent?.Asr?.OffsetSeconds??0,AsrRecognizedText=gameEvent?.Asr?.RecognizedText??"",
            IsLlmEvent=gameEvent?.IsLlmEvent??false,LlmText=gameEvent?.Llm?.AccumulatedText??"",LlmDelta=gameEvent?.Llm?.Delta??"",LlmCallbackName=gameEvent?.Llm?.CallbackName??"",
            LlmEvent=Encode(gameEvent?.Llm),LlmRequestId=gameEvent?.Llm?.RequestId??"",LlmEventName=gameEvent?.Llm?.EventName??"",LlmError=gameEvent?.Llm?.Error??"",LlmIsFinal=gameEvent?.Llm?.IsFinal??false,
            LlmToolCall=Encode(gameEvent?.Llm?.ToolCall),LlmToolName=gameEvent?.Llm?.ToolCall?.Name??"",LlmToolCallId=gameEvent?.Llm?.ToolCall?.Id??"",
            LlmToolArgumentsJson=gameEvent?.Llm?.ToolCall?.ArgumentsJson??"",LlmToolResult=gameEvent?.Llm?.ToolResult??"",
            IsRealtimeVoiceEvent=gameEvent?.IsRealtimeVoiceEvent??false,RealtimeVoiceEvent=Encode(gameEvent?.RealtimeVoice),RealtimeVoiceRequestId=gameEvent?.RealtimeVoice?.RequestId??"",
            RealtimeVoiceEventName=gameEvent?.RealtimeVoice?.EventName??"",RealtimeVoiceCallbackName=gameEvent?.RealtimeVoice?.CallbackName??"",RealtimeVoiceText=gameEvent?.RealtimeVoice?.Text??"",
            RealtimeVoiceDelta=gameEvent?.RealtimeVoice?.Delta??"",RealtimeVoiceAccumulatedText=gameEvent?.RealtimeVoice?.AccumulatedText??"",RealtimeVoiceError=gameEvent?.RealtimeVoice?.Error??"",
            RealtimeVoiceWakeWord=gameEvent?.RealtimeVoice?.WakeWord??"",RealtimeVoiceRecognizedText=gameEvent?.RealtimeVoice?.RecognizedText??"",RealtimeVoiceIsFinal=gameEvent?.RealtimeVoice?.IsFinal??false};
    }
    public object Files()=>new{Project=ProjectFiles,Save=_save?.Snapshot()};
    public void SaveFiles(string json)
    {
        if(_save is null)return;
        var delta=JsonSerializer.Deserialize<SaveDelta>(json,new JsonSerializerOptions(JsonSerializerDefaults.Web))??new();
        foreach(string path in delta.Deleted)_save.Delete(path);
        foreach(var file in delta.Written)if(!_save.Exists(file.Key) || _save.ReadText(file.Key)!=file.Value)_save.WriteText(file.Key,file.Value);
    }
    private sealed class SaveDelta
    {
        public Dictionary<string,string> Written { get; set; } = [];
        public string[] Deleted { get; set; } = [];
    }
    public object? Invoke(WebPythonRequest request)
    {
        if(!_objects.TryGetValue(request.Handle,out var target) || target is null)return null;
        Type type=target.GetType();
        if(request.Name=="GetType")return type.FullName;
        if(request.Operation=="get")
        {
            if(target is RuntimeSaveStore && request.Name=="SaveDirectory")return "save";
            var property=type.GetProperty(request.Name);if(property is not null && property.GetIndexParameters().Length==0)return Encode(property.GetValue(target));
            var field=type.GetField(request.Name);if(field is not null)return Encode(field.GetValue(target));
            if(type.GetMethods().Any(m=>m.Name==request.Name))return new{__method=request.Name};
            throw new MissingMemberException(type.FullName,request.Name);
        }
        if(request.Operation=="set")
        {
            var property=type.GetProperty(request.Name);if(property?.CanWrite==true){property.SetValue(target,ConvertValue(request.Value,property.PropertyType));return null;}
            var field=type.GetField(request.Name);if(field is not null && !field.IsInitOnly){field.SetValue(target,ConvertValue(request.Value,field.FieldType));return null;}
            throw new MissingMemberException(type.FullName,request.Name);
        }
        if(request.Operation=="iter" && target is IEnumerable items)return items.Cast<object?>().Select(Encode).ToArray();
        var arguments=request.Arguments.ValueKind==JsonValueKind.Array?request.Arguments.EnumerateArray().ToArray():[];
        var named=request.Value.ValueKind==JsonValueKind.Object?request.Value.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value):new Dictionary<string,JsonElement>();
        if(target is RuntimeSaveStore save && request.Operation=="call")
        {
            if(request.Name=="GetFullPath" && arguments.Length==1)return "save/"+RuntimeSaveStore.Normalize(arguments[0].GetString()!);
            if(request.Name=="WriteJson" && arguments.Length==2){save.WriteText(arguments[0].GetString()!,arguments[1].GetRawText());return null;}
            if(request.Name=="ReadJson" && arguments.Length>=1){string text=save.ReadText(arguments[0].GetString()!,"");return text.Length==0?(arguments.Length>1?arguments[1]:null):JsonSerializer.Deserialize<JsonElement>(text);}
        }
        if(request.Operation=="index")
        {
            var property=type.GetProperties().First(p=>p.GetIndexParameters().Length==1);
            return Encode(property.GetValue(target,[ConvertValue(arguments[0],property.GetIndexParameters()[0].ParameterType)]));
        }
        Exception? conversion=null;
        foreach(var method in type.GetMethods(BindingFlags.Instance|BindingFlags.Public).Where(m=>m.Name==request.Name && !m.ContainsGenericParameters))
        {
            var parameters=method.GetParameters();var input=parameters.Where(p=>!p.IsOut).ToArray();
            if(arguments.Length>input.Length || input.Skip(arguments.Length).Any(p=>!p.HasDefaultValue && !named.ContainsKey(p.Name!)))continue;
            if(named.Keys.Any(k=>!input.Any(p=>p.Name==k)))continue;
            try
            {
                object?[] values=new object?[parameters.Length];int index=0;
                for(int i=0;i<parameters.Length;i++){var p=parameters[i];if(p.IsOut)values[i]=null;else values[i]=index<arguments.Length?ConvertValue(arguments[index++],p.ParameterType):named.TryGetValue(p.Name!,out var arg)?ConvertValue(arg,p.ParameterType):p.DefaultValue;}
                object? result=method.Invoke(target,values);
                var output=parameters.Select((p,i)=>(p,i)).Where(x=>x.p.IsOut).Select(x=>Encode(values[x.i])).ToArray();
                return output.Length>0?new{__tuple=new[]{Encode(result)}.Concat(output).ToArray()}:Encode(result);
            }
            catch(ArgumentException exception){conversion=exception;}
            catch(JsonException exception){conversion=new ArgumentException(exception.Message,exception);}
            catch(TargetInvocationException exception){throw exception.InnerException??exception;}
        }
        throw conversion??new MissingMethodException(type.FullName,request.Name);
    }
    public async Task<object?> Await(int handle)
    {
        if(_objects.GetValueOrDefault(handle) is not Task task)throw new InvalidOperationException("Python await requires an engine task.");
        try{await task;return Encode(task.GetType().GetProperty("Result")?.GetValue(task));}finally{_objects.Remove(handle);}
    }
    private object? ConvertValue(JsonElement json,Type type)
    {
        if(type.IsByRef)type=type.GetElementType()!;
        type=Nullable.GetUnderlyingType(type)??type;
        if(json.ValueKind==JsonValueKind.Null)return null;
        if(json.ValueKind==JsonValueKind.Object && json.TryGetProperty("__handle",out var handle))
        {var instance=_objects[handle.GetInt32()];if(instance is not null&&!type.IsInstanceOfType(instance))throw new ArgumentException("The object does not match this overload.");return instance;}
        if(json.ValueKind==JsonValueKind.Object && json.TryGetProperty("__callback",out var callback))
        {
            int id=callback.GetInt32();
            if(type==typeof(Action))return (Action)(()=>Callback?.Invoke(id));
            if(type==typeof(Action<RuntimeLlmStreamUpdate>))return (Action<RuntimeLlmStreamUpdate>)(value=>CallbackWithValue?.Invoke(id,Encode(value)));
            if(type==typeof(Action<RuntimeLlmResult>))return (Action<RuntimeLlmResult>)(value=>CallbackWithValue?.Invoke(id,Encode(value)));
            if(type==typeof(Action<Exception>))return (Action<Exception>)(value=>CallbackWithValue?.Invoke(id,value.Message));
            throw new ArgumentException("The callback does not match this overload.");
        }
        if(json.ValueKind==JsonValueKind.Object && json.TryGetProperty("__vector",out var vector))
        {
            float[] v=vector.EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            if(type==typeof(Vector2))return new Vector2(v[0],v[1]);if(type==typeof(Vector3))return new Vector3(v[0],v[1],v[2]);if(type==typeof(Vector4))return new Vector4(v[0],v[1],v[2],v[3]);
        }
        if(type.IsEnum)return json.ValueKind==JsonValueKind.String?Enum.Parse(type,json.GetString()!,true):Enum.ToObject(type,json.GetInt32());
        if(type.IsArray && json.ValueKind==JsonValueKind.Array)
        {
            var values=json.EnumerateArray().ToArray();var array=Array.CreateInstance(type.GetElementType()!,values.Length);
            for(int i=0;i<values.Length;i++)array.SetValue(ConvertValue(values[i],type.GetElementType()!),i);return array;
        }
        return JsonSerializer.Deserialize(json.GetRawText(),type,new JsonSerializerOptions(JsonSerializerDefaults.Web){IncludeFields=true});
    }
    private object? Encode(object? value)
    {
        if(value is null || value is string || value.GetType().IsPrimitive || value is decimal)return value;
        if(value is IDictionary dictionary){var result=new Dictionary<string,object?>();foreach(object key in dictionary.Keys)result[key.ToString()!]=Encode(dictionary[key]);return result;}
        if(value is Enum)return value.ToString();
        if(value is JsonElement)return value;
        if(value is Task task){int handle=_nextId++;_objects[handle]=task;return new{__task=handle};}
        if(value is ValueTask valueTask)return Encode(valueTask.AsTask());
        if(value is Vector2 v2)return new{__vector=new[]{v2.X,v2.Y}};if(value is Vector3 v3)return new{__vector=new[]{v3.X,v3.Y,v3.Z}};if(value is Vector4 v4)return new{__vector=new[]{v4.X,v4.Y,v4.Z,v4.W}};
        if(value is IEnumerable sequence)return sequence.Cast<object?>().Select(Encode).ToArray();
        Type type=value.GetType();string identity=type.FullName+":"+(type.GetProperty("Id")?.GetValue(value)?.ToString()??System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value).ToString());
        if(!_stableIds.TryGetValue(identity,out int id))_stableIds[identity]=id=_nextId++;
        _objects[id]=value;return new{__handle=id};
    }
}

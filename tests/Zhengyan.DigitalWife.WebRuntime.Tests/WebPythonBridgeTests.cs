using System.Numerics;
using System.Text.Json;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

static class WebPythonBridgeTests
{
    public static async Task RunAsync()
    {
        RuntimeSpeechRequest? speech=null;
        using var scene=new RuntimeScene("scenes/main.scene.json",new(){Entities=[new(){Id="actor",Name="Actor",MotionLayers=[new(){Path="walk.vmd"}]}]},p=>p,speechRequest:(_,value)=>speech=value);
        var entity=scene.GetEntity("actor")!;var bridge=new WebPythonBridge();bridge.Frame(new(scene,entity),0,true,null);
        JsonElement J(object? value)=>JsonSerializer.SerializeToElement(value);
        object? Call(int handle,string name,object[] arguments,object? named=null)=>bridge.Invoke(new(handle,"call",name,J(arguments),J(named)));
        object? Get(int handle,string name)=>bridge.Invoke(new(handle,"get",name,J(Array.Empty<object>()),J(null)));
        int Handle(object value)=>J(value).GetProperty("__handle").GetInt32();
        bridge.Invoke(new(2,"set","Position",J(Array.Empty<object>()),J(new{__vector=new[]{1f,2f,3f}})));
        Check(entity.Position==new Vector3(1,2,3),"Python vector assignment failed");
        Call(2,"SetMotionLayerWeight",["walk.vmd",.25f]);Check(entity.MotionLayers[0].Weight==.25f,"Python string overload failed");
        Call(2,"SetMotionLayerWeight",[0,.75f]);Check(entity.MotionLayers[0].Weight==.75f,"Python integer overload failed");
        Call(2,"BindRelation",["parent"],new{bindLighting=true});Check(entity.Definition.Relation.BindLighting,"Python named optional argument failed");
        int save=Handle(Get(1,"Save")!);Call(save,"WriteText",["test.txt","hello"]);
        Check((string?)Call(save,"ReadText",["test.txt"])=="hello","Python save API failed");
        Check(J(Call(save,"Snapshot",[])!).GetProperty("test.txt").GetString()=="hello","Python dictionary decoding failed");
        Call(save,"WriteJson",["data.json",new{number=42}]);Check(J(Call(save,"ReadJson",["data.json"])!).GetProperty("number").GetInt32()==42,"Python JSON save failed");
        bridge.SaveFiles("{\"written\":{\"file.txt\":\"offline\"},\"deleted\":[]}");Check(scene.Save.ReadText("file.txt")=="offline" && scene.Save.ReadText("test.txt")=="hello","Python virtual save sync overwrote direct API writes");
        int realtime=Handle(Get(1,"RealtimeVoice")!);int task=J(Call(realtime,"ResetConversationAsync",[])!).GetProperty("__task").GetInt32();await bridge.Await(task);
        int callback=-1;bridge.Callback=value=>callback=value;Call(2,"Speak",["test",new{__callback=9}]);speech!.Completion!();Check(callback==9,"Python callback bridge failed");
        Console.WriteLine("PASS Python vectors, overloads, kwargs, save files, async tasks and callbacks");
    }
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}

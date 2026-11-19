using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ImGuiNET;
using Silk.NET.Windowing;
using Zhengyan.DigitalWife.GamePlayer;
using Zhengyan.DigitalWife.Mmd.Game;

internal static class MenuExitRegression
{
    public static unsafe void Run()
    {
        var game=(Game)RuntimeHelpers.GetUninitializedObject(typeof(CSharpScriptGlobals).Assembly.GetType("Zhengyan.DigitalWife.GamePlayer.GamePlayerGame")!);
        var window=DispatchProxy.Create<IWindow, ExitWindowProxy>();
        var proxy=(ExitWindowProxy)(object)window;
        typeof(Game).GetField("_window",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(game,window);
        typeof(Game).GetField("_afterFrame",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(game,new ConcurrentQueue<Action>());
        var context=ImGui.CreateContext();
        proxy.OnClose=()=>ImGui.DestroyContext(context);
        try
        {
            var io=ImGui.GetIO();io.DisplaySize=new Vector2(320,240);io.DeltaTime=1f/60;
            io.Fonts.GetTexDataAsRGBA32(out byte* pixels,out int width,out int height);
            ImGui.NewFrame();ImGui.Begin("exit fixture");ImGui.OpenPopup("menu");
            if(!ImGui.BeginPopup("menu"))throw new InvalidOperationException("Popup did not open.");
            game.PostAfterFrame(game.Exit);
            // Calling Exit from any draw callback must also be safe.
            game.Exit();
            if(proxy.Closes!=0)throw new InvalidOperationException("Exit destroyed native state inside the popup.");
            ImGui.CloseCurrentPopup();ImGui.EndPopup();ImGui.End();ImGui.Render();
            typeof(Game).GetMethod("DispatchAfterFrame",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(game,null);
            if(proxy.Closes!=0)throw new InvalidOperationException("Callback must request exit before close.");
            typeof(Game).GetMethod("CloseIfRequested",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(game,null);
            if(proxy.Closes!=1)throw new InvalidOperationException("Window did not close once after rendering.");
            typeof(Game).GetMethod("CloseIfRequested",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(game,null);
            if(proxy.Closes!=1)throw new InvalidOperationException("Exit was not idempotent.");
        }
        finally {if(proxy.Closes==0)ImGui.DestroyContext(context);}
    }
}

public class ExitWindowProxy : DispatchProxy
{
    public int Closes;
    public Action? OnClose;
    protected override object? Invoke(MethodInfo? method,object?[]? args)
    {
        if(method?.Name=="Close"){Closes++;OnClose?.Invoke();return null;}
        throw new NotSupportedException(method?.Name);
    }
}

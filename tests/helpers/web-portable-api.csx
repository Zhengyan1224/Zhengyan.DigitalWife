using Zhengyan.DigitalWife.GamePlayer;

if (IsStart)
{
    bool blocked = false;
    try { await Scene.Network.TcpSendTextAsync("127.0.0.1", 12345, "fixture"); }
    catch (PlatformNotSupportedException) { blocked = true; }
    var command = Scene.Runtime.ExecuteShellCommand("echo fixture");
    var bubble = Scene.Bubble.GetOrCreate("api-fixture");
    bubble.UseScreenSpace(500, 430);
    bubble.Width = 180;
    bubble.SetText(blocked && !command.Success && command.ExitCode == -1 && Time.UnixTimeSeconds > 0 ? "api-ready" : "api-failed");
    bubble.Show();
}
if (IsGuiEvent)
{
    var tool = new RuntimeLlmScriptTool("answer", "Fixture", "{\"type\":\"object\"}", "answer").ToTool(Entity!, Scene);
    string result = await tool.InvokeAsync(new RuntimeLlmToolCall("call", "answer", "{}"), CancellationToken.None);
    Scene.Bubble.ShowText("api-fixture", result);
}
if (LlmEventName == "tool_execute") return new { answer = 42 };
return null;

using Microsoft.JSInterop;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed class WebSpeechService(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime = jsRuntime;
    private IJSObjectReference? _module;
    public async ValueTask SpeakAsync(string text, string language = "zh-CN", float rate = 1, float pitch = 1, float volume = 1)
        => await (await ModuleAsync()).InvokeVoidAsync("speak", text, language, rate, pitch, volume);
    public async ValueTask SpeakWithCallbackAsync(
        string text,
        string language,
        float rate,
        float pitch,
        float volume,
        object callbackReference,
        string entityId,
        string callbackName,int? speakerId=null)
        => await (await ModuleAsync()).InvokeVoidAsync(
            "speakWithCallback",
            text,
            language,
            rate,
            pitch,
            volume,
            callbackReference,
            entityId,
            callbackName,speakerId);
    public async ValueTask StopAsync() => await (await ModuleAsync()).InvokeVoidAsync("stopSpeaking");
    private async ValueTask<IJSObjectReference> ModuleAsync() => _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", WebRuntimeModuleUrl.Resolve("web-platform.js"));
    public async ValueTask DisposeAsync() { if (_module is not null) await _module.DisposeAsync(); _module = null; }
}

public sealed class WebMicrophoneService(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime = jsRuntime;
    private IJSObjectReference? _module;
    public async ValueTask<byte[]> RecordAsync(int maxMilliseconds = 15000)
        => await (await ModuleAsync()).InvokeAsync<byte[]>("recordMicrophone", maxMilliseconds);
    public async ValueTask StopAsync() => await (await ModuleAsync()).InvokeVoidAsync("stopMicrophone");
    private async ValueTask<IJSObjectReference> ModuleAsync() => _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", WebRuntimeModuleUrl.Resolve("web-platform.js"));
    public async ValueTask DisposeAsync() { if (_module is not null) await _module.DisposeAsync(); _module = null; }
}

public sealed class WebNetworkService(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime = jsRuntime;
    private IJSObjectReference? _module;
    public async ValueTask<T?> FetchJsonAsync<T>(string url, string method = "GET", object? body = null, IReadOnlyDictionary<string, string>? headers = null)
        => await (await ModuleAsync()).InvokeAsync<T?>("fetchJson", url, method, body, headers ?? new Dictionary<string, string>());
    public async ValueTask ConnectWebSocketAsync(string url) => await (await ModuleAsync()).InvokeVoidAsync("connectWebSocket", url);
    public async ValueTask SendWebSocketAsync(string text) => await (await ModuleAsync()).InvokeVoidAsync("sendWebSocket", text);
    public async ValueTask CloseWebSocketAsync() => await (await ModuleAsync()).InvokeVoidAsync("closeWebSocket");
    private async ValueTask<IJSObjectReference> ModuleAsync() => _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", WebRuntimeModuleUrl.Resolve("web-platform.js"));
    public async ValueTask DisposeAsync() { if (_module is not null) await _module.DisposeAsync(); _module = null; }
}

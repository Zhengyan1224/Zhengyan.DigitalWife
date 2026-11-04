using Microsoft.JSInterop;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed class WebAudioService(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime = jsRuntime;
    private IJSObjectReference? _module;

    public async ValueTask<string> UnlockAsync()
        => await (await ModuleAsync()).InvokeAsync<string>("unlock");

    public async ValueTask PlayAsync(string key, string uri, bool loop = false, float volume = 1.0f)
        => await (await ModuleAsync()).InvokeVoidAsync("play", key, uri, loop, volume);

    public async ValueTask PlayOneShotAsync(string uri, float volume = 1.0f)
        => await (await ModuleAsync()).InvokeVoidAsync("playOneShot", uri, volume);

    public async ValueTask PauseAsync(string key)
        => await (await ModuleAsync()).InvokeVoidAsync("pause", key);

    public async ValueTask StopAsync(string key)
        => await (await ModuleAsync()).InvokeVoidAsync("stop", key);

    public async ValueTask SetVolumeAsync(string key, float volume)
        => await (await ModuleAsync()).InvokeVoidAsync("setVolume", key, volume);

    public async ValueTask SetLoopAsync(string key, bool loop)
        => await (await ModuleAsync()).InvokeVoidAsync("setLoop", key, loop);

    public async ValueTask StopAllAsync()
        => await (await ModuleAsync()).InvokeVoidAsync("stopAll");

    private async ValueTask<IJSObjectReference> ModuleAsync()
        => _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/web-audio.js");

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try { await _module.InvokeVoidAsync("dispose"); }
            catch (JSDisconnectedException) { }
            await _module.DisposeAsync();
        }
        _module = null;
    }
}

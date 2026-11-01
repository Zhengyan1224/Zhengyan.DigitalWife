using System.Text.Json;
using Microsoft.JSInterop;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed class WebSaveStore(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime = jsRuntime;
    private IJSObjectReference? _module;

    public async ValueTask SaveAsync<T>(string key, T value)
        => await (await ModuleAsync()).InvokeVoidAsync("putJson", key, value);

    public async ValueTask<T?> LoadAsync<T>(string key)
        => await (await ModuleAsync()).InvokeAsync<T?>("getJson", key);

    public async ValueTask DeleteAsync(string key)
        => await (await ModuleAsync()).InvokeVoidAsync("removeJson", key);

    private async ValueTask<IJSObjectReference> ModuleAsync()
        => _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/web-storage.js");

    public async ValueTask DisposeAsync()
    {
        if (_module is not null) await _module.DisposeAsync();
        _module = null;
    }
}

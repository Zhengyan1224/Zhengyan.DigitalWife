using Microsoft.JSInterop;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed record WebGpuCapability(
    bool Available,
    string Name,
    string Vendor,
    string Architecture,
    int MaxTextureDimension2D,
    int MaxBindGroups,
    string Reason);

public sealed class WebGpuCapabilityService(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime = jsRuntime;
    private IJSObjectReference? _module;
    public async ValueTask<WebGpuCapability> DetectAsync()
        => await (await ModuleAsync()).InvokeAsync<WebGpuCapability>("detectWebGpu");
    private async ValueTask<IJSObjectReference> ModuleAsync() => _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/web-gpu.js");
    public async ValueTask DisposeAsync() { if (_module is not null) await _module.DisposeAsync(); _module = null; }
}

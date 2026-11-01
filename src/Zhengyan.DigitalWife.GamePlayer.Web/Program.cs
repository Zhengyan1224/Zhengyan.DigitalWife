using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Web;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
        builder.Services.AddScoped<WebHttpAssetStore>();
        builder.Services.AddScoped<IWebAssetStore>(sp => sp.GetRequiredService<WebHttpAssetStore>());
        builder.Services.AddScoped<WebIndexedDbAssetCache>();
        builder.Services.AddScoped<WebSaveStore>();
        builder.Services.AddScoped<WebAudioService>();
        builder.Services.AddScoped<WebSpeechService>();
        builder.Services.AddScoped<WebMicrophoneService>();
        builder.Services.AddScoped<WebNetworkService>();
        builder.Services.AddScoped<WebGpuCapabilityService>();
        builder.Services.AddScoped<WebProjectLoader>();
        builder.Services.AddScoped<WebRuntimeHost>();

        await builder.Build().RunAsync();
    }
}

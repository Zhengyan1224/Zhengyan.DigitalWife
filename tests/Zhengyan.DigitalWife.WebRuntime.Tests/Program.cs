using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

VerifyVertexUpdates();
await VerifyFrameInputAndStartupAsync();
await VerifyFrameInputAndStartupAsync(includeModels: true);
Console.WriteLine("Web runtime C# regression tests passed.");
return 0;

static void VerifyVertexUpdates()
{
    WebPmxMesh mesh = new(
        [1, 2, 3, 4, 5, 6], [0, 0, 1, 0, 1, 0], [0.1f, 0.2f, 0.3f, 0.4f],
        [0u, 1u, 0u], Vector3.Zero, Vector3.One, "test");
    WebPmxMeshUpdate first = mesh.CreateUpdate();
    Check(first.Vertices.Length == 2 * 32, "The vertex update has an incorrect binary stride.");
    float[] firstValues = MemoryMarshal.Cast<byte, float>(first.Vertices).ToArray();
    Check(firstValues.SequenceEqual(new float[] { 1, 2, 3, 0, 0, 1, .1f, .2f, 4, 5, 6, 0, 1, 0, .3f, .4f }), "Initial mesh data was interleaved incorrectly.");

    float[] posed = [10, 20, 30, 0, 0, 1, .2f, .25f, 40, 50, 60, 0, 1, 0, .4f, .6f];
    mesh.UpdateFromInterleaved(posed);
    WebPmxMeshUpdate updated = mesh.CreateUpdate();
    float[] updatedValues = MemoryMarshal.Cast<byte, float>(updated.Vertices).ToArray();
    Check(updatedValues[7] == .75f && updatedValues[15] == 1.0f - .6f, "Posed UVs were not flipped exactly once.");
    Check(mesh.Uvs[1] == updatedValues[7] && mesh.Positions[0] == updatedValues[0], "Binary and mesh-array values disagree.");
    Check(MemoryMarshal.Cast<byte, float>(first.Vertices).SequenceEqual(firstValues), "An update mutated bytes already queued for upload.");
    Check(!ReferenceEquals(first.Vertices, updated.Vertices), "Successive frame updates share a mutable byte buffer.");
    Console.WriteLine("PASS binary PMX vertex updates and UV orientation");
}

static async Task VerifyFrameInputAndStartupAsync(bool includeModels = false)
{
    Dictionary<string, byte[]> assets = new()
    {
        ["game.project.json"] = Encoding.UTF8.GetBytes("""
            {"defaultScene":"scenes/main.scene.json","scenes":["scenes/main.scene.json"],"window":{"width":1280,"height":720}}
            """),
        ["scenes/main.scene.json"] = Encoding.UTF8.GetBytes("""
            {"name":"Input test","camera":{"position":{"x":0,"y":2,"z":8},"target":{"x":0,"y":1,"z":0},"controlMode":"editor","mouseSensitivity":0.15},"entities":[]}
            """)
    };
    if (includeModels)
    {
        // Four vertices and one root bone; keep the fixture self-contained.
        byte[] pmx = Convert.FromBase64String("UE1YIAAAAEAIAQAEBAQEBAQPAAAAV2ViIE1pbmltYWwgUE1YDwAAAFdlYiBNaW5pbWFsIFBNWA8AAABTdGFnZSAzIGZpeHR1cmUPAAAAU3RhZ2UgMyBmaXh0dXJlBAAAAAAAgL8AAIC/AAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAAIA/AACAPwAAgL8AAAAAAAAAAAAAAAAAAIA/AACAPwAAAAAAAAAAAAAAgD8AAIA/AACAPwAAAAAAAAAAAAAAAAAAgD8AAIA/AACAPwAAAAAAAACAPwAAgL8AAIA/AAAAAAAAAAAAAAAAAACAPwAAAAAAAIA/AAAAAAAAAIA/BgAAAAAAAAABAAAAAgAAAAAAAAACAAAAAwAAAAAAAAABAAAADAAAAFdlYiBNYXRlcmlhbAwAAABXZWIgTWF0ZXJpYWwAAIA/AACAPwAAgD8AAIA/zcxMPs3MTD7NzEw+AAAAQc3MTD7NzEw+zcxMPgAAAAAAAAAAAAAAAAAAAIA/AACAP///////////AAEAAAAAAAYAAAABAAAABAAAAHJvb3QEAAAAcm9vdAAAAAAAAAAAAAAAAP////8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==");
        assets["assets/first.pmx"] = pmx;
        assets["assets/second.pmx"] = pmx;
        assets["scenes/main.scene.json"] = Encoding.UTF8.GetBytes("""
            {"name":"Input test","camera":{"position":{"x":0,"y":2,"z":8},"target":{"x":0,"y":1,"z":0},"controlMode":"editor","mouseSensitivity":0.15},"entities":[{"id":"first","name":"First","type":"pmx_model","assetPath":"assets/first.pmx","enablePhysics":false},{"id":"second","name":"Second","type":"pmx_model","assetPath":"assets/second.pmx","enablePhysics":false}]}
            """);
    }
    WebAssetManifest manifest = new()
    {
        Assets = assets.Select(asset => new WebAssetEntry
        {
            Path = asset.Key, Kind = "json", Size = asset.Value.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(asset.Value)).ToLowerInvariant()
        }).ToList()
    };
    WebMemoryAssetStore store = new(manifest);
    foreach ((string assetPath, byte[] bytes) in assets) store.Add(assetPath, bytes);
    FakeBrowser browser = new();
    await using WebRuntimeHost host = new(browser, new WebProjectLoader(store), new WebAudioService(browser), new WebSpeechService(browser));
    await host.StartAsync(default(ElementReference), default(ElementReference));
    Check(host.Snapshot.IsRunning && host.Snapshot.RendererReady, $"Runtime failed to start: {host.Snapshot.LastError}");
    Check(browser.StartupFrameAttempted && host.Snapshot.FrameCount == 0, "Scene updates ran before mesh/overlay initialization completed.");
    Console.WriteLine("PASS startup does not advance a partially loaded scene");
    if (includeModels)
    {
        Check(host.Snapshot.PmxEntityCount == 2, "The scene did not load both model assets.");
        Check(browser.MeshUploadArgumentCount == 1, "The model array was expanded into separate JavaScript arguments.");
        Check(browser.UploadedMeshes is { Length: 2 }, "JavaScript did not receive both models in its meshes argument.");
        Check(browser.UploadedMeshes!.Select(mesh => mesh.Id).SequenceEqual(new[] { "first", "second" }), "Model IDs were lost at the JavaScript boundary.");
        Check(browser.LastFrame!.Meshes.Count == 2, "The render frame did not include both models.");
        Console.WriteLine("PASS all PMX models are uploaded in a single JavaScript argument");
    }

    await host.OnAnimationFrameWithInput(1000, null);
    float[] original = browser.LastFrame!.ViewProjection.ToArray();
    await host.OnAnimationFrameWithInput(1016, new WebPointerFrameInput(40, 10, 0, 0, 0, 50, 20, 1));
    float[] moved = browser.LastFrame!.ViewProjection.ToArray();
    Check(!original.SequenceEqual(moved), "Frame-batched mouse input did not reach the camera.");
    await host.OnAnimationFrameWithInput(1032, null);
    Check(moved.Zip(browser.LastFrame!.ViewProjection).All(pair => MathF.Abs(pair.First - pair.Second) < 1e-5f), "A gesture was consumed more than once.");
    Check(host.Snapshot.FrameCount == 3, "The frame counter did not advance.");
    Console.WriteLine("PASS frame-batched camera input is applied once");

    await host.OnBrowserWarning("Unsupported optional texture");
    Check(host.Snapshot.IsRunning && host.Snapshot.RendererReady, "A texture warning disabled the renderer.");
    Check(host.Snapshot.LastError == "Unsupported optional texture", "The texture warning was lost.");
    Console.WriteLine("PASS texture warnings preserve the running scene");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class FakeBrowser : IJSRuntime, IJSObjectReference
{
    private WebRuntimeHost? _host;
    public bool StartupFrameAttempted { get; private set; }
    public WebRenderFrame? LastFrame { get; private set; }
    public int MeshUploadArgumentCount { get; private set; }
    public WebPmxMeshInstance[]? UploadedMeshes { get; private set; }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (identifier == "start")
        {
            _host = ((DotNetObjectReference<WebRuntimeHost>)args![2]!).Value;
            await _host.OnCanvasResized(1280, 720, 1);
        }
        if (identifier == "setOverlayState" && !StartupFrameAttempted)
        {
            StartupFrameAttempted = true;
            await _host!.OnAnimationFrameWithInput(0, new WebPointerFrameInput(100, 100, 0, 0, 0, 0, 0, 1));
        }
        if (identifier == "setFrameState") LastFrame = (WebRenderFrame)args![0]!;
        if (identifier == "loadPmxMeshes")
        {
            MeshUploadArgumentCount = args!.Length;
            UploadedMeshes = args.Length == 1 ? args[0] as WebPmxMeshInstance[] : null;
        }
        object? result = identifier switch
        {
            "import" => this,
            "enableWebGl2" => new WebGl2Info { Renderer = "Regression renderer" },
            "detectWebGpu" => new WebGpuCapability(false, "", "", "", 0, 0, "Test"),
            _ => null
        };
        return result is TValue value ? value : default!;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

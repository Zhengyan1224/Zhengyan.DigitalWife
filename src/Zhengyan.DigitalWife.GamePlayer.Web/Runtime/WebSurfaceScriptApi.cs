using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed class WebSurfaceScriptApi(WebPmxMesh mesh,Func<string,string?> source):IRuntimePmxScriptApi
{
    private readonly Dictionary<string,WebCustomUniform> _uniforms=new(StringComparer.Ordinal);
    public (string Id,string Vertex,string Fragment)? CustomShaderSource {get;private set;}
    public WebCustomShader? CustomShader=>CustomShaderSource is {} shader?new(shader.Id,new Dictionary<string,WebCustomUniform>(_uniforms)):null;
    public object? Get(string property)=>property=="MaterialNames"?mesh.Materials.Select(m=>m.Name).ToArray():null;
    public object? Invoke(string method,object?[] args)
    {
        if(method=="SetCustomShader")
        {
            string? vertex=source((string)args[0]!),fragment=source((string)args[1]!);if(vertex is null||fragment is null)return false;
            vertex=WebPmxAsset.BrowserShaderSource(vertex);fragment=WebPmxAsset.BrowserShaderSource(fragment);
            string id=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(vertex+"\n"+fragment)));
            CustomShaderSource=(id,vertex,fragment);return true;
        }
        if(method=="ClearCustomShader"){CustomShaderSource=null;return null;}
        if(method=="ClearCustomShaderUniform"){_uniforms.Remove((string)args[0]!);return null;}
        if(method=="ClearCustomShaderUniforms"){_uniforms.Clear();return null;}
        if(method.StartsWith("SetCustomShader",StringComparison.Ordinal)){_uniforms[(string)args[0]!]=new(method=="SetCustomShaderInt",args.Skip(1).Select(Convert.ToSingle).ToArray());return null;}
        return null;
    }
}

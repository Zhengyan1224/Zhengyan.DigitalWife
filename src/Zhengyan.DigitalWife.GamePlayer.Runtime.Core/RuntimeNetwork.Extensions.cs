using System.Net.Http.Headers;
using System.Text;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public partial class RuntimeNetwork
{
    // Preserve the original portable overload while the six-argument form
    // supplies the desktop script contract.
    public Task<RuntimeHttpResponse> HttpSendAsync(string method,string url,CancellationToken cancellationToken,string? body=null,string? contentType=null,int timeoutSeconds=15,IReadOnlyDictionary<string,string>? headers=null)
        =>HttpSendAsync(method,url,body,contentType,timeoutSeconds,headers,cancellationToken);
    public async Task<RuntimeHttpResponse> HttpSendAsync(string method,string url,string? body,string? contentType,int timeoutSeconds,
        IReadOnlyDictionary<string,string>? headers,CancellationToken cancellationToken)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1,timeoutSeconds)));
        using HttpRequestMessage request=new(new(method),url);
        if(body is not null){request.Content=new StringContent(body,Encoding.UTF8);request.Content.Headers.ContentType=MediaTypeHeaderValue.Parse(contentType??"text/plain; charset=utf-8");}
        if(headers is not null)foreach(var header in headers)request.Headers.TryAddWithoutValidation(header.Key,header.Value);
        using var response=await Client.SendAsync(request,timeout.Token);
        return new((int)response.StatusCode,response.IsSuccessStatusCode,response.ReasonPhrase??"",await response.Content.ReadAsStringAsync(timeout.Token),response.Headers.Concat(response.Content.Headers).ToDictionary(h=>h.Key,h=>h.Value.ToArray(),StringComparer.OrdinalIgnoreCase));
    }
}

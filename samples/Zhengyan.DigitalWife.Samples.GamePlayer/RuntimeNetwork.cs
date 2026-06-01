using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed class RuntimeHttpResponse
{
    public required int StatusCode { get; init; }

    public required bool IsSuccessStatusCode { get; init; }

    public required string ReasonPhrase { get; init; }

    public required string Body { get; init; }

    public required IReadOnlyDictionary<string, string[]> Headers { get; init; }

    public string GetHeader(string name)
    {
        return Headers.TryGetValue(name, out string[]? values)
            ? string.Join(", ", values)
            : string.Empty;
    }
}

public sealed class RuntimeTcpMessage
{
    public required string Text { get; init; }

    public required byte[] Data { get; init; }

    public required string RemoteHost { get; init; }

    public required int RemotePort { get; init; }
}

public sealed class RuntimeUdpMessage
{
    public required string Text { get; init; }

    public required byte[] Data { get; init; }

    public required string RemoteHost { get; init; }

    public required int RemotePort { get; init; }
}

public sealed class RuntimeNetwork
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public Task<RuntimeHttpResponse> HttpGetAsync(
        string url,
        int timeoutSeconds = 15,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        return HttpSendAsync("GET", url, body: null, contentType: null, timeoutSeconds, headers);
    }

    public Task<RuntimeHttpResponse> HttpPostTextAsync(
        string url,
        string text,
        string contentType = "text/plain; charset=utf-8",
        int timeoutSeconds = 15,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        return HttpSendAsync("POST", url, text, contentType, timeoutSeconds, headers);
    }

    public Task<RuntimeHttpResponse> HttpPostJsonAsync<T>(
        string url,
        T value,
        int timeoutSeconds = 15,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        return HttpSendAsync(
            "POST",
            url,
            JsonSerializer.Serialize(value),
            "application/json; charset=utf-8",
            timeoutSeconds,
            headers);
    }

    public async Task<RuntimeHttpResponse> HttpSendAsync(
        string method,
        string url,
        string? body = null,
        string? contentType = "text/plain; charset=utf-8",
        int timeoutSeconds = 15,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        Uri uri = CreateHttpUri(url);
        using CancellationTokenSource cts = CreateTimeout(timeoutSeconds);
        using HttpRequestMessage request = new(new HttpMethod(string.IsNullOrWhiteSpace(method) ? "GET" : method.Trim().ToUpperInvariant()), uri);

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8);
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                request.Content.Headers.Remove("Content-Type");
                request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }
        }

        ApplyHeaders(request, headers);
        using HttpResponseMessage response = await HttpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
        string responseBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        return new RuntimeHttpResponse
        {
            StatusCode = (int)response.StatusCode,
            IsSuccessStatusCode = response.IsSuccessStatusCode,
            ReasonPhrase = response.ReasonPhrase ?? string.Empty,
            Body = responseBody,
            Headers = response.Headers
                .Concat(response.Content.Headers)
                .ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase)
        };
    }

    public async Task<string> TcpSendTextAsync(
        string host,
        int port,
        string text,
        int timeoutSeconds = 5,
        string encodingName = "utf-8",
        int receiveBytes = 65536)
    {
        Encoding encoding = GetEncoding(encodingName);
        byte[] response = await TcpSendAsync(host, port, encoding.GetBytes(text ?? string.Empty), timeoutSeconds, receiveBytes).ConfigureAwait(false);
        return encoding.GetString(response);
    }

    public async Task<byte[]> TcpSendAsync(
        string host,
        int port,
        byte[] data,
        int timeoutSeconds = 5,
        int receiveBytes = 65536)
    {
        ValidateEndpoint(host, port);
        using CancellationTokenSource cts = CreateTimeout(timeoutSeconds);
        using TcpClient client = new();
        await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
        await using NetworkStream stream = client.GetStream();
        await stream.WriteAsync(data, cts.Token).ConfigureAwait(false);

        if (receiveBytes <= 0)
        {
            return [];
        }

        byte[] buffer = new byte[Math.Max(1, receiveBytes)];
        int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token).ConfigureAwait(false);
        return buffer[..read];
    }

    public async Task<RuntimeTcpMessage> TcpReceiveTextOnceAsync(
        int port,
        int timeoutSeconds = 10,
        string encodingName = "utf-8",
        int receiveBytes = 65536,
        string listenAddress = "0.0.0.0")
    {
        Encoding encoding = GetEncoding(encodingName);
        RuntimeTcpMessage message = await TcpReceiveOnceAsync(port, timeoutSeconds, receiveBytes, listenAddress).ConfigureAwait(false);
        return new RuntimeTcpMessage
        {
            Text = encoding.GetString(message.Data),
            Data = message.Data,
            RemoteHost = message.RemoteHost,
            RemotePort = message.RemotePort
        };
    }

    public async Task<RuntimeTcpMessage> TcpReceiveOnceAsync(
        int port,
        int timeoutSeconds = 10,
        int receiveBytes = 65536,
        string listenAddress = "0.0.0.0")
    {
        ValidatePort(port);
        IPAddress address = ParseListenAddress(listenAddress);
        using CancellationTokenSource cts = CreateTimeout(timeoutSeconds);
        TcpListener listener = new(address, port);
        listener.Start();
        try
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(cts.Token).ConfigureAwait(false);
            EndPoint? remote = client.Client.RemoteEndPoint;
            await using NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[Math.Max(1, receiveBytes)];
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token).ConfigureAwait(false);
            (string remoteHost, int remotePort) = SplitEndpoint(remote);
            return new RuntimeTcpMessage
            {
                Text = Encoding.UTF8.GetString(buffer, 0, read),
                Data = buffer[..read],
                RemoteHost = remoteHost,
                RemotePort = remotePort
            };
        }
        finally
        {
            listener.Stop();
        }
    }

    public async Task<string> UdpSendTextAsync(
        string host,
        int port,
        string text,
        int timeoutSeconds = 5,
        string encodingName = "utf-8",
        int receiveBytes = 65536,
        bool waitForResponse = true)
    {
        Encoding encoding = GetEncoding(encodingName);
        byte[] response = await UdpSendAsync(host, port, encoding.GetBytes(text ?? string.Empty), timeoutSeconds, receiveBytes, waitForResponse).ConfigureAwait(false);
        return encoding.GetString(response);
    }

    public async Task<byte[]> UdpSendAsync(
        string host,
        int port,
        byte[] data,
        int timeoutSeconds = 5,
        int receiveBytes = 65536,
        bool waitForResponse = true)
    {
        ValidateEndpoint(host, port);
        using CancellationTokenSource cts = CreateTimeout(timeoutSeconds);
        using UdpClient client = new();
        await client.SendAsync(data, data.Length, host, port).WaitAsync(cts.Token).ConfigureAwait(false);
        if (!waitForResponse || receiveBytes <= 0)
        {
            return [];
        }

        UdpReceiveResult result = await client.ReceiveAsync(cts.Token).ConfigureAwait(false);
        return result.Buffer.Length <= receiveBytes
            ? result.Buffer
            : result.Buffer[..receiveBytes];
    }

    public async Task<RuntimeUdpMessage> UdpReceiveTextAsync(
        int port,
        int timeoutSeconds = 10,
        string encodingName = "utf-8",
        int receiveBytes = 65536,
        string listenAddress = "0.0.0.0")
    {
        Encoding encoding = GetEncoding(encodingName);
        RuntimeUdpMessage message = await UdpReceiveAsync(port, timeoutSeconds, receiveBytes, listenAddress).ConfigureAwait(false);
        return new RuntimeUdpMessage
        {
            Text = encoding.GetString(message.Data),
            Data = message.Data,
            RemoteHost = message.RemoteHost,
            RemotePort = message.RemotePort
        };
    }

    public async Task<RuntimeUdpMessage> UdpReceiveAsync(
        int port,
        int timeoutSeconds = 10,
        int receiveBytes = 65536,
        string listenAddress = "0.0.0.0")
    {
        ValidatePort(port);
        IPAddress address = ParseListenAddress(listenAddress);
        using CancellationTokenSource cts = CreateTimeout(timeoutSeconds);
        using UdpClient client = new(new IPEndPoint(address, port));
        UdpReceiveResult result = await client.ReceiveAsync(cts.Token).ConfigureAwait(false);
        byte[] data = result.Buffer.Length <= receiveBytes
            ? result.Buffer
            : result.Buffer[..Math.Max(0, receiveBytes)];
        return new RuntimeUdpMessage
        {
            Text = Encoding.UTF8.GetString(data),
            Data = data,
            RemoteHost = result.RemoteEndPoint.Address.ToString(),
            RemotePort = result.RemoteEndPoint.Port
        };
    }

    private static Uri CreateHttpUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("URL must be an absolute http:// or https:// URL.", nameof(url));
        }

        return uri;
    }

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return;
        }

        foreach ((string key, string value) in headers)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(key, value) && request.Content is not null)
            {
                request.Content.Headers.TryAddWithoutValidation(key, value);
            }
        }
    }

    private static CancellationTokenSource CreateTimeout(int timeoutSeconds)
    {
        return new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
    }

    private static Encoding GetEncoding(string encodingName)
    {
        return string.IsNullOrWhiteSpace(encodingName)
            ? Encoding.UTF8
            : Encoding.GetEncoding(encodingName);
    }

    private static void ValidateEndpoint(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host is required.", nameof(host));
        }

        ValidatePort(port);
    }

    private static void ValidatePort(int port)
    {
        if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 0 and 65535.");
        }
    }

    private static IPAddress ParseListenAddress(string listenAddress)
    {
        if (string.IsNullOrWhiteSpace(listenAddress)
            || listenAddress == "0.0.0.0"
            || listenAddress == "*")
        {
            return IPAddress.Any;
        }

        if (listenAddress == "::")
        {
            return IPAddress.IPv6Any;
        }

        return IPAddress.Parse(listenAddress);
    }

    private static (string Host, int Port) SplitEndpoint(EndPoint? endpoint)
    {
        return endpoint is IPEndPoint ip
            ? (ip.Address.ToString(), ip.Port)
            : (string.Empty, 0);
    }
}

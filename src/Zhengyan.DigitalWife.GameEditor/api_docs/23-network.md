---
id: network
title: Network 网络通信 API
category: 网络
objects:
  - RuntimeNetwork
  - RuntimeHttpResponse
  - RuntimeTcpMessage
  - RuntimeUdpMessage
keywords:
  - network
  - http
  - tcp
  - udp
---

# Network 网络通信 API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | Network 网络通信 API |
| 分类 | 网络 |
| 主要对象 | ``RuntimeNetwork``, ``RuntimeHttpResponse``, ``RuntimeTcpMessage``, ``RuntimeUdpMessage`` |
| C# 入口 | `Scene.Network.HttpGetAsync/TcpReceiveTextOnceAsync/UdpReceiveTextAsync` |
| Python 入口 | `scene.network.http_get/tcp_receive_text_once/udp_receive_text` |
| 说明 | HTTP/HTTPS、TCP、UDP 网络通信 API、返回值和超时注意事项。 |

## API 内容

`Scene.Network` / `scene.network` 提供 HTTP/HTTPS、TCP 和 UDP 通信能力。实现基于 .NET / Python 标准网络库，目标平台为 Windows、Linux 和 macOS。

注意：网络调用可能等待超时。不要在每帧 `update` 里做长时间阻塞请求；实时逻辑应设置较短的 `timeout`，长请求优先放在点击事件、加载脚本或后台逻辑里。脚本网络访问没有额外沙盒限制，访问外网或监听端口时需要遵守系统防火墙、网络权限和目标服务协议。

### C# Network

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    RuntimeHttpResponse response = await Scene.Network.HttpGetAsync(
        "https://example.com",
        timeoutSeconds: 10);

    Console.WriteLine(response.StatusCode);
    Console.WriteLine(response.Body);
}

var payload = new { message = "hello" };
RuntimeHttpResponse post = await Scene.Network.HttpPostJsonAsync(
    "https://example.com/api/messages",
    payload,
    timeoutSeconds: 10);

string tcpReply = await Scene.Network.TcpSendTextAsync(
    "127.0.0.1",
    9000,
    "ping\n",
    timeoutSeconds: 3);

RuntimeTcpMessage tcpMessage = await Scene.Network.TcpReceiveTextOnceAsync(
    9001,
    timeoutSeconds: 10);

string udpReply = await Scene.Network.UdpSendTextAsync(
    "127.0.0.1",
    9002,
    "ping",
    timeoutSeconds: 3);

RuntimeUdpMessage udpMessage = await Scene.Network.UdpReceiveTextAsync(
    9003,
    timeoutSeconds: 10);

await Scene.Network.UdpSendAsync(
    "127.0.0.1",
    9004,
    Encoding.UTF8.GetBytes("fire-and-forget"),
    waitForResponse: false);
```

C# API：

| 方法 | 说明 |
| --- | --- |
| `HttpGetAsync(url, timeoutSeconds, headers)` | 发送 HTTP GET。`url` 必须是绝对 `http://` 或 `https://` 地址。 |
| `HttpPostTextAsync(url, text, contentType, timeoutSeconds, headers)` | 发送文本 POST。 |
| `HttpPostJsonAsync(url, value, timeoutSeconds, headers)` | 序列化对象为 JSON 并发送 POST。 |
| `HttpSendAsync(method, url, body, contentType, timeoutSeconds, headers)` | 自定义 HTTP 方法、请求体和 Content-Type。 |
| `TcpSendTextAsync(host, port, text, timeoutSeconds, encodingName, receiveBytes)` | TCP 连接、发送文本，并读取一次响应。 |
| `TcpSendAsync(host, port, data, timeoutSeconds, receiveBytes)` | TCP 连接、发送字节，并读取一次响应；`receiveBytes <= 0` 时不等待响应。 |
| `TcpReceiveTextOnceAsync(port, timeoutSeconds, encodingName, receiveBytes, listenAddress)` | 启动一次性 TCP 监听，接受一个连接并返回文本。 |
| `TcpReceiveOnceAsync(port, timeoutSeconds, receiveBytes, listenAddress)` | 启动一次性 TCP 监听，接受一个连接并返回字节。 |
| `UdpSendTextAsync(host, port, text, timeoutSeconds, encodingName, receiveBytes, waitForResponse)` | UDP 发送文本；`waitForResponse = false` 时不等待回复。 |
| `UdpSendAsync(host, port, data, timeoutSeconds, receiveBytes, waitForResponse)` | UDP 发送字节；可选择等待一个回复包。 |
| `UdpReceiveTextAsync(port, timeoutSeconds, encodingName, receiveBytes, listenAddress)` | 监听一个 UDP 数据包并返回文本。 |
| `UdpReceiveAsync(port, timeoutSeconds, receiveBytes, listenAddress)` | 监听一个 UDP 数据包并返回字节。 |

返回类型：

| 类型 | 字段 |
| --- | --- |
| `RuntimeHttpResponse` | `StatusCode`、`IsSuccessStatusCode`、`ReasonPhrase`、`Body`、`Headers`、`GetHeader(name)`。 |
| `RuntimeTcpMessage` | `Text`、`Data`、`RemoteHost`、`RemotePort`。 |
| `RuntimeUdpMessage` | `Text`、`Data`、`RemoteHost`、`RemotePort`。 |

### Python Network

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "clicked":
        return

    response = scene.network.http_get("https://example.com", timeout=10)
    print(response["status_code"])
    print(response["body"])

    post = scene.network.http_post_json(
        "https://example.com/api/messages",
        {"message": "hello"},
        timeout=10)

    tcp_reply = scene.network.tcp_send_text(
        "127.0.0.1",
        9000,
        "ping\n",
        timeout=3)

    tcp_message = scene.network.tcp_receive_text_once(9001, timeout=10)

    udp_reply = scene.network.udp_send_text(
        "127.0.0.1",
        9002,
        "ping",
        timeout=3)

    udp_message = scene.network.udp_receive_text(9003, timeout=10)

    scene.network.udp_send(
        "127.0.0.1",
        9004,
        b"fire-and-forget",
        wait_for_response=False)
```

Python API：

| 方法 | 说明 |
| --- | --- |
| `http_get(url, timeout=15, headers=None)` | 发送 HTTP GET。 |
| `http_post_text(url, text, content_type="text/plain; charset=utf-8", timeout=15, headers=None)` | 发送文本 POST。 |
| `http_post_json(url, value, timeout=15, headers=None)` | 序列化对象为 JSON 并发送 POST。 |
| `http_send(method, url, body=None, content_type=None, timeout=15, headers=None)` | 自定义 HTTP 方法、请求体和 Content-Type。 |
| `tcp_send_text(host, port, text, timeout=5, encoding="utf-8", receive_bytes=65536)` | TCP 发送文本并读取一次响应。 |
| `tcp_send(host, port, data, timeout=5, receive_bytes=65536)` | TCP 发送字节并读取一次响应；`receive_bytes <= 0` 时不等待响应。 |
| `tcp_receive_text_once(port, timeout=10, encoding="utf-8", receive_bytes=65536, listen_address="0.0.0.0")` | 一次性 TCP 监听，返回文本消息。 |
| `tcp_receive_once(port, timeout=10, receive_bytes=65536, listen_address="0.0.0.0")` | 一次性 TCP 监听，返回字节消息。 |
| `udp_send_text(host, port, text, timeout=5, encoding="utf-8", receive_bytes=65536, wait_for_response=True)` | UDP 发送文本；可选择等待回复。 |
| `udp_send(host, port, data, timeout=5, receive_bytes=65536, wait_for_response=True)` | UDP 发送字节；可选择等待回复。 |
| `udp_receive_text(port, timeout=10, encoding="utf-8", receive_bytes=65536, listen_address="0.0.0.0")` | 监听一个 UDP 数据包并返回文本。 |
| `udp_receive(port, timeout=10, receive_bytes=65536, listen_address="0.0.0.0")` | 监听一个 UDP 数据包并返回字节。 |

Python 返回值：

| 方法类别 | 返回值 |
| --- | --- |
| HTTP | `dict`：`status_code`、`is_success_status_code`、`reason_phrase`、`body`、`headers`。 |
| TCP / UDP 接收 | `dict`：`text`、`data`、`remote_host`、`remote_port`。 |
| TCP / UDP 发送 | 文本方法返回 `str`，字节方法返回 `bytes`；不等待响应时返回空字节。 |

边界说明：

- HTTP 只支持绝对 `http://` 和 `https://` 地址；当前没有 WebSocket 封装。
- TCP 接收 API 是一次性监听：收到一个连接并读取一次后关闭监听。
- UDP 是无连接数据包协议，可能丢包、乱序或没有响应；只发不等回复时使用 `waitForResponse: false` / `wait_for_response=False`。
- 监听端口可能被系统防火墙、杀毒软件或已有进程占用。
- C# 网络 API 是 `async`；Python 网络 API 是同步阻塞调用。

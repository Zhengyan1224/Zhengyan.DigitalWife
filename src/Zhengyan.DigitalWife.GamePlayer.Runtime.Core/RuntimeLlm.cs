using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed record RuntimeLlmChatMessage(string Role, string Content)
{
    public string? ToolCallId { get; init; }
    public IReadOnlyList<RuntimeLlmToolCall> ToolCalls { get; init; } = [];
}
public sealed record RuntimeLlmToolCall(string Id, string Name, string ArgumentsJson);
public sealed record RuntimeLlmStreamUpdate(string Delta, string AccumulatedText, bool IsFinal, IReadOnlyList<RuntimeLlmToolCall> ToolCalls)
{
    public RuntimeLlmStreamUpdate(string delta, string accumulatedText, bool isFinal) : this(delta, accumulatedText, isFinal, []) { }
}
public sealed record RuntimeLlmScriptEvent(string RequestId, string EventName, string Delta, string AccumulatedText, bool IsFinal,
    string Error, string CallbackName, RuntimeLlmToolCall? ToolCall = null, string? ToolResult = null);
public sealed record RuntimeLlmTool(string Name, string Description, string ParametersJsonSchema, Func<RuntimeLlmToolCall, CancellationToken, Task<string>> Handler)
{
    public RuntimeLlmTool(string name, string description, string parametersJsonSchema, Func<string, string> handler)
        : this(name, description, parametersJsonSchema, (call, _) => Task.FromResult(handler(call.ArgumentsJson))) { }
    public RuntimeLlmTool(string name, string description, string parametersJsonSchema, Func<string, CancellationToken, Task<string>> handler)
        : this(name, description, parametersJsonSchema, (call, cancellation) => handler(call.ArgumentsJson, cancellation)) { }
    public Task<string> InvokeAsync(RuntimeLlmToolCall toolCall, CancellationToken cancellationToken) => Handler(toolCall, cancellationToken);
}

/// <summary>Portable HTTP/SSE chat, tools, cancellation and script events.</summary>
public sealed partial class RuntimeLlm : IDisposable
{
    private sealed class Request(CancellationTokenSource cancellation) { public CancellationTokenSource Cancellation = cancellation; public Task Task = Task.CompletedTask; public bool UserCancelled; }
    private sealed class ToolBuilder { public string Id = "", Name = ""; public StringBuilder Arguments = new(); }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RuntimeSaveStore _save;
    private readonly RuntimeNetwork _network;
    private readonly ConcurrentDictionary<string, Request> _requests = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _projectText = new(StringComparer.OrdinalIgnoreCase);
    private GameProjectLlmSettings _settings = new();
    private bool _disposed;
    public RuntimeLlm(RuntimeSaveStore save, RuntimeNetwork network) { _save = save; _network = network; }
    public event Action<RuntimeEntity, RuntimeLlmScriptEvent>? ScriptEvent;
    public bool Enabled => _settings.Enabled;
    public string BaseUrl => _settings.BaseUrl;
    public string Model => _settings.Model;
    public bool SkillsEnabled => _settings.EnableSkills;
    public bool MemoryEnabled => _settings.EnableMemory;
    public string SkillsDirectory => "skills";
    public string MemoryDirectory => "memory";
    public void Configure(GameProjectLlmSettings settings) => _settings = settings;
    public void RegisterProjectText(string path, string text) => _projectText[RuntimeSaveStore.Normalize(path)] = text;
    public string GetCharacterMemoryPath(RuntimeEntity entity) => GetCharacterMemoryPath(entity.Name);
    public string GetCharacterMemoryPath(string characterName) => "memory/characters/" + string.Concat(characterName.Select(c => char.IsLetterOrDigit(c) ? c : '_')) + ".md";
    public void CancelRequest(string requestId) { if (_requests.TryRemove(requestId, out var request)) { request.UserCancelled = true; request.Cancellation.Cancel(); } }
    public void CancelAllRequests() { foreach (string id in _requests.Keys) CancelRequest(id); }
    public Task WaitForRequestsAsync() => Task.WhenAll(_requests.Values.Select(r => r.Task));
    public string StartChat(RuntimeEntity callbackTarget, IReadOnlyList<RuntimeLlmChatMessage> messages, string? requestId,
        string? onDeltaCallback, string? onCompletedCallback, string? onErrorCallback)
        => StartChatWithTools(callbackTarget, messages, [], requestId, onDeltaCallback, onCompletedCallback, onErrorCallback,"llm_tool_call","llm_tool_result",8);
    public string StartChat(RuntimeEntity callbackTarget, string userText, string? systemPrompt, string? requestId,
        string? onDeltaCallback, string? onCompletedCallback, string? onErrorCallback)
        => StartChat(callbackTarget, Messages(userText, systemPrompt), requestId, onDeltaCallback, onCompletedCallback, onErrorCallback);
    public string StartChatWithTools(RuntimeEntity callbackTarget, IReadOnlyList<RuntimeLlmChatMessage> messages, IReadOnlyList<RuntimeLlmTool> tools,
        string? requestId, string? onDeltaCallback, string? onCompletedCallback, string? onErrorCallback,
        string? onToolCallCallback, string? onToolResultCallback, int maxToolRounds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string id = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
        CancelRequest(id);
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds)));
        Request request = new(cancellation); _requests[id] = request;
        var scriptOptions=_scriptOptions.Value;
        request.Task = RunAsync();
        return id;

        void Emit(string eventName, string callback, string text = "", string delta = "", RuntimeLlmToolCall? tool = null, string? result = null, string error = "")
        {
            if (!_disposed && !request.UserCancelled && (!cancellation.IsCancellationRequested || eventName == "error") && _requests.TryGetValue(id, out var current) && ReferenceEquals(current, request))
            {
                var previous=_scriptOptions.Value;_scriptOptions.Value=null;
                try{ScriptEvent?.Invoke(callbackTarget, new(id, eventName, delta, text, eventName == "completed", error, callback, tool, result));if(eventName=="delta")scriptOptions?.Delta?.Invoke(new(delta,text,false));else if(eventName=="completed")scriptOptions?.Completed?.Invoke(new(id,text));else if(eventName=="error")scriptOptions?.Error?.Invoke(new InvalidOperationException(error));}
                finally{_scriptOptions.Value=previous;}
            }
        }
        async Task RunAsync()
        {
            try
            {
                string text = "";
                await foreach (var update in StreamChatWithToolsAsync(messages, tools, maxToolRounds,
                    tool => Emit("tool_call", onToolCallCallback ?? "", text, tool: tool),
                    (tool, result) => Emit("tool_result", onToolResultCallback ?? "", text, tool: tool, result: result), cancellation.Token))
                {
                    text = update.AccumulatedText;
                    if (update.Delta.Length > 0) Emit("delta", onDeltaCallback ?? "", text, update.Delta);
                }
                Emit("completed", onCompletedCallback ?? "", text);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { if (!request.UserCancelled) Emit("error", onErrorCallback ?? "", error: "LLM request timed out."); }
            catch (Exception exception) { Emit("error", onErrorCallback ?? "", error: exception.GetBaseException().Message); }
            finally { if (_requests.TryGetValue(id, out var current) && ReferenceEquals(current, request)) _requests.TryRemove(id, out _); cancellation.Dispose(); }
        }
    }
    public async Task<string> ChatAsync(IReadOnlyList<RuntimeLlmChatMessage> messages, CancellationToken cancellationToken = default)
    {
        string result = ""; await foreach (var update in StreamChatAsync(messages, cancellationToken)) result = update.AccumulatedText; return result;
    }
    public Task<string> ChatAsync(string userText, string? systemPrompt = null, CancellationToken cancellationToken = default)
        => ChatAsync(Messages(userText, systemPrompt), cancellationToken);
    public IAsyncEnumerable<RuntimeLlmStreamUpdate> StreamChatAsync(IReadOnlyList<RuntimeLlmChatMessage> messages, CancellationToken cancellationToken = default)
        => StreamChatWithToolsAsync(messages, [], cancellationToken: cancellationToken);
    public async IAsyncEnumerable<RuntimeLlmStreamUpdate> StreamChatWithToolsAsync(IReadOnlyList<RuntimeLlmChatMessage> messages,
        IReadOnlyList<RuntimeLlmTool> tools, int maxToolRounds = 8, Action<RuntimeLlmToolCall>? onToolCall = null,
        Action<RuntimeLlmToolCall, string>? onToolResult = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Enabled) throw new InvalidOperationException("LLM is disabled in the game project.");
        List<RuntimeLlmChatMessage> conversation = [.. messages];
        var available = tools.Concat(BuiltInTools()).GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray();
        string accumulated = "";
        for (int round = 0; round <= Math.Clamp(maxToolRounds, 1, 1000); round++)
        {
            IReadOnlyList<RuntimeLlmToolCall> calls = [];
            string roundText = "";
            await foreach (var update in StreamRoundAsync(conversation, available, cancellationToken))
            {
                roundText = update.AccumulatedText;
                if (update.Delta.Length > 0) { accumulated += update.Delta; yield return new(update.Delta, accumulated, false); }
                if (update.ToolCalls.Count > 0) calls = update.ToolCalls;
            }
            if (calls.Count == 0) { yield return new("", accumulated, true); yield break; }
            if (round == Math.Clamp(maxToolRounds, 1, 1000)) throw new InvalidOperationException("LLM exceeded the configured tool round limit.");
            conversation.Add(new("assistant", roundText) { ToolCalls = calls });
            foreach (var call in calls)
            {
                cancellationToken.ThrowIfCancellationRequested(); onToolCall?.Invoke(call);
                string result;
                var tool = available.FirstOrDefault(t => t.Name.Equals(call.Name, StringComparison.OrdinalIgnoreCase));
                try { result = tool is null ? JsonSerializer.Serialize(new { error = "Unknown tool: " + call.Name }) : await tool.InvokeAsync(call, cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { result = JsonSerializer.Serialize(new { error = exception.Message }); }
                onToolResult?.Invoke(call, result); conversation.Add(new("tool", result) { ToolCallId = call.Id });
            }
        }
    }
    private async IAsyncEnumerable<RuntimeLlmStreamUpdate> StreamRoundAsync(IReadOnlyList<RuntimeLlmChatMessage> messages, IReadOnlyList<RuntimeLlmTool> tools,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?> { ["model"] = _scriptOptions.Value?.Model??Model, ["stream"] = true,
            ["messages"] = messages.Select(m =>
            {
                var value = new Dictionary<string, object?> { ["role"] = m.Role, ["content"] = m.Content };
                if (m.ToolCallId is not null) value["tool_call_id"] = m.ToolCallId;
                if (m.ToolCalls.Count > 0) value["tool_calls"] = m.ToolCalls.Select(c => new { id = c.Id, type = "function", function = new { name = c.Name, arguments = c.ArgumentsJson } }).ToArray();
                return value;
            }).ToArray() };
        if ((_scriptOptions.Value?.Temperature??_settings.DefaultTemperature) is float temperature) body["temperature"] = temperature;
        if (tools.Count > 0) body["tools"] = tools.Select(t => new { type = "function", function = new { name = t.Name, description = t.Description, parameters = JsonSerializer.Deserialize<JsonElement>(t.ParametersJsonSchema) } }).ToArray();
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(new Uri(BaseUrl.TrimEnd('/') + "/"), _settings.ChatCompletionsPath.TrimStart('/')))
        { Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json") };
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        _network.ConfigureStreamingRequest?.Invoke(request);
        using var response = await _network.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"LLM HTTP {(int)response.StatusCode}: {error[..Math.Min(500, error.Length)]}");
        }
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
        Dictionary<int, ToolBuilder> builders = [];
        string text = "";
        while (await reader.ReadLineAsync(cancellationToken) is string line)
        {
            if (line.Length == 0 || line.StartsWith(':')) continue;
            string data = line.StartsWith("data:", StringComparison.Ordinal) ? line[5..].TrimStart() : line;
            if (data == "[DONE]") break;
            if (!data.StartsWith('{')) continue;
            using var json = JsonDocument.Parse(data);
            if (!json.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
            var choice = choices[0];
            var delta = choice.TryGetProperty("delta", out var streamed) ? streamed : choice.GetProperty("message");
            if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                string part = content.GetString()!; text += part; yield return new(part, text, false);
            }
            if (delta.TryGetProperty("tool_calls", out var toolCalls)) foreach (var item in toolCalls.EnumerateArray())
            {
                int index = item.TryGetProperty("index", out var i) ? i.GetInt32() : builders.Count;
                if (!builders.TryGetValue(index, out var builder)) builders[index] = builder = new();
                if (item.TryGetProperty("id", out var id)) builder.Id += id.GetString();
                if (item.TryGetProperty("function", out var function))
                {
                    if (function.TryGetProperty("name", out var name)) builder.Name += name.GetString();
                    if (function.TryGetProperty("arguments", out var arguments)) builder.Arguments.Append(arguments.GetString());
                }
            }
        }
        yield return new("", text, true, builders.OrderBy(p => p.Key).Select(p => new RuntimeLlmToolCall(p.Value.Id, p.Value.Name, p.Value.Arguments.ToString())).ToArray());
    }
    private IEnumerable<RuntimeLlmTool> BuiltInTools()
    {
        const string schema = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"content\":{\"type\":\"string\"},\"query\":{\"type\":\"string\"}}}";
        if (MemoryEnabled) foreach (string name in new[] { "memory_read", "memory_write", "memory_update", "memory_forget", "memory_search" })
            yield return new(name, "Read, search or maintain this game's persistent memory files.", schema, arguments =>
            {
                using var json = JsonDocument.Parse(arguments); var root = json.RootElement;
                string Get(string key) => root.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
                if (name == "memory_search")
                {
                    var memory = new Dictionary<string, string>(_projectText.Where(p => p.Key.StartsWith("memory/")), StringComparer.OrdinalIgnoreCase);
                    foreach (var file in _save.Snapshot().Where(p => p.Key.StartsWith("memory/"))) memory[file.Key] = file.Value;
                    return JsonSerializer.Serialize(memory.Where(p => p.Value.Length > 0 && p.Value.Contains(Get("query"), StringComparison.OrdinalIgnoreCase)).Select(p => new { path = p.Key, content = p.Value }));
                }
                string path = RuntimeSaveStore.Normalize(Get("path"));
                if (!path.StartsWith("memory/", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Memory tools require a memory/ path.");
                if (name == "memory_read") return _save.ReadText(path, _projectText.GetValueOrDefault(path, ""));
                if (name == "memory_forget") { _save.WriteText(path, ""); return "{\"deleted\":true}"; }
                _save.WriteText(path, name == "memory_write" ? _save.ReadText(path, _projectText.GetValueOrDefault(path, "")) + Get("content") : Get("content")); return "{\"saved\":true}";
            });
        if (SkillsEnabled)
        {
            yield return new("skills_list", "List skill documents packaged with this game.", schema, _ => JsonSerializer.Serialize(_projectText.Keys.Where(p => p.StartsWith("skills/", StringComparison.OrdinalIgnoreCase) && p.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase))));
            yield return new("skill_read", "Read a packaged skill document or supporting file.", schema, arguments =>
            {
                using var json = JsonDocument.Parse(arguments);
                string path = RuntimeSaveStore.Normalize(json.RootElement.GetProperty("path").GetString()!);
                return _projectText.GetValueOrDefault(path, "Skill resource not found.");
            });
        }
    }
    private static IReadOnlyList<RuntimeLlmChatMessage> Messages(string text, string? systemPrompt)
        => string.IsNullOrWhiteSpace(systemPrompt) ? [new("user", text)] : [new("system", systemPrompt), new("user", text)];
    public void Dispose() { _disposed = true; CancelAllRequests(); ScriptEvent = null; }
}

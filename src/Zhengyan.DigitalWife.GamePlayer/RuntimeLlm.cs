using System.Text.Json;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Llm;
using Zhengyan.DigitalWife.Llm.OpenAI;

namespace Zhengyan.DigitalWife.GamePlayer;

public sealed class RuntimeLlm : IDisposable
{
    private const int DefaultMaxToolRounds = 4;

    private readonly GameProjectLlmSettings _settings;
    private readonly string _projectDirectory;
    private readonly RuntimeLlmSkillTools _skillTools;
    private readonly MainThreadDispatcher _dispatcher;
    private readonly Action<RuntimeEntity, RuntimeLlmScriptEvent> _dispatchScriptEvent;
    private readonly object _sync = new();
    private readonly List<CancellationTokenSource> _activeRequests = [];
    private OpenAiCompatibleLlmClient? _client;
    private bool _disposed;

    internal RuntimeLlm(
        GameProjectLlmSettings settings,
        string projectDirectory,
        MainThreadDispatcher dispatcher,
        Action<RuntimeEntity, RuntimeLlmScriptEvent> dispatchScriptEvent)
    {
        _settings = settings;
        _projectDirectory = Path.GetFullPath(projectDirectory);
        _skillTools = new RuntimeLlmSkillTools(_projectDirectory);
        _dispatcher = dispatcher;
        _dispatchScriptEvent = dispatchScriptEvent;
    }

    public bool Enabled => _settings.Enabled;

    public string Provider => _settings.Provider;

    public string BaseUrl => _settings.BaseUrl;

    public string Model => _settings.Model;

    public string ChatCompletionsPath => _settings.ChatCompletionsPath;

    public float? DefaultTemperature => _settings.DefaultTemperature;

    public bool SkillsEnabled => _settings.EnableSkills;

    public string SkillsDirectory => _skillTools.SkillsDirectory;

    internal GameProjectLlmSettings Settings => _settings;

    public async Task<string> ChatAsync(
        string userText,
        string? systemPrompt = null,
        string? model = null,
        float? temperature = null,
        CancellationToken cancellationToken = default)
    {
        string result = string.Empty;
        await foreach (RuntimeLlmStreamUpdate update in StreamChatAsync(userText, systemPrompt, model, temperature, cancellationToken))
        {
            result = update.AccumulatedText;
        }

        return result;
    }

    public async Task<string> ChatWithToolsAsync(
        string userText,
        IEnumerable<RuntimeLlmTool> tools,
        string? systemPrompt = null,
        string? model = null,
        float? temperature = null,
        int maxToolRounds = DefaultMaxToolRounds,
        CancellationToken cancellationToken = default)
    {
        string result = string.Empty;
        List<RuntimeLlmChatMessage> messages = [];
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new RuntimeLlmChatMessage("system", systemPrompt));
        }

        messages.Add(new RuntimeLlmChatMessage("user", userText));
        await foreach (RuntimeLlmStreamUpdate update in StreamChatWithToolsAsync(messages, tools, model, temperature, maxToolRounds, cancellationToken))
        {
            result = update.AccumulatedText;
        }

        return result;
    }

    public string StartChat(
        RuntimeEntity callbackTarget,
        string userText,
        string? systemPrompt = null,
        string? model = null,
        float? temperature = null,
        Action<RuntimeLlmStreamUpdate>? onDelta = null,
        Action<RuntimeLlmResult>? onCompleted = null,
        Action<Exception>? onError = null,
        string? requestId = null,
        string? onDeltaCallback = null,
        string? onCompletedCallback = null,
        string? onErrorCallback = null)
    {
        ArgumentNullException.ThrowIfNull(callbackTarget);

        List<RuntimeLlmChatMessage> messages = [];
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new RuntimeLlmChatMessage("system", systemPrompt));
        }

        messages.Add(new RuntimeLlmChatMessage("user", userText));
        return StartChat(
            callbackTarget,
            messages,
            model,
            temperature,
            onDelta,
            onCompleted,
            onError,
            requestId,
            onDeltaCallback,
            onCompletedCallback,
            onErrorCallback);
    }

    public string StartChat(
        RuntimeEntity callbackTarget,
        IEnumerable<RuntimeLlmChatMessage> messages,
        string? model = null,
        float? temperature = null,
        Action<RuntimeLlmStreamUpdate>? onDelta = null,
        Action<RuntimeLlmResult>? onCompleted = null,
        Action<Exception>? onError = null,
        string? requestId = null,
        string? onDeltaCallback = null,
        string? onCompletedCallback = null,
        string? onErrorCallback = null)
    {
        return StartChatCore(
            callbackTarget,
            messages,
            model,
            temperature,
            tools: null,
            maxToolRounds: DefaultMaxToolRounds,
            onDelta,
            onCompleted,
            onError,
            requestId,
            onDeltaCallback,
            onCompletedCallback,
            onErrorCallback,
            onToolCallCallback: null,
            onToolResultCallback: null);
    }

    public string StartChatWithTools(
        RuntimeEntity callbackTarget,
        string userText,
        IEnumerable<RuntimeLlmTool> tools,
        string? systemPrompt = null,
        string? model = null,
        float? temperature = null,
        string? requestId = null,
        string? onDeltaCallback = null,
        string? onCompletedCallback = null,
        string? onErrorCallback = null,
        string? onToolCallCallback = null,
        string? onToolResultCallback = null,
        int maxToolRounds = DefaultMaxToolRounds)
    {
        ArgumentNullException.ThrowIfNull(callbackTarget);

        List<RuntimeLlmChatMessage> messages = [];
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new RuntimeLlmChatMessage("system", systemPrompt));
        }

        messages.Add(new RuntimeLlmChatMessage("user", userText));
        return StartChatWithTools(
            callbackTarget,
            messages,
            tools,
            model,
            temperature,
            requestId,
            onDeltaCallback,
            onCompletedCallback,
            onErrorCallback,
            onToolCallCallback,
            onToolResultCallback,
            maxToolRounds);
    }

    public string StartChatWithTools(
        RuntimeEntity callbackTarget,
        IEnumerable<RuntimeLlmChatMessage> messages,
        IEnumerable<RuntimeLlmTool> tools,
        string? model = null,
        float? temperature = null,
        string? requestId = null,
        string? onDeltaCallback = null,
        string? onCompletedCallback = null,
        string? onErrorCallback = null,
        string? onToolCallCallback = null,
        string? onToolResultCallback = null,
        int maxToolRounds = DefaultMaxToolRounds)
    {
        return StartChatCore(
            callbackTarget,
            messages,
            model,
            temperature,
            tools,
            maxToolRounds,
            onDelta: null,
            onCompleted: null,
            onError: null,
            requestId,
            onDeltaCallback,
            onCompletedCallback,
            onErrorCallback,
            onToolCallCallback,
            onToolResultCallback);
    }

    public IAsyncEnumerable<RuntimeLlmStreamUpdate> StreamChatAsync(
        string userText,
        string? systemPrompt = null,
        string? model = null,
        float? temperature = null,
        CancellationToken cancellationToken = default)
    {
        List<RuntimeLlmChatMessage> messages = [];
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new RuntimeLlmChatMessage("system", systemPrompt));
        }

        messages.Add(new RuntimeLlmChatMessage("user", userText));
        return StreamChatAsync(messages, model, temperature, cancellationToken);
    }

    public async IAsyncEnumerable<RuntimeLlmStreamUpdate> StreamChatAsync(
        IEnumerable<RuntimeLlmChatMessage> messages,
        string? model = null,
        float? temperature = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<RuntimeLlmTool> effectiveTools = CreateEffectiveTools(null);
        if (effectiveTools.Count > 0)
        {
            await foreach (RuntimeLlmStreamUpdate update in StreamChatWithToolsAsync(
                messages,
                effectiveTools,
                model,
                temperature,
                DefaultMaxToolRounds,
                cancellationToken))
            {
                yield return update;
            }

            yield break;
        }

        await foreach (RuntimeLlmStreamUpdate update in StreamChatCoreAsync(
            messages,
            model,
            temperature,
            tools: null,
            cancellationToken))
        {
            yield return update;
        }
    }

    public async IAsyncEnumerable<RuntimeLlmStreamUpdate> StreamChatWithToolsAsync(
        IEnumerable<RuntimeLlmChatMessage> messages,
        IEnumerable<RuntimeLlmTool> tools,
        string? model = null,
        float? temperature = null,
        int maxToolRounds = DefaultMaxToolRounds,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        List<RuntimeLlmChatMessage> conversation = messages.ToList();
        List<RuntimeLlmTool> toolList = CreateEffectiveTools(tools);
        RuntimeLlmStreamUpdate lastUpdate = new(string.Empty, string.Empty, false);

        for (int round = 0; round <= Math.Max(0, maxToolRounds); round++)
        {
            RuntimeLlmStreamUpdate roundLastUpdate = lastUpdate;
            await foreach (RuntimeLlmStreamUpdate update in StreamChatCoreAsync(
                conversation,
                model,
                temperature,
                toolList,
                cancellationToken))
            {
                roundLastUpdate = update;
                if (update.ToolCalls.Count == 0)
                {
                    yield return update;
                }
            }

            lastUpdate = roundLastUpdate;
            if (roundLastUpdate.ToolCalls.Count == 0)
            {
                yield break;
            }

            conversation.Add(new RuntimeLlmChatMessage("assistant", roundLastUpdate.AccumulatedText)
            {
                ToolCalls = roundLastUpdate.ToolCalls
            });

            foreach (RuntimeLlmToolCall toolCall in roundLastUpdate.ToolCalls)
            {
                RuntimeLlmTool? tool = FindTool(toolList, toolCall.Name);
                string result = tool is null
                    ? $"Tool '{toolCall.Name}' is not registered."
                    : await tool.InvokeAsync(toolCall, cancellationToken);
                conversation.Add(new RuntimeLlmChatMessage("tool", result)
                {
                    ToolCallId = toolCall.Id
                });
            }
        }

        throw new InvalidOperationException($"LLM tool call loop exceeded maxToolRounds={maxToolRounds}.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancellationTokenSource[] requests;
        lock (_sync)
        {
            requests = _activeRequests.ToArray();
            _activeRequests.Clear();
        }

        foreach (CancellationTokenSource request in requests)
        {
            request.Cancel();
            request.Dispose();
        }

        _client?.Dispose();
        _client = null;
    }

    private string StartChatCore(
        RuntimeEntity callbackTarget,
        IEnumerable<RuntimeLlmChatMessage> messages,
        string? model,
        float? temperature,
        IEnumerable<RuntimeLlmTool>? tools,
        int maxToolRounds,
        Action<RuntimeLlmStreamUpdate>? onDelta,
        Action<RuntimeLlmResult>? onCompleted,
        Action<Exception>? onError,
        string? requestId,
        string? onDeltaCallback,
        string? onCompletedCallback,
        string? onErrorCallback,
        string? onToolCallCallback,
        string? onToolResultCallback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callbackTarget);

        string resolvedRequestId = string.IsNullOrWhiteSpace(requestId)
            ? Guid.NewGuid().ToString("N")
            : requestId.Trim();
        List<RuntimeLlmChatMessage> capturedMessages = messages.ToList();
        List<RuntimeLlmTool> capturedTools = CreateEffectiveTools(tools);

        try
        {
            CancellationTokenSource cts = new();
            lock (_sync)
            {
                _activeRequests.Add(cts);
            }

            _ = Task.Run(async () =>
            {
                string accumulated = string.Empty;
                try
                {
                    IAsyncEnumerable<RuntimeLlmStreamUpdate> stream = capturedTools.Count > 0
                        ? StreamChatWithToolsForBackgroundAsync(
                            callbackTarget,
                            resolvedRequestId,
                            capturedMessages,
                            capturedTools,
                            model,
                            temperature,
                            maxToolRounds,
                            onToolCallCallback,
                            onToolResultCallback,
                            cts.Token)
                        : StreamChatAsync(capturedMessages, model, temperature, cts.Token);

                    await foreach (RuntimeLlmStreamUpdate update in stream)
                    {
                        accumulated = update.AccumulatedText;
                        RuntimeLlmStreamUpdate capturedUpdate = update;
                        _dispatcher.Post(() =>
                        {
                            if (_disposed)
                            {
                                return;
                            }

                            onDelta?.Invoke(capturedUpdate);
                            DispatchScriptEvent(
                                callbackTarget,
                                new RuntimeLlmScriptEvent(
                                    resolvedRequestId,
                                    "delta",
                                    capturedUpdate.Delta,
                                    capturedUpdate.AccumulatedText,
                                    capturedUpdate.IsFinal,
                                    string.Empty,
                                    onDeltaCallback ?? string.Empty,
                                    null,
                                    null));
                        });
                    }

                    RuntimeLlmResult result = new(resolvedRequestId, accumulated);
                    _dispatcher.Post(() =>
                    {
                        if (_disposed)
                        {
                            return;
                        }

                        onCompleted?.Invoke(result);
                        DispatchScriptEvent(
                            callbackTarget,
                            new RuntimeLlmScriptEvent(
                                resolvedRequestId,
                                "completed",
                                string.Empty,
                                result.Text,
                                true,
                                string.Empty,
                                onCompletedCallback ?? string.Empty,
                                null,
                                null));
                    });
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested || _disposed)
                {
                }
                catch (Exception ex)
                {
                    _dispatcher.Post(() =>
                    {
                        if (_disposed)
                        {
                            return;
                        }

                        onError?.Invoke(ex);
                        DispatchScriptEvent(
                            callbackTarget,
                            new RuntimeLlmScriptEvent(
                                resolvedRequestId,
                                "error",
                                string.Empty,
                                accumulated,
                                true,
                                ex.Message,
                                onErrorCallback ?? string.Empty,
                                null,
                                null));
                    });
                }
                finally
                {
                    lock (_sync)
                    {
                        _activeRequests.Remove(cts);
                    }

                    cts.Dispose();
                }
            });
        }
        catch (Exception ex)
        {
            _dispatcher.Post(() =>
            {
                onError?.Invoke(ex);
                DispatchScriptEvent(
                    callbackTarget,
                    new RuntimeLlmScriptEvent(
                        resolvedRequestId,
                        "error",
                        string.Empty,
                        string.Empty,
                        true,
                        ex.Message,
                        onErrorCallback ?? string.Empty,
                        null,
                        null));
            });
        }

        return resolvedRequestId;
    }

    private async IAsyncEnumerable<RuntimeLlmStreamUpdate> StreamChatWithToolsForBackgroundAsync(
        RuntimeEntity callbackTarget,
        string requestId,
        IEnumerable<RuntimeLlmChatMessage> messages,
        IReadOnlyList<RuntimeLlmTool> tools,
        string? model,
        float? temperature,
        int maxToolRounds,
        string? onToolCallCallback,
        string? onToolResultCallback,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        List<RuntimeLlmChatMessage> conversation = messages.ToList();
        RuntimeLlmStreamUpdate lastUpdate = new(string.Empty, string.Empty, false);

        for (int round = 0; round <= Math.Max(0, maxToolRounds); round++)
        {
            RuntimeLlmStreamUpdate roundLastUpdate = lastUpdate;
            await foreach (RuntimeLlmStreamUpdate update in StreamChatCoreAsync(
                conversation,
                model,
                temperature,
                tools,
                cancellationToken))
            {
                roundLastUpdate = update;
                if (update.ToolCalls.Count == 0)
                {
                    yield return update;
                }
            }

            lastUpdate = roundLastUpdate;
            if (roundLastUpdate.ToolCalls.Count == 0)
            {
                yield break;
            }

            conversation.Add(new RuntimeLlmChatMessage("assistant", roundLastUpdate.AccumulatedText)
            {
                ToolCalls = roundLastUpdate.ToolCalls
            });

            foreach (RuntimeLlmToolCall toolCall in roundLastUpdate.ToolCalls)
            {
                DispatchToolEvent(callbackTarget, requestId, "tool_call", onToolCallCallback, toolCall, null);
                RuntimeLlmTool? tool = FindTool(tools, toolCall.Name);
                string result = tool is null
                    ? $"Tool '{toolCall.Name}' is not registered."
                    : await tool.InvokeAsync(toolCall, cancellationToken);
                DispatchToolEvent(callbackTarget, requestId, "tool_result", onToolResultCallback, toolCall, result);
                conversation.Add(new RuntimeLlmChatMessage("tool", result)
                {
                    ToolCallId = toolCall.Id
                });
            }
        }

        throw new InvalidOperationException($"LLM tool call loop exceeded maxToolRounds={maxToolRounds}.");
    }

    private async IAsyncEnumerable<RuntimeLlmStreamUpdate> StreamChatCoreAsync(
        IEnumerable<RuntimeLlmChatMessage> messages,
        string? model,
        float? temperature,
        IEnumerable<RuntimeLlmTool>? tools,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_settings.Enabled)
        {
            throw new InvalidOperationException("Project LLM is disabled. Enable Project.Llm.Enabled in GameEditor or game.project.json.");
        }

        string resolvedModel = ResolveModel(model);
        List<LlmChatMessage> requestMessages = messages
            .Where(message => !string.IsNullOrWhiteSpace(message.Role) && IsValidMessage(message))
            .Select(ToLlmChatMessage)
            .ToList();

        if (requestMessages.Count == 0)
        {
            throw new ArgumentException("At least one LLM message is required.", nameof(messages));
        }

        IReadOnlyList<LlmToolDefinition>? requestTools = tools?
            .Select(tool => new LlmToolDefinition(tool.Name, tool.Description, tool.ParametersJsonSchema))
            .ToArray();

        await foreach (LlmStreamUpdate update in GetClient().StreamChatAsync(
            requestMessages,
            new LlmRequestOptions
            {
                Model = resolvedModel,
                Temperature = temperature ?? _settings.DefaultTemperature,
                Tools = requestTools
            },
            cancellationToken))
        {
            yield return new RuntimeLlmStreamUpdate(
                update.Delta,
                update.AccumulatedText,
                update.IsFinal,
                update.ToolCalls.Select(call => new RuntimeLlmToolCall(call.Id, call.Name, call.ArgumentsJson)).ToArray());
        }
    }

    private void DispatchToolEvent(
        RuntimeEntity target,
        string requestId,
        string eventName,
        string? callbackName,
        RuntimeLlmToolCall toolCall,
        string? toolResult)
    {
        _dispatcher.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            DispatchScriptEvent(
                target,
                new RuntimeLlmScriptEvent(
                    requestId,
                    eventName,
                    string.Empty,
                    string.Empty,
                    false,
                    string.Empty,
                    callbackName ?? string.Empty,
                    toolCall,
                    toolResult));
        });
    }

    private void DispatchScriptEvent(RuntimeEntity target, RuntimeLlmScriptEvent scriptEvent)
    {
        _dispatchScriptEvent(target, scriptEvent);
    }

    private OpenAiCompatibleLlmClient GetClient()
    {
        if (_client is not null)
        {
            return _client;
        }

        string apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            throw new InvalidOperationException("Project LLM BaseUrl is required.");
        }

        _client = new OpenAiCompatibleLlmClient(
            new OpenAiCompatibleLlmOptions
            {
                BaseUrl = _settings.BaseUrl,
                ApiKey = apiKey,
                Model = _settings.Model,
                ChatCompletionsPath = string.IsNullOrWhiteSpace(_settings.ChatCompletionsPath)
                    ? "/v1/chat/completions"
                    : _settings.ChatCompletionsPath,
                Timeout = TimeSpan.FromSeconds(Math.Clamp(_settings.TimeoutSeconds, 1, 3600))
            },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenAiCompatibleLlmClient>.Instance);
        return _client;
    }

    private string ResolveModel(string? overrideModel)
    {
        string? model = !string.IsNullOrWhiteSpace(overrideModel)
            ? overrideModel
            : _settings.Model;
        return string.IsNullOrWhiteSpace(model)
            ? throw new InvalidOperationException("Project LLM Model is required.")
            : model;
    }

    private string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            return _settings.ApiKey;
        }

        if (!string.IsNullOrWhiteSpace(_settings.ApiKeyEnvironmentVariable))
        {
            return Environment.GetEnvironmentVariable(_settings.ApiKeyEnvironmentVariable) ?? string.Empty;
        }

        return string.Empty;
    }

    private static RuntimeLlmTool? FindTool(IEnumerable<RuntimeLlmTool> tools, string name)
    {
        return tools.FirstOrDefault(tool => string.Equals(tool.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private List<RuntimeLlmTool> CreateEffectiveTools(IEnumerable<RuntimeLlmTool>? tools)
    {
        Dictionary<string, RuntimeLlmTool> effectiveTools = new(StringComparer.OrdinalIgnoreCase);
        if (tools is not null)
        {
            foreach (RuntimeLlmTool tool in tools)
            {
                if (!string.IsNullOrWhiteSpace(tool.Name))
                {
                    effectiveTools[tool.Name.Trim()] = tool;
                }
            }
        }

        if (_settings.EnableSkills)
        {
            foreach (RuntimeLlmTool tool in _skillTools.Tools)
            {
                if (!effectiveTools.ContainsKey(tool.Name))
                {
                    effectiveTools[tool.Name] = tool;
                }
            }
        }

        return effectiveTools.Values.ToList();
    }

    private static bool IsValidMessage(RuntimeLlmChatMessage message)
    {
        return !string.IsNullOrWhiteSpace(message.Content)
            || !string.IsNullOrWhiteSpace(message.ToolCallId)
            || message.ToolCalls.Count > 0;
    }

    private static LlmChatMessage ToLlmChatMessage(RuntimeLlmChatMessage message)
    {
        return new LlmChatMessage(message.Role.Trim(), message.Content)
        {
            ToolCallId = message.ToolCallId,
            ToolCalls = message.ToolCalls.Select(call => new LlmToolCall(call.Id, call.Name, call.ArgumentsJson)).ToArray()
        };
    }
}

public sealed record RuntimeLlmChatMessage(string Role, string Content)
{
    public string? ToolCallId { get; init; }

    public IReadOnlyList<RuntimeLlmToolCall> ToolCalls { get; init; } = [];
}

public sealed record RuntimeLlmToolCall(string Id, string Name, string ArgumentsJson);

public sealed record RuntimeLlmStreamUpdate(
    string Delta,
    string AccumulatedText,
    bool IsFinal,
    IReadOnlyList<RuntimeLlmToolCall> ToolCalls)
{
    public RuntimeLlmStreamUpdate(string delta, string accumulatedText, bool isFinal)
        : this(delta, accumulatedText, isFinal, [])
    {
    }
}

public sealed record RuntimeLlmResult(string RequestId, string Text);

public sealed record RuntimeLlmTool(
    string Name,
    string Description,
    string ParametersJsonSchema,
    Func<RuntimeLlmToolCall, CancellationToken, Task<string>> Handler)
{
    public RuntimeLlmTool(string name, string description, string parametersJsonSchema, Func<string, string> handler)
        : this(name, description, parametersJsonSchema, (call, _) => Task.FromResult(handler(call.ArgumentsJson)))
    {
    }

    public RuntimeLlmTool(string name, string description, string parametersJsonSchema, Func<string, CancellationToken, Task<string>> handler)
        : this(name, description, parametersJsonSchema, (call, cancellationToken) => handler(call.ArgumentsJson, cancellationToken))
    {
    }

    public Task<string> InvokeAsync(RuntimeLlmToolCall toolCall, CancellationToken cancellationToken)
    {
        return Handler(toolCall, cancellationToken);
    }
}

public sealed record RuntimeLlmScriptTool(
    string Name,
    string Description,
    string ParametersJsonSchema,
    string CallbackName)
{
    public RuntimeLlmTool ToTool(RuntimeEntity entity, RuntimeScene scene)
    {
        return new RuntimeLlmTool(
            Name,
            Description,
            ParametersJsonSchema,
            async (toolCall, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? result = await scene.InvokeLlmToolAsync(entity, CallbackName, toolCall);
                return string.IsNullOrWhiteSpace(result) ? "{}" : result;
            });
    }
}

public sealed record RuntimeLlmScriptEvent(
    string RequestId,
    string EventName,
    string Delta,
    string AccumulatedText,
    bool IsFinal,
    string Error,
    string CallbackName,
    RuntimeLlmToolCall? ToolCall,
    string? ToolResult);

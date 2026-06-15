using System.Net;
using System.Text.Json;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Llm;
using Zhengyan.DigitalWife.Llm.OpenAI;

namespace Zhengyan.DigitalWife.GamePlayer;

public sealed class RuntimeLlm : IDisposable
{
    private const int DefaultMaxToolRounds = 4;
    private const int NativeToolFirstUpdateTimeoutSeconds = 20;
    private const string TextToolCallStart = "<dw_tool_call>";
    private const string TextToolCallEnd = "</dw_tool_call>";

    private sealed record ResolvedToolCalls(IReadOnlyList<RuntimeLlmToolCall> Calls, bool FromTextProtocol);

    private sealed record ToolRoundResult(
        RuntimeLlmStreamUpdate LastUpdate,
        bool NativeToolsEnabled,
        IReadOnlyList<RuntimeLlmStreamUpdate> Updates);

    private sealed record StreamBuffer(
        RuntimeLlmStreamUpdate LastUpdate,
        List<RuntimeLlmStreamUpdate> Updates,
        bool SawToolCallDelta);

    private sealed class ActiveLlmRequest
    {
        public ActiveLlmRequest(string requestId, CancellationTokenSource cancellation)
        {
            RequestId = requestId;
            Cancellation = cancellation;
        }

        public string RequestId { get; }

        public CancellationTokenSource Cancellation { get; }

        public string CancelReason { get; set; } = "unknown";
    }

    private readonly GameProjectLlmSettings _settings;
    private readonly string _projectDirectory;
    private readonly RuntimeLlmSkillTools _skillTools;
    private readonly MainThreadDispatcher _dispatcher;
    private readonly Action<RuntimeEntity, RuntimeLlmScriptEvent> _dispatchScriptEvent;
    private readonly object _sync = new();
    private readonly List<ActiveLlmRequest> _activeRequests = [];
    private OpenAiCompatibleLlmClient? _client;
    private bool _disposed;

    internal RuntimeLlm(
        GameProjectLlmSettings settings,
        string projectDirectory,
        string saveDirectory,
        MainThreadDispatcher dispatcher,
        Action<RuntimeEntity, RuntimeLlmScriptEvent> dispatchScriptEvent)
    {
        _settings = settings;
        _projectDirectory = Path.GetFullPath(projectDirectory);
        _skillTools = new RuntimeLlmSkillTools(_projectDirectory, saveDirectory);
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

    public bool MemoryEnabled => _settings.EnableMemory;

    public string SkillsDirectory => _skillTools.SkillsDirectory;

    public string MemoryDirectory => _skillTools.MemoryDirectory;

    public string GetCharacterMemoryPath(RuntimeEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return _skillTools.GetCharacterMemoryPath(entity.Name);
    }

    public string GetCharacterMemoryPath(string characterName)
        => _skillTools.GetCharacterMemoryPath(characterName);

    internal GameProjectLlmSettings Settings => _settings;

    public void CancelRequest(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        ActiveLlmRequest[] requests;
        lock (_sync)
        {
            string normalizedRequestId = requestId.Trim();
            requests = _activeRequests
                .Where(request => string.Equals(request.RequestId, normalizedRequestId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (ActiveLlmRequest request in requests)
            {
                request.CancelReason = "cancel_request";
            }
        }

        foreach (ActiveLlmRequest request in requests)
        {
            request.Cancellation.Cancel();
        }
    }

    public void CancelAllRequests()
    {
        ActiveLlmRequest[] requests;
        lock (_sync)
        {
            requests = _activeRequests.ToArray();
            foreach (ActiveLlmRequest request in requests)
            {
                request.CancelReason = "cancel_all_requests";
            }
        }

        foreach (ActiveLlmRequest request in requests)
        {
            request.Cancellation.Cancel();
        }
    }

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
        List<RuntimeLlmTool> toolList = CreateEffectiveTools(tools);
        List<RuntimeLlmChatMessage> conversation = CreateToolAwareConversation(messages, toolList);
        RuntimeLlmStreamUpdate lastUpdate = new(string.Empty, string.Empty, false);
        bool nativeToolsEnabled = toolList.Count > 0;

        for (int round = 0; round <= Math.Max(0, maxToolRounds); round++)
        {
            ToolRoundResult roundResult = await ReadToolRoundAsync(
                conversation,
                model,
                temperature,
                toolList,
                nativeToolsEnabled,
                lastUpdate,
                cancellationToken);

            RuntimeLlmStreamUpdate roundLastUpdate = roundResult.LastUpdate;
            lastUpdate = roundLastUpdate;
            nativeToolsEnabled = roundResult.NativeToolsEnabled;
            ResolvedToolCalls resolvedToolCalls = ResolveToolCalls(roundLastUpdate);
            IReadOnlyList<RuntimeLlmToolCall> toolCalls = resolvedToolCalls.Calls;
            if (toolCalls.Count == 0)
            {
                foreach (RuntimeLlmStreamUpdate update in FilterVisibleUpdates(roundResult.Updates, toolCallsDetected: false))
                {
                    yield return update;
                }

                if (roundResult.Updates.Count == 0
                    && (!string.IsNullOrEmpty(roundLastUpdate.AccumulatedText) || !string.IsNullOrEmpty(roundLastUpdate.Delta)))
                {
                    yield return roundLastUpdate;
                }

                yield break;
            }

            foreach (RuntimeLlmStreamUpdate update in FilterVisibleUpdates(roundResult.Updates, toolCallsDetected: true))
            {
                yield return update;
            }

            if (resolvedToolCalls.FromTextProtocol)
            {
                nativeToolsEnabled = false;
            }

            AddToolCallMessage(conversation, roundLastUpdate, resolvedToolCalls);

            foreach (RuntimeLlmToolCall toolCall in toolCalls)
            {
                RuntimeLlmTool? tool = FindTool(toolList, toolCall.Name);
                string result = tool is null
                    ? $"Tool '{toolCall.Name}' is not registered."
                    : await tool.InvokeAsync(toolCall, cancellationToken);
                AddToolResultMessage(conversation, toolCall, result, resolvedToolCalls.FromTextProtocol);
            }
        }

        throw new InvalidOperationException($"LLM tool call loop exceeded maxToolRounds={maxToolRounds}.");
    }

    private static IEnumerable<RuntimeLlmStreamUpdate> FilterVisibleUpdates(
        IReadOnlyList<RuntimeLlmStreamUpdate> updates,
        bool toolCallsDetected)
    {
        if (!toolCallsDetected)
        {
            return updates;
        }

        return updates.Where(update =>
            update.ToolCalls.Count == 0
            && !LooksLikeTextToolCallPayload(update.AccumulatedText)
            && !LooksLikeTextToolCallPayload(update.Delta));
    }

    private static bool IsVisibleToolRoundUpdate(RuntimeLlmStreamUpdate update)
    {
        return update.ToolCalls.Count == 0
            && !LooksLikeTextToolCallPayload(update.AccumulatedText)
            && !LooksLikeTextToolCallPayload(update.Delta);
    }

    private static bool CanStartVisibleToolStreaming(string accumulatedText)
    {
        if (string.IsNullOrWhiteSpace(accumulatedText))
        {
            return false;
        }

        return !CouldBeTextToolCallPayloadPrefix(accumulatedText)
            && !LooksLikeTextToolCallPayload(accumulatedText);
    }

    private static bool CouldBeTextToolCallPayloadPrefix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        string trimmed = text.TrimStart();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return true;
        }

        if (trimmed.StartsWith("```", StringComparison.Ordinal)
            || "```".StartsWith(trimmed, StringComparison.Ordinal))
        {
            return true;
        }

        return TextToolCallStart.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ActiveLlmRequest[] requests;
        lock (_sync)
        {
            requests = _activeRequests.ToArray();
            _activeRequests.Clear();
            foreach (ActiveLlmRequest request in requests)
            {
                request.CancelReason = "dispose";
            }
        }

        foreach (ActiveLlmRequest request in requests)
        {
            request.Cancellation.Cancel();
            request.Cancellation.Dispose();
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
        string displayModel = string.IsNullOrWhiteSpace(model) ? _settings.Model : model.Trim();

        try
        {
            Console.WriteLine(
                $"[GamePlayer] LLM request start request={resolvedRequestId}, target={callbackTarget.Name}, " +
                $"model={displayModel}, tools={capturedTools.Count}, messages={capturedMessages.Count}");
            CancellationTokenSource cts = new();
            ActiveLlmRequest activeRequest = new(resolvedRequestId, cts);
            lock (_sync)
            {
                _activeRequests.Add(activeRequest);
            }

            _ = Task.Run(async () =>
            {
                string accumulated = string.Empty;
                try
                {
                    void DispatchDelta(RuntimeLlmStreamUpdate update)
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

                    if (capturedTools.Count > 0)
                    {
                        accumulated = await RunChatWithToolsForBackgroundAsync(
                            callbackTarget,
                            resolvedRequestId,
                            capturedMessages,
                            capturedTools,
                            model,
                            temperature,
                            maxToolRounds,
                            onToolCallCallback,
                            onToolResultCallback,
                            DispatchDelta,
                            cts.Token);
                    }
                    else
                    {
                        await foreach (RuntimeLlmStreamUpdate update in StreamChatAsync(capturedMessages, model, temperature, cts.Token))
                        {
                            DispatchDelta(update);
                        }
                    }

                    RuntimeLlmResult result = new(resolvedRequestId, accumulated);
                    Console.WriteLine(
                        $"[GamePlayer] LLM request completed request={resolvedRequestId}, " +
                        $"textLength={result.Text.Length}");
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
                    Console.WriteLine(
                        $"[GamePlayer] LLM request canceled request={resolvedRequestId}, " +
                        $"reason={activeRequest.CancelReason}.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[GamePlayer] LLM request failed request={resolvedRequestId}: {ex.Message}");
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
                        _activeRequests.Remove(activeRequest);
                    }

                    cts.Dispose();
                }
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GamePlayer] LLM request failed request={resolvedRequestId}: {ex.Message}");
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

    private async Task<string> RunChatWithToolsForBackgroundAsync(
        RuntimeEntity callbackTarget,
        string requestId,
        IEnumerable<RuntimeLlmChatMessage> messages,
        IReadOnlyList<RuntimeLlmTool> tools,
        string? model,
        float? temperature,
        int maxToolRounds,
        string? onToolCallCallback,
        string? onToolResultCallback,
        Action<RuntimeLlmStreamUpdate> onVisibleUpdate,
        CancellationToken cancellationToken)
    {
        List<RuntimeLlmChatMessage> conversation = CreateToolAwareConversation(messages, tools);
        RuntimeLlmStreamUpdate lastUpdate = new(string.Empty, string.Empty, false);
        bool nativeToolsEnabled = tools.Count > 0;

        for (int round = 0; round <= Math.Max(0, maxToolRounds); round++)
        {
            RuntimeLlmStreamUpdate roundLastUpdate = lastUpdate;
            List<RuntimeLlmStreamUpdate> pendingVisibleUpdates = [];
            bool visibleStreamingStarted = false;

            while (true)
            {
                roundLastUpdate = lastUpdate;
                pendingVisibleUpdates.Clear();
                visibleStreamingStarted = false;

                try
                {
                    IAsyncEnumerable<RuntimeLlmStreamUpdate> stream = StreamChatCoreAsync(
                        conversation,
                        model,
                        temperature,
                        nativeToolsEnabled ? tools : null,
                        cancellationToken);

                    if (nativeToolsEnabled)
                    {
                        await foreach (RuntimeLlmStreamUpdate update in ReadWithFirstUpdateTimeoutAsync(stream, cancellationToken))
                        {
                            roundLastUpdate = update;
                            if (!IsVisibleToolRoundUpdate(update))
                            {
                                continue;
                            }

                            if (!visibleStreamingStarted)
                            {
                                pendingVisibleUpdates.Add(update);
                                if (CanStartVisibleToolStreaming(update.AccumulatedText))
                                {
                                    visibleStreamingStarted = true;
                                    foreach (RuntimeLlmStreamUpdate pendingUpdate in pendingVisibleUpdates)
                                    {
                                        onVisibleUpdate(pendingUpdate);
                                    }

                                    pendingVisibleUpdates.Clear();
                                }

                                continue;
                            }

                            onVisibleUpdate(update);
                        }
                    }
                    else
                    {
                        await foreach (RuntimeLlmStreamUpdate update in stream.WithCancellation(cancellationToken))
                        {
                            roundLastUpdate = update;
                            if (!IsVisibleToolRoundUpdate(update))
                            {
                                continue;
                            }

                            if (!visibleStreamingStarted)
                            {
                                pendingVisibleUpdates.Add(update);
                                if (CanStartVisibleToolStreaming(update.AccumulatedText))
                                {
                                    visibleStreamingStarted = true;
                                    foreach (RuntimeLlmStreamUpdate pendingUpdate in pendingVisibleUpdates)
                                    {
                                        onVisibleUpdate(pendingUpdate);
                                    }

                                    pendingVisibleUpdates.Clear();
                                }

                                continue;
                            }

                            onVisibleUpdate(update);
                        }
                    }

                    break;
                }
                catch (HttpRequestException exception) when (nativeToolsEnabled && IsNativeToolPayloadRejected(exception))
                {
                    Console.Error.WriteLine($"[GamePlayer] LLM backend rejected native tool payload ({(int?)exception.StatusCode}); retrying with text tool protocol.");
                    nativeToolsEnabled = false;
                }
                catch (TimeoutException exception) when (nativeToolsEnabled)
                {
                    Console.Error.WriteLine($"[GamePlayer] LLM native tool stream timed out before first update; retrying with text tool protocol. {exception.Message}");
                    nativeToolsEnabled = false;
                }
            }

            lastUpdate = roundLastUpdate;
            ResolvedToolCalls resolvedToolCalls = ResolveToolCalls(roundLastUpdate);
            IReadOnlyList<RuntimeLlmToolCall> toolCalls = resolvedToolCalls.Calls;
            if (toolCalls.Count == 0)
            {
                foreach (RuntimeLlmStreamUpdate update in pendingVisibleUpdates)
                {
                    onVisibleUpdate(update);
                }

                return roundLastUpdate.AccumulatedText;
            }

            if (resolvedToolCalls.FromTextProtocol)
            {
                nativeToolsEnabled = false;
            }

            AddToolCallMessage(conversation, roundLastUpdate, resolvedToolCalls);

            foreach (RuntimeLlmToolCall toolCall in toolCalls)
            {
                Console.WriteLine(
                    $"[GamePlayer] LLM tool call request={requestId}, name={toolCall.Name}, " +
                    $"argumentsLength={toolCall.ArgumentsJson.Length}");
                DispatchToolEvent(callbackTarget, requestId, "tool_call", onToolCallCallback, toolCall, null);
                RuntimeLlmTool? tool = FindTool(tools, toolCall.Name);
                string result = tool is null
                    ? $"Tool '{toolCall.Name}' is not registered."
                    : await tool.InvokeAsync(toolCall, cancellationToken);
                Console.WriteLine(
                    $"[GamePlayer] LLM tool result request={requestId}, name={toolCall.Name}, " +
                    $"resultLength={result.Length}");
                DispatchToolEvent(callbackTarget, requestId, "tool_result", onToolResultCallback, toolCall, result);
                AddToolResultMessage(conversation, toolCall, result, resolvedToolCalls.FromTextProtocol);
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

    private async Task<ToolRoundResult> ReadToolRoundAsync(
        List<RuntimeLlmChatMessage> conversation,
        string? model,
        float? temperature,
        IReadOnlyList<RuntimeLlmTool> tools,
        bool nativeToolsEnabled,
        RuntimeLlmStreamUpdate fallbackLastUpdate,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReadToolRoundCoreAsync(
                conversation,
                model,
                temperature,
                nativeToolsEnabled ? tools : null,
                fallbackLastUpdate,
                nativeToolsEnabled,
                cancellationToken);
        }
        catch (HttpRequestException exception) when (nativeToolsEnabled && IsNativeToolPayloadRejected(exception))
        {
            Console.Error.WriteLine($"[GamePlayer] LLM backend rejected native tool payload ({(int?)exception.StatusCode}); retrying with text tool protocol.");
            return await ReadToolRoundCoreAsync(
                conversation,
                model,
                temperature,
                tools: null,
                fallbackLastUpdate,
                nativeToolsEnabled: false,
                cancellationToken);
        }
        catch (TimeoutException exception) when (nativeToolsEnabled)
        {
            Console.Error.WriteLine($"[GamePlayer] LLM native tool stream timed out before first update; retrying with text tool protocol. {exception.Message}");
            return await ReadToolRoundCoreAsync(
                conversation,
                model,
                temperature,
                tools: null,
                fallbackLastUpdate,
                nativeToolsEnabled: false,
                cancellationToken);
        }
    }

    private async Task<ToolRoundResult> ReadToolRoundCoreAsync(
        List<RuntimeLlmChatMessage> conversation,
        string? model,
        float? temperature,
        IReadOnlyList<RuntimeLlmTool>? tools,
        RuntimeLlmStreamUpdate fallbackLastUpdate,
        bool nativeToolsEnabled,
        CancellationToken cancellationToken)
    {
        StreamBuffer buffer = await ReadToolRoundBufferAsync(
            conversation,
            model,
            temperature,
            tools,
            fallbackLastUpdate,
            nativeToolsEnabled,
            cancellationToken);

        return new ToolRoundResult(buffer.LastUpdate, nativeToolsEnabled, buffer.Updates);
    }

    private async Task<StreamBuffer> ReadToolRoundBufferAsync(
        List<RuntimeLlmChatMessage> conversation,
        string? model,
        float? temperature,
        IReadOnlyList<RuntimeLlmTool>? tools,
        RuntimeLlmStreamUpdate fallbackLastUpdate,
        bool nativeToolsEnabled,
        CancellationToken cancellationToken)
    {
        RuntimeLlmStreamUpdate lastUpdate = fallbackLastUpdate;
        List<RuntimeLlmStreamUpdate> updates = [];
        bool sawToolCallDelta = false;
        IAsyncEnumerable<RuntimeLlmStreamUpdate> stream = StreamChatCoreAsync(
            conversation,
            model,
            temperature,
            tools,
            cancellationToken);

        if (nativeToolsEnabled)
        {
            await foreach (RuntimeLlmStreamUpdate update in ReadWithFirstUpdateTimeoutAsync(stream, cancellationToken))
            {
                lastUpdate = update;
                updates.Add(update);
                sawToolCallDelta |= update.ToolCalls.Count > 0;
            }
        }
        else
        {
            await foreach (RuntimeLlmStreamUpdate update in stream.WithCancellation(cancellationToken))
            {
                lastUpdate = update;
                updates.Add(update);
                sawToolCallDelta |= update.ToolCalls.Count > 0;
            }
        }

        return new StreamBuffer(lastUpdate, updates, sawToolCallDelta);
    }

    private static async IAsyncEnumerable<RuntimeLlmStreamUpdate> ReadWithFirstUpdateTimeoutAsync(
        IAsyncEnumerable<RuntimeLlmStreamUpdate> stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using CancellationTokenSource firstUpdateTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        firstUpdateTimeout.CancelAfter(TimeSpan.FromSeconds(NativeToolFirstUpdateTimeoutSeconds));

        var enumerator = stream.WithCancellation(firstUpdateTimeout.Token).GetAsyncEnumerator();
        bool hasFirstUpdate = false;
        try
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !hasFirstUpdate)
                {
                    throw new TimeoutException($"No SSE update arrived within {NativeToolFirstUpdateTimeoutSeconds} seconds.");
                }

                if (!hasNext)
                {
                    yield break;
                }

                if (!hasFirstUpdate)
                {
                    hasFirstUpdate = true;
                    firstUpdateTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
                }

                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    private static bool IsNativeToolPayloadRejected(HttpRequestException exception)
    {
        return exception.StatusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.UnprocessableEntity
            or HttpStatusCode.NotImplemented
            or HttpStatusCode.UnsupportedMediaType;
    }

    private static ResolvedToolCalls ResolveToolCalls(RuntimeLlmStreamUpdate update)
    {
        if (update.ToolCalls.Count > 0)
        {
            return new ResolvedToolCalls(update.ToolCalls, FromTextProtocol: false);
        }

        RuntimeLlmToolCall? textToolCall = TryParseTextToolCall(update.AccumulatedText);
        return textToolCall is null
            ? new ResolvedToolCalls(Array.Empty<RuntimeLlmToolCall>(), FromTextProtocol: false)
            : new ResolvedToolCalls([textToolCall], FromTextProtocol: true);
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

    private static void AddToolCallMessage(
        List<RuntimeLlmChatMessage> conversation,
        RuntimeLlmStreamUpdate update,
        ResolvedToolCalls resolvedToolCalls)
    {
        if (resolvedToolCalls.FromTextProtocol)
        {
            string content = string.IsNullOrWhiteSpace(update.AccumulatedText)
                ? string.Join(
                    "\n",
                    resolvedToolCalls.Calls.Select(CreateTextToolCallMessage))
                : update.AccumulatedText;
            conversation.Add(new RuntimeLlmChatMessage("assistant", content));
            return;
        }

        conversation.Add(new RuntimeLlmChatMessage("assistant", string.Empty)
        {
            ToolCalls = resolvedToolCalls.Calls
        });
    }

    private static void AddToolResultMessage(
        List<RuntimeLlmChatMessage> conversation,
        RuntimeLlmToolCall toolCall,
        string result,
        bool fromTextProtocol)
    {
        if (fromTextProtocol)
        {
            conversation.Add(new RuntimeLlmChatMessage("user", CreateTextToolResultMessage(toolCall, result)));
            return;
        }

        conversation.Add(new RuntimeLlmChatMessage("tool", result)
        {
            ToolCallId = toolCall.Id
        });
    }

    private static string CreateTextToolResultMessage(RuntimeLlmToolCall toolCall, string result)
    {
        string example = TextToolCallStart + "{\"name\":\"tool_name\",\"arguments\":{}}" + TextToolCallEnd;
        return string.Join(
            "\n",
            $"Tool result for {toolCall.Name}:",
            string.IsNullOrWhiteSpace(result) ? "{}" : result,
            "",
            "Use this tool result to continue answering the original user request.",
            $"If another tool is required, output only {example}.");
    }

    private static string CreateTextToolCallMessage(RuntimeLlmToolCall toolCall)
    {
        string toolNameJson = JsonSerializer.Serialize(toolCall.Name);
        string argumentsJson = NormalizeArgumentsJson(toolCall.ArgumentsJson);
        return TextToolCallStart + "{\"name\":" + toolNameJson + ",\"arguments\":" + argumentsJson + "}" + TextToolCallEnd;
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

        if (_settings.EnableMemory)
        {
            foreach (RuntimeLlmTool tool in _skillTools.MemoryTools)
            {
                if (!effectiveTools.ContainsKey(tool.Name))
                {
                    effectiveTools[tool.Name] = tool;
                }
            }
        }

        if (_settings.EnableSkills)
        {
            foreach (RuntimeLlmTool tool in _skillTools.SkillTools)
            {
                if (!effectiveTools.ContainsKey(tool.Name))
                {
                    effectiveTools[tool.Name] = tool;
                }
            }
        }

        return effectiveTools.Values.ToList();
    }

    private static List<RuntimeLlmChatMessage> CreateToolAwareConversation(
        IEnumerable<RuntimeLlmChatMessage> messages,
        IReadOnlyList<RuntimeLlmTool> tools)
    {
        List<RuntimeLlmChatMessage> conversation = messages.ToList();
        if (tools.Count == 0)
        {
            return conversation;
        }

        string instruction = CreateTextToolCallInstruction(tools);
        int lastSystemIndex = -1;
        for (int i = 0; i < conversation.Count; i++)
        {
            if (string.Equals(conversation[i].Role, "system", StringComparison.OrdinalIgnoreCase))
            {
                lastSystemIndex = i;
            }
        }

        if (lastSystemIndex >= 0)
        {
            RuntimeLlmChatMessage system = conversation[lastSystemIndex];
            conversation[lastSystemIndex] = system with
            {
                Content = string.Join("\n\n", system.Content, instruction)
            };
            return conversation;
        }

        conversation.Insert(0, new RuntimeLlmChatMessage("system", instruction));
        return conversation;
    }

    private static string CreateTextToolCallInstruction(IReadOnlyList<RuntimeLlmTool> tools)
    {
        string toolList = string.Join(
            "\n",
            tools.Select(tool => $"- {tool.Name}: {tool.Description}"));

        return string.Join(
            "\n",
            "工具调用说明：",
            "如果你需要读取 skills、搜索项目、执行命令、联网查询或使用任何已注册工具，优先使用原生 OpenAI tool_calls/function calling。",
            "如果当前模型或服务不能发出原生 tool_calls，则你必须只输出下面这种文本协议，不要附加解释、寒暄或其它内容：",
            $"{TextToolCallStart}{{\"name\":\"tool_name\",\"arguments\":{{}}}}{TextToolCallEnd}",
            "运行时会执行该工具，并把工具结果继续发给你；拿到工具结果后再回答用户，或继续请求下一个工具。",
            "arguments 必须是 JSON object，name 必须是下列工具名之一。",
            "可用工具：",
            toolList);
    }

    private static RuntimeLlmToolCall? TryParseTextToolCall(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string payload = ExtractTextToolCallPayload(text);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string name = GetOptionalString(root, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                name = GetOptionalString(root, "tool");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string argumentsJson = "{}";
            if (root.TryGetProperty("arguments", out JsonElement arguments)
                || root.TryGetProperty("args", out arguments))
            {
                argumentsJson = arguments.ValueKind == JsonValueKind.String
                    ? NormalizeArgumentsJson(arguments.GetString())
                    : arguments.GetRawText();
            }

            return new RuntimeLlmToolCall(Guid.NewGuid().ToString("N"), name.Trim(), argumentsJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool LooksLikeTextToolCallPayload(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        return trimmed.Contains(TextToolCallStart, StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("{", StringComparison.Ordinal) && TryParseTextToolCall(trimmed) is not null;
    }

    private static string ExtractTextToolCallPayload(string text)
    {
        string trimmed = text.Trim();
        int start = trimmed.IndexOf(TextToolCallStart, StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start += TextToolCallStart.Length;
            int end = trimmed.IndexOf(TextToolCallEnd, start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                return string.Empty;
            }

            return trimmed[start..end].Trim();
        }

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            string[] lines = trimmed.Split('\n');
            if (lines.Length >= 3)
            {
                trimmed = string.Join('\n', lines.Skip(1).Take(lines.Length - 2)).Trim();
            }
        }

        return trimmed.StartsWith("{", StringComparison.Ordinal) ? trimmed : string.Empty;
    }

    private static string GetOptionalString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string NormalizeArgumentsJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "{}";
        }

        string trimmed = value.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal)
            || trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return JsonSerializer.Serialize(new { value = trimmed });
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

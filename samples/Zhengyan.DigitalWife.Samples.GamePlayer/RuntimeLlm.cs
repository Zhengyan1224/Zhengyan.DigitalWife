using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Llm;
using Zhengyan.DigitalWife.Llm.OpenAI;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed class RuntimeLlm : IDisposable
{
    private readonly GameProjectLlmSettings _settings;
    private readonly MainThreadDispatcher _dispatcher;
    private readonly Action<RuntimeEntity, RuntimeLlmScriptEvent> _dispatchScriptEvent;
    private readonly object _sync = new();
    private readonly List<CancellationTokenSource> _activeRequests = [];
    private OpenAiCompatibleLlmClient? _client;
    private bool _disposed;

    internal RuntimeLlm(
        GameProjectLlmSettings settings,
        MainThreadDispatcher dispatcher,
        Action<RuntimeEntity, RuntimeLlmScriptEvent> dispatchScriptEvent)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _dispatchScriptEvent = dispatchScriptEvent;
    }

    public bool Enabled => _settings.Enabled;

    public string Provider => _settings.Provider;

    public string BaseUrl => _settings.BaseUrl;

    public string Model => _settings.Model;

    public string ChatCompletionsPath => _settings.ChatCompletionsPath;

    public float? DefaultTemperature => _settings.DefaultTemperature;

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
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callbackTarget);

        string resolvedRequestId = string.IsNullOrWhiteSpace(requestId)
            ? Guid.NewGuid().ToString("N")
            : requestId.Trim();
        List<RuntimeLlmChatMessage> capturedMessages = messages.ToList();
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
                    await foreach (RuntimeLlmStreamUpdate update in StreamChatAsync(capturedMessages, model, temperature, cts.Token))
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
                                    onDeltaCallback ?? string.Empty));
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
                                onCompletedCallback ?? string.Empty));
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
                                onErrorCallback ?? string.Empty));
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
                        onErrorCallback ?? string.Empty));
            });
        }

        return resolvedRequestId;
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_settings.Enabled)
        {
            throw new InvalidOperationException("Project LLM is disabled. Enable Project.Llm.Enabled in GameEditor or game.project.json.");
        }

        string resolvedModel = ResolveModel(model);
        List<LlmChatMessage> requestMessages = messages
            .Where(message => !string.IsNullOrWhiteSpace(message.Role) && !string.IsNullOrWhiteSpace(message.Content))
            .Select(message => new LlmChatMessage(message.Role.Trim(), message.Content))
            .ToList();

        if (requestMessages.Count == 0)
        {
            throw new ArgumentException("At least one LLM message is required.", nameof(messages));
        }

        await foreach (LlmStreamUpdate update in GetClient().StreamChatAsync(
            requestMessages,
            new LlmRequestOptions
            {
                Model = resolvedModel,
                Temperature = temperature ?? _settings.DefaultTemperature
            },
            cancellationToken))
        {
            yield return new RuntimeLlmStreamUpdate(update.Delta, update.AccumulatedText, update.IsFinal);
        }
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
        string model = !string.IsNullOrWhiteSpace(overrideModel)
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
}

public sealed record RuntimeLlmChatMessage(string Role, string Content);

public sealed record RuntimeLlmStreamUpdate(string Delta, string AccumulatedText, bool IsFinal);

public sealed record RuntimeLlmResult(string RequestId, string Text);

public sealed record RuntimeLlmScriptEvent(
    string RequestId,
    string EventName,
    string Delta,
    string AccumulatedText,
    bool IsFinal,
    string Error,
    string CallbackName);

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jularr.Web.Features.Ai;

namespace Jularr.Web.Infrastructure.Ai;

/// <summary>One line-oriented connection to <c>codex app-server</c> (JSONL over stdio).</summary>
public interface ICodexAppServerTransport : IAsyncDisposable
{
    Task WriteLineAsync(string line, CancellationToken cancellationToken);

    /// <summary>Returns null when the server closed the stream.</summary>
    Task<string?> ReadLineAsync(CancellationToken cancellationToken);
}

public interface ICodexAppServerLauncher
{
    Task<ICodexAppServerTransport> StartAsync(CancellationToken cancellationToken);
}

public sealed class CodexAppServerException(int code, string message) : Exception(message)
{
    public const int MethodNotFound = -32601;

    public int Code { get; } = code;

    public bool IsMethodNotFound => Code == MethodNotFound;
}

public sealed record CodexAppServerNotification(string Method, JsonElement Params);

/// <summary>
/// JSON-RPC client for the public Codex app-server v2 protocol (the <c>"jsonrpc"</c> header is
/// omitted on the wire). The connection starts lazily, is shared by all requests, and restarts after
/// the server exits. Method support is learned from responses: a method-not-found error marks the
/// method unsupported so callers fall back instead of guessing.
/// </summary>
public sealed class CodexAppServerClient(
    ICodexAppServerLauncher launcher,
    TimeProvider time,
    ILogger<CodexAppServerClient> logger) : IAsyncDisposable
{
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan InitializeTimeout = TimeSpan.FromSeconds(20);

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SemaphoreSlim connectGate = new(1, 1);
    private readonly ConcurrentDictionary<string, AiCapabilityState> methods = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> notificationsSeen = new(StringComparer.Ordinal);
    private Connection? connection;
    private DateTimeOffset? unavailableUntil;
    private string? lastError;

    public event Action<CodexAppServerNotification>? Notification;

    /// <summary>Raised when a connection ends; in-flight turns fail instead of waiting for a timeout.</summary>
    public event Action? Disconnected;

    public string? UserAgent { get; private set; }

    public bool? IsAvailable { get; private set; }

    public DateTimeOffset? DetectedAt { get; private set; }

    public string? LastError => lastError;

    public AiCapabilityState GetMethodState(string method) =>
        methods.TryGetValue(method, out var state) ? state : AiCapabilityState.Unknown;

    public bool HasSeenNotification(string method) => notificationsSeen.ContainsKey(method);

    /// <summary>Starts and initializes the server if needed; false when it is unavailable.</summary>
    public async Task<bool> TryConnectAsync(CancellationToken cancellationToken)
    {
        if (connection is { IsOpen: true })
        {
            return true;
        }

        if (unavailableUntil is { } until && time.GetUtcNow() < until)
        {
            return false;
        }

        await connectGate.WaitAsync(cancellationToken);
        try
        {
            if (connection is { IsOpen: true })
            {
                return true;
            }

            if (connection is not null)
            {
                await connection.DisposeAsync();
                connection = null;
            }

            var transport = await launcher.StartAsync(cancellationToken);
            var next = new Connection(this, transport);
            next.Start();

            try
            {
                var result = await next.RequestAsync(
                    "initialize",
                    new
                    {
                        clientInfo = new { name = "jularr", title = "Jularr", version = typeof(CodexAppServerClient).Assembly.GetName().Version?.ToString() ?? "0" },
                        capabilities = new { experimentalApi = false, requestAttestation = false }
                    },
                    InitializeTimeout,
                    cancellationToken);
                await next.NotifyAsync("initialized", new { }, cancellationToken);

                UserAgent = result.TryGetProperty("userAgent", out var agent) && agent.ValueKind == JsonValueKind.String
                    ? agent.GetString()
                    : null;
            }
            catch
            {
                await next.DisposeAsync();
                throw;
            }

            connection = next;
            IsAvailable = true;
            DetectedAt = time.GetUtcNow();
            unavailableUntil = null;
            lastError = null;
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            IsAvailable = false;
            DetectedAt = time.GetUtcNow();
            unavailableUntil = time.GetUtcNow() + RetryAfterFailure;
            lastError = AiErrorSanitizer.Sanitize(exception.Message);
            logger.LogInformation("Codex app-server is unavailable; using codex exec. {Reason}", lastError);
            return false;
        }
        finally
        {
            connectGate.Release();
        }
    }

    public async Task<JsonElement> RequestAsync(
        string method,
        object? parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!await TryConnectAsync(cancellationToken) || connection is not { } current)
        {
            throw new InvalidOperationException("Codex app-server is not available.");
        }

        try
        {
            var result = await current.RequestAsync(method, parameters, timeout, cancellationToken);
            methods[method] = AiCapabilityState.Supported;
            return result;
        }
        catch (CodexAppServerException exception) when (exception.IsMethodNotFound)
        {
            methods[method] = AiCapabilityState.Unsupported;
            throw;
        }
    }

    /// <summary>Drops the connection, e.g. after login/logout so the next request reads fresh auth.</summary>
    public async Task ResetAsync()
    {
        await connectGate.WaitAsync();
        try
        {
            if (connection is not null)
            {
                await connection.DisposeAsync();
                connection = null;
            }

            unavailableUntil = null;
        }
        finally
        {
            connectGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (connection is not null)
        {
            await connection.DisposeAsync();
            connection = null;
        }

        connectGate.Dispose();
    }

    private void RaiseDisconnected()
    {
        try
        {
            Disconnected?.Invoke();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Codex app-server disconnect handler failed.");
        }
    }

    private void Dispatch(CodexAppServerNotification notification)
    {
        notificationsSeen[notification.Method] = true;
        try
        {
            Notification?.Invoke(notification);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Codex app-server notification handler failed for {Method}.", notification.Method);
        }
    }

    private sealed class Connection(CodexAppServerClient owner, ICodexAppServerTransport transport) : IAsyncDisposable
    {
        private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
        private readonly SemaphoreSlim writeGate = new(1, 1);
        private readonly CancellationTokenSource stop = new();
        private long nextId;
        private Task? reader;
        private volatile bool open = true;

        public bool IsOpen => open;

        public void Start() => reader = Task.Run(ReadLoopAsync);

        public async Task<JsonElement> RequestAsync(
            string method,
            object? parameters,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (!open)
            {
                throw new InvalidOperationException("Codex app-server connection is closed.");
            }

            var id = Interlocked.Increment(ref nextId);
            var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[id] = completion;

            try
            {
                await WriteAsync(new { id, method, @params = parameters ?? new { } }, cancellationToken);
                return await completion.Task.WaitAsync(timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new InvalidOperationException($"Codex app-server did not answer {method} in time.");
            }
            finally
            {
                pending.TryRemove(id, out _);
            }
        }

        public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken) =>
            WriteAsync(new { method, @params = parameters ?? new { } }, cancellationToken);

        private async Task WriteAsync(object message, CancellationToken cancellationToken)
        {
            var line = JsonSerializer.Serialize(message, JsonOptions);
            await writeGate.WaitAsync(cancellationToken);
            try
            {
                await transport.WriteLineAsync(line, cancellationToken);
            }
            finally
            {
                writeGate.Release();
            }
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var line = await transport.ReadLineAsync(stop.Token);
                    if (line is null)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    await HandleLineAsync(line);
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
            {
            }
            finally
            {
                open = false;
                foreach (var entry in pending)
                {
                    entry.Value.TrySetException(new InvalidOperationException("Codex app-server stopped."));
                }

                owner.RaiseDisconnected();
            }
        }

        private async Task HandleLineAsync(string line)
        {
            JsonElement root;
            try
            {
                using var document = JsonDocument.Parse(line);
                root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var hasMethod = root.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String;
            var hasId = root.TryGetProperty("id", out var id);

            if (hasMethod && hasId)
            {
                // Server-initiated requests (approvals, elicitations) are never granted: Jularr jobs are
                // least-privilege and run with approvals disabled, so any such request is declined.
                try
                {
                    await WriteAsync(
                        new { id, error = new { code = CodexAppServerException.MethodNotFound, message = "Not supported by Jularr." } },
                        stop.Token);
                }
                catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
                {
                }

                return;
            }

            if (hasMethod)
            {
                owner.Dispatch(new CodexAppServerNotification(
                    method.GetString()!,
                    root.TryGetProperty("params", out var parameters) ? parameters : default));
                return;
            }

            if (!hasId || !id.TryGetInt64(out var requestId) || !pending.TryRemove(requestId, out var completion))
            {
                return;
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var code = error.TryGetProperty("code", out var codeValue) && codeValue.TryGetInt32(out var parsed)
                    ? parsed
                    : 0;
                var message = error.TryGetProperty("message", out var messageValue) && messageValue.ValueKind == JsonValueKind.String
                    ? messageValue.GetString()
                    : null;
                completion.TrySetException(new CodexAppServerException(
                    code,
                    AiErrorSanitizer.Sanitize(message) ?? "Codex app-server request failed."));
                return;
            }

            completion.TrySetResult(root.TryGetProperty("result", out var result) ? result : default);
        }

        public async ValueTask DisposeAsync()
        {
            open = false;
            await stop.CancelAsync();
            await transport.DisposeAsync();
            if (reader is not null)
            {
                try
                {
                    await reader.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (TimeoutException)
                {
                }
            }

            stop.Dispose();
            writeGate.Dispose();
        }
    }
}

/// <summary>Starts <c>codex app-server</c> over stdio with the server's shared CODEX_HOME.</summary>
public sealed class CodexAppServerProcessLauncher : ICodexAppServerLauncher
{
    public Task<ICodexAppServerTransport> StartAsync(CancellationToken cancellationToken)
    {
        var startInfo = CodexCliProvider.CreateStartInfo(["app-server"]);
        startInfo.RedirectStandardInput = true;

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Codex app-server could not be started.");
        }

        return Task.FromResult<ICodexAppServerTransport>(new ProcessTransport(process));
    }

    private sealed class ProcessTransport : ICodexAppServerTransport
    {
        private readonly Process process;
        private readonly Task stderrDrain;

        public ProcessTransport(Process process)
        {
            this.process = process;
            process.StandardInput.AutoFlush = true;
            // Diagnostics on stderr may contain account details; drain without keeping them.
            stderrDrain = Task.Run(async () =>
            {
                try
                {
                    while (await process.StandardError.ReadLineAsync() is not null)
                    {
                    }
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
                {
                }
            });
        }

        public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
        }

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            await process.StandardOutput.ReadLineAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.StandardInput.Close();
                    if (!process.WaitForExit(TimeSpan.FromSeconds(2)))
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
            {
            }

            try
            {
                await stderrDrain.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (TimeoutException)
            {
            }

            process.Dispose();
        }
    }
}

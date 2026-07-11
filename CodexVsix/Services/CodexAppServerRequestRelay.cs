using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CodexVsix.Services;

/// <summary>
/// Relays JSON-RPC server requests into the official Codex webview and matches
/// the response produced by its native approval and interactive-input UI.
/// </summary>
internal sealed class CodexAppServerRequestRelay : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, TaskCompletionSource<JObject?>> _pending = new(StringComparer.Ordinal);
    private readonly Action<JObject> _postMessage;
    private bool _disposed;

    public CodexAppServerRequestRelay(Action<JObject> postMessage)
    {
        _postMessage = postMessage ?? throw new ArgumentNullException(nameof(postMessage));
    }

    public Task<JObject?> ForwardAsync(JObject request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var id = request["id"];
        if (id is null)
        {
            return Task.FromResult<JObject?>(null);
        }

        var key = CreateKey(id);
        var completion = new TaskCompletionSource<JObject?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return Task.FromResult<JObject?>(null);
            }

            if (_pending.ContainsKey(key))
            {
                throw new InvalidOperationException("An app-server request with the same id is already pending.");
            }

            _pending.Add(key, completion);
        }

        try
        {
            _postMessage(new JObject
            {
                ["type"] = "mcp-request",
                ["hostId"] = "local",
                ["request"] = request.DeepClone()
            });
        }
        catch
        {
            lock (_syncRoot)
            {
                _pending.Remove(key);
            }

            throw;
        }

        return completion.Task;
    }

    public bool TryHandleResponse(JObject message)
    {
        var response = message["response"] as JObject ?? message["message"] as JObject;
        var id = response?["id"];
        if (response is null || id is null)
        {
            return false;
        }

        TaskCompletionSource<JObject?>? completion;
        lock (_syncRoot)
        {
            var key = CreateKey(id);
            if (!_pending.TryGetValue(key, out completion))
            {
                return false;
            }

            _pending.Remove(key);
        }

        completion.TrySetResult((JObject)response.DeepClone());
        return true;
    }

    public void CancelPending()
    {
        TaskCompletionSource<JObject?>[] pending;
        lock (_syncRoot)
        {
            pending = new TaskCompletionSource<JObject?>[_pending.Count];
            _pending.Values.CopyTo(pending, 0);
            _pending.Clear();
        }

        foreach (var completion in pending)
        {
            completion.TrySetResult(null);
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        CancelPending();
    }

    private static string CreateKey(JToken id)
    {
        return id.ToString(Formatting.None);
    }
}

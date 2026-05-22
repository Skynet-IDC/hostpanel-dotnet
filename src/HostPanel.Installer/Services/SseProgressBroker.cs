using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HostPanel.Installer.Services;

/// <summary>
/// One writer (the InstallOrchestrator), many readers (browser SSE clients on
/// /api/install/events). Each broadcast is also retained in a ring buffer so
/// a late-connecting client can replay everything that happened before it
/// subscribed — important because the operator may reload the progress page
/// mid-install.
/// </summary>
public class SseProgressBroker
{
    private readonly ConcurrentDictionary<Guid, Channel> _subscribers = new();
    private readonly List<InstallProgressEvent> _history = new();
    private readonly object _historyLock = new();
    private const int HistoryCap = 2000;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public void Broadcast(InstallProgressEvent ev)
    {
        lock (_historyLock)
        {
            _history.Add(ev);
            if (_history.Count > HistoryCap) _history.RemoveAt(0);
        }
        foreach (var sub in _subscribers.Values)
        {
            sub.TryWrite(ev);
        }
    }

    public IDisposable Subscribe(Func<InstallProgressEvent, Task> onEvent, out IReadOnlyList<InstallProgressEvent> backlog)
    {
        lock (_historyLock)
        {
            backlog = _history.ToArray();
        }
        var id = Guid.NewGuid();
        var ch = new Channel(onEvent);
        _subscribers[id] = ch;
        return new Subscription(() => _subscribers.TryRemove(id, out _));
    }

    public static string SerializeForSse(InstallProgressEvent ev) =>
        "data: " + JsonSerializer.Serialize(ev, JsonOpts) + "\n\n";

    private class Channel
    {
        private readonly Func<InstallProgressEvent, Task> _on;
        public Channel(Func<InstallProgressEvent, Task> on) => _on = on;
        public void TryWrite(InstallProgressEvent ev) { _ = _on(ev); }
    }

    private class Subscription : IDisposable
    {
        private readonly Action _onDispose;
        public Subscription(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}

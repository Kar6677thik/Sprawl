using System.Threading.Channels;
using Sprawl.Contracts;

namespace Sprawl.Api.Events;

/// <summary>
/// In-process fan-out of committed batches to SSE subscribers (one replica, so no broker). A slow subscriber loses its
/// oldest events rather than blocking writers; the UI detects the version gap and refetches.
/// </summary>
public sealed class CanvasEventHub
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, HashSet<Channel<CanvasEvent>>> subscribers = new(StringComparer.Ordinal);

    public Subscription Subscribe(string canvasId)
    {
        var channel = Channel.CreateBounded<CanvasEvent>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        lock (gate)
        {
            if (!subscribers.TryGetValue(canvasId, out var set)) subscribers[canvasId] = set = [];
            set.Add(channel);
        }
        return new Subscription(this, canvasId, channel);
    }

    public void Publish(string canvasId, CanvasEvent canvasEvent)
    {
        Channel<CanvasEvent>[] targets;
        lock (gate) targets = subscribers.TryGetValue(canvasId, out var set) ? [.. set] : [];
        foreach (var channel in targets) channel.Writer.TryWrite(canvasEvent);
    }

    public int SubscriberCount(string canvasId)
    {
        lock (gate) return subscribers.TryGetValue(canvasId, out var set) ? set.Count : 0;
    }

    private void Remove(string canvasId, Channel<CanvasEvent> channel)
    {
        lock (gate)
        {
            if (!subscribers.TryGetValue(canvasId, out var set)) return;
            set.Remove(channel);
            if (set.Count == 0) subscribers.Remove(canvasId);
        }
        channel.Writer.TryComplete();
    }

    public sealed class Subscription(CanvasEventHub hub, string canvasId, Channel<CanvasEvent> channel) : IDisposable
    {
        public ChannelReader<CanvasEvent> Reader => channel.Reader;
        public void Dispose() => hub.Remove(canvasId, channel);
    }
}

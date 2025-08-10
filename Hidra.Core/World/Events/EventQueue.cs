// Hidra.Core/World/Events/EventQueue.cs
namespace Hidra.Core;

using System;
using System.Collections.Generic;
using System.Linq;
using Hidra.Core.Logging;

/// <summary>
/// A thread-safe priority queue for managing scheduled simulation events.
/// </summary>
/// <remarks>
/// This queue is optimized for high-performance, concurrent writes from multiple threads
/// and efficient, single-threaded processing of due events. It uses a min-heap
/// (<see cref="PriorityQueue{TElement, TPriority}"/>) for efficient sorting by execution tick.
/// </remarks>
public class EventQueue
{
    private readonly PriorityQueue<Event, ulong> _queue = new();
    private readonly object _queueLock = new();

    /// <summary>
    /// Thread-safely adds a new event to the queue.
    /// </summary>
    /// <param name="newEvent">The event to schedule.</param>
    /// <remarks>
    /// Enqueue is an O(log n) operation, and the lock is held for a minimal time.
    /// </remarks>
    public void Push(Event newEvent)
    {
        lock (_queueLock)
        {
            _queue.Enqueue(newEvent, newEvent.ExecutionTick);
            Logger.Log("EVENT_QUEUE", LogLevel.Debug, $"Pushed event {newEvent.Type} for target {newEvent.TargetId} at tick {newEvent.ExecutionTick}.");
        }
    }

    /// <summary>
    /// Dequeues and processes all events that are due on the specified tick.
    /// </summary>
    /// <param name="currentTick">The current simulation tick. All events scheduled for this exact tick will be processed.</param>
    /// <param name="processAction">The action to perform on each due event.</param>
    /// <remarks>
    /// <para>
    /// This method itself is not internally thread-safe and should only be called from a single,
    /// main simulation thread to prevent race conditions in event processing.
    /// It drains all due events into a temporary list, releasing the lock before invoking the
    /// <paramref name="processAction"/>. This minimizes lock contention.
    /// </para>
    /// <para>
    /// Events are processed in a deterministic order by sorting them by their unique ID.
    /// This implementation strictly processes events for the exact <paramref name="currentTick"/>,
    /// ignoring any future or potentially late events to ensure predictable behavior within a single simulation step.
    /// </para>
    /// </remarks>
    public void ProcessDueEvents(ulong currentTick, Action<Event> processAction)
    {
        var eventsToProcess = new List<Event>();
        
        lock (_queueLock)
        {
            // Peek is O(1), Dequeue is O(log n). This loop efficiently drains all due events for the current tick.
            while (_queue.TryPeek(out _, out var executionTick) && executionTick == currentTick)
            {
                eventsToProcess.Add(_queue.Dequeue());
            }
        }
        
        if (eventsToProcess.Count > 0)
        {
            Logger.Log("EVENT_QUEUE", LogLevel.Info, $"Processing {eventsToProcess.Count} due events for tick {currentTick}.");
        }

        // Process the collected events outside the lock.
        // Invariant: Since all events are for the same tick, we only need to sort by ID for determinism.
        foreach (var evt in eventsToProcess.OrderBy(e => e.Id))
        {
            processAction(evt);
        }
    }
}
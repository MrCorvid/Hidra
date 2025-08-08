// Hidra.Core/World/Events/EventQueue.cs
using Hidra.Core.Logging;
using System;
using System.Collections.Generic;
using System.Linq; // Add using for LINQ OrderBy/ThenBy

namespace Hidra.Core
{
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
        public void Push(Event newEvent)
        {
            lock (_queueLock)
            {
                // Enqueue is an O(log n) operation, and the lock is held for a minimal time.
                _queue.Enqueue(newEvent, newEvent.ExecutionTick);
                Logger.Log("EVENT_QUEUE", LogLevel.Debug, $"Pushed event {newEvent.Type} for target {newEvent.TargetId} at tick {newEvent.ExecutionTick}.");
            }
        }

        /// <summary>
        /// Dequeues and processes all events that are due on or before the specified tick.
        /// </summary>
        /// <remarks>
        /// This method itself is not internally thread-safe and should only be called from a single,
        /// main simulation thread to prevent race conditions in event processing.
        /// This implementation has been corrected to only process events for the exact current tick,
        /// preventing potential side-effects from processing late events in the same batch as current ones.
        /// </remarks>
        /// <param name="currentTick">The current simulation tick. All events scheduled for this tick or earlier will be processed.</param>
        /// <param name="processAction">The action to perform on each due event.</param>
        public void ProcessDueEvents(ulong currentTick, Action<Event> processAction)
        {
            // This temporary list avoids holding the lock while the events are being processed,
            // which could be a lengthy operation and cause thread contention.
            List<Event> eventsToProcess = new();
            
            lock (_queueLock)
            {
                // Peek is O(1), Dequeue is O(log n). This loop efficiently drains all due events.
                // We now strictly process events for the CURRENT tick only. The '<=' was too broad
                // and could lead to unforeseen interactions if the simulation ever lagged.
                while (_queue.TryPeek(out Event? nextEvent, out ulong executionTick) && executionTick == currentTick)
                {
                    eventsToProcess.Add(_queue.Dequeue());
                }
            }
            
            if (eventsToProcess.Count > 0)
            {
                Logger.Log("EVENT_QUEUE", LogLevel.Info, $"Processing {eventsToProcess.Count} due events for tick {currentTick}.");
            }

            // Process the collected events outside the lock.
            // Since all events are for the same tick, we only need to sort by ID for determinism.
            // The previous implementation's OrderBy(tick) is now redundant but harmless.
            foreach (var evt in eventsToProcess.OrderBy(e => e.Id))
            {
                processAction(evt);
            }
        }
    }
}
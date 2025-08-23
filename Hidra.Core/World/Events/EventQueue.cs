// Hidra.Core/World/Events/EventQueue.cs
using System;
using System.Collections.Generic;
using System.Linq;

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
                _queue.Enqueue(newEvent, newEvent.ExecutionTick);
            }
        }

        /// <summary>
        /// Retrieves a list of all events scheduled for a specific tick without removing them.
        /// </summary>
        /// <param name="tick">The simulation tick to inspect.</param>
        /// <returns>A list of events scheduled for the specified tick.</returns>
        /// <remarks>
        /// This operation can be slow on large queues as it iterates through all unordered items.
        /// It is intended primarily for diagnostic and debugging purposes.
        /// </remarks>
        public List<Event> PeekEventsForTick(ulong tick)
        {
            lock (_queueLock)
            {
                return _queue.UnorderedItems
                             .Where(item => item.Element.ExecutionTick == tick)
                             .Select(item => item.Element)
                             .ToList();
            }
        }
        
        /// <summary>
        /// Dequeues all events due on or before the current tick and partitions them
        /// into two lists based on their type. This is a highly efficient, single-pass
        /// operation designed for the core simulation loop.
        /// </summary>
        /// <param name="currentTick">The current simulation tick.</param>
        /// <param name="pulseEvents">The list to populate with PotentialPulse events.</param>
        /// <param name="otherEvents">The list to populate with all other event types.</param>
        public void ProcessAndPartionDueEvents(ulong currentTick, List<Event> pulseEvents, List<Event> otherEvents)
        {
            lock (_queueLock)
            {
                // This loop efficiently drains all due events.
                while (_queue.Count > 0 && _queue.Peek().ExecutionTick <= currentTick)
                {
                    var e = _queue.Dequeue();
                    if (e.Type == EventType.PotentialPulse)
                    {
                        pulseEvents.Add(e);
                    }
                    else
                    {
                        otherEvents.Add(e);
                    }
                }
            }
        }
    }
}
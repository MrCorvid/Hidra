// Hidra.Tests/Core/EventQueueTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hidra.Tests.Core
{
    [TestClass]
    public class EventQueueTests
    {
        private EventQueue _eventQueue = null!;
        private List<Event> _processedPulses = null!;
        private List<Event> _processedOthers = null!;

        [TestInitialize]
        public void Init()
        {
            _eventQueue = new EventQueue();
            _processedPulses = new List<Event>();
            _processedOthers = new List<Event>();
        }

        private Event CreateEvent(ulong id, ulong executionTick, EventType type = EventType.Activate)
        {
            // Use the new strongly-typed payload
            var payload = new EventPayload();
            return new Event { Id = id, ExecutionTick = executionTick, Type = type, Payload = payload };
        }
        
        #region Core Logic and Ordering Tests

        [TestMethod]
        public void ProcessAndPartionDueEvents_OnEmptyQueue_DoesNotAddEvents()
        {
            _eventQueue.ProcessAndPartionDueEvents(10UL, _processedPulses, _processedOthers);
            Assert.AreEqual(0, _processedPulses.Count);
            Assert.AreEqual(0, _processedOthers.Count);
        }

        [TestMethod]
        public void ProcessAndPartionDueEvents_WithOnlyDueEvents_PartitionsCorrectly()
        {
            const ulong currentTick = 100;
            _eventQueue.Push(CreateEvent(id: 3, executionTick: currentTick, type: EventType.PotentialPulse));
            _eventQueue.Push(CreateEvent(id: 1, executionTick: currentTick, type: EventType.Activate));
            _eventQueue.Push(CreateEvent(id: 2, executionTick: currentTick, type: EventType.PotentialPulse));

            _eventQueue.ProcessAndPartionDueEvents(currentTick, _processedPulses, _processedOthers);

            Assert.AreEqual(2, _processedPulses.Count);
            Assert.AreEqual(1, _processedOthers.Count);
            CollectionAssert.AreEquivalent(new ulong[] { 2, 3 }, _processedPulses.Select(e => e.Id).ToList());
            Assert.AreEqual(1UL, _processedOthers[0].Id);
        }

        [TestMethod]
        public void ProcessAndPartionDueEvents_WithOnlyFutureEvents_DoesNotProcessAny()
        {
            const ulong currentTick = 50;
            _eventQueue.Push(CreateEvent(id: 1, executionTick: 51));
            _eventQueue.Push(CreateEvent(id: 2, executionTick: 100));

            _eventQueue.ProcessAndPartionDueEvents(currentTick, _processedPulses, _processedOthers);

            Assert.AreEqual(0, _processedPulses.Count + _processedOthers.Count);

            _eventQueue.ProcessAndPartionDueEvents(51, _processedPulses, _processedOthers);
            Assert.AreEqual(1, _processedOthers.Count);
            Assert.AreEqual(1UL, _processedOthers[0].Id);
        }
        
        [TestMethod]
        public void ProcessAndPartionDueEvents_WithMixedEvents_ProcessesLateAndDueAndLeavesFuture()
        {
            const ulong currentTick = 100;
            _eventQueue.Push(CreateEvent(id: 10, executionTick: 101, type: EventType.Activate)); // Future
            _eventQueue.Push(CreateEvent(id: 6, executionTick: 100, type: EventType.PotentialPulse)); // Due
            _eventQueue.Push(CreateEvent(id: 5, executionTick: 100, type: EventType.Activate)); // Due
            _eventQueue.Push(CreateEvent(id: 2, executionTick: 50, type: EventType.PotentialPulse)); // Late

            _eventQueue.ProcessAndPartionDueEvents(currentTick, _processedPulses, _processedOthers);

            Assert.AreEqual(2, _processedPulses.Count);
            Assert.AreEqual(1, _processedOthers.Count);

            // Check that the queue now only contains the future event
            _processedPulses.Clear();
            _processedOthers.Clear();
            _eventQueue.ProcessAndPartionDueEvents(100, _processedPulses, _processedOthers);
            Assert.AreEqual(0, _processedPulses.Count + _processedOthers.Count, "Queue should be empty for the current tick.");
        }

        #endregion

        #region Concurrency Tests

        [TestMethod]
        public async Task Push_FromMultipleThreads_AllEventsAreQueuedSuccessfully()
        {
            const int eventCount = 1000;
            var tasks = new List<Task>();
            long nextEventId = 0;

            for (int i = 0; i < eventCount; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    ulong newId = (ulong)Interlocked.Increment(ref nextEventId);
                    _eventQueue.Push(CreateEvent(newId, executionTick: 500));
                }));
            }
            await Task.WhenAll(tasks);

            _eventQueue.ProcessAndPartionDueEvents(500, _processedPulses, _processedOthers);
            Assert.AreEqual(eventCount, _processedOthers.Count, "The number of processed events must match the number of events pushed concurrently.");
        }
        
        #endregion
    }
}
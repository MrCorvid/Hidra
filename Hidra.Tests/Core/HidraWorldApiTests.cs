// Hidra.Tests/Core/HidraWorldApiTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using Hidra.Core.Brain;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System;
using Hidra.Core.Logging;
using System.IO;
using System.Reflection;

namespace Hidra.Tests.Core
{
    [TestClass]
    public class HidraWorldApiTests : BaseTestClass
    {
        private HidraConfig _config = null!;
        private HidraWorld _world = null!;
        private string _tempTestDirectory = null!;

        // System gene constants referenced in the SUT
        private const uint SYS_GENE_GESTATION = 1;

        [TestInitialize]
        public override void BaseInit()
        {
            base.BaseInit();
            _config = TestDefaults.DeterministicConfig();
            _world = CreateWorld(_config);

            _tempTestDirectory = Path.Combine(Path.GetTempPath(), "HidraApiTest_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempTestDirectory);
        }

        [TestCleanup]
        public override void BaseCleanup()
        {
            base.BaseCleanup();
            if (Directory.Exists(_tempTestDirectory))
            {
                try
                {
                    Directory.Delete(_tempTestDirectory, true);
                }
                catch (IOException)
                {
                    // Handle cases where the directory might be locked by a process
                }
            }
        }

        #region Getters (Thread-Safe Reads)

        [TestMethod]
        public void GetNeuronById_WhenExists_ReturnsNeuron()
        {
            var addedNeuron = _world.AddNeuron(Vector3.Zero);
            var foundNeuron = _world.GetNeuronById(addedNeuron.Id);
            Assert.IsNotNull(foundNeuron);
            Assert.AreEqual(addedNeuron.Id, foundNeuron.Id);
        }

        #endregion

        #region Metrics API

        [TestMethod]
        public void GetRecentSnapshots_WhenMetricsEnabled_ReturnsCorrectSnapshots()
        {
            _config.MetricsEnabled = true;
            _config.MetricsRingCapacity = 10;
            _config.MetricsCollectionInterval = 1;
            _world = CreateWorld(_config);

            _world.AddNeuron(Vector3.Zero);

            // Step 1: Processes Tick 0, takes snapshot for Tick 0, advances world to Tick 1.
            _world.Step();
            // Step 2: Processes Tick 1, takes snapshot for Tick 1, advances world to Tick 2.
            _world.Step();

            var snapshots = _world.GetRecentSnapshots();
            Assert.AreEqual(2, snapshots.Count, "Should have collected two snapshots.");
            
            // The snapshots are for the ticks that were just COMPLETED.
            // After two steps, the world has completed Tick 0 and Tick 1.
            // The snapshots are returned most-recent first.
            Assert.AreEqual(1UL, snapshots[0].Tick, "First snapshot in list should be for the latest completed tick (Tick 1).");
            Assert.AreEqual(0UL, snapshots[1].Tick, "Second snapshot in list should be for the earlier completed tick (Tick 0).");
        }

        #endregion

        #region State Modifiers (Thread-Safe Writes)

        [TestMethod]
        public void AddNeuron_CorrectlyInitializesLVarsAndQueuesEvent()
        {
            // --- ARRANGE ---
            _config.InitialNeuronHealth = 80.0f;
            _config.InitialPotential = 0.1f;
            _world = CreateWorld(_config);
            var initialNeuronCount = _world.Neurons.Count;
            
            // --- ACT & ASSERT (INITIALIZATION) ---
            var newNeuron = _world.AddNeuron(Vector3.One); // CurrentTick = 0. Event is queued for tick 1.
            
            // Assert the neuron's state immediately after creation, before Step() can alter it.
            Assert.AreEqual(initialNeuronCount + 1, _world.Neurons.Count);
            AreClose(_config.InitialNeuronHealth, newNeuron.LocalVariables[(int)Hidra.Core.LVarIndex.Health]);
            AreClose(_config.InitialPotential, newNeuron.LocalVariables[(int)Hidra.Core.LVarIndex.SomaPotential]);

            // --- ACT & ASSERT (EVENT QUEUEING) ---
            _world.Step(); // Processes Tick 0, advances CurrentTick to 1
            Assert.AreEqual(1UL, _world.CurrentTick);

            // Check the scheduled events for the *next* tick (tick 1)
            var events = _world.GetEventsForTick(1);
            Assert.IsTrue(events.Any(e => e.Type == EventType.ExecuteGene &&
                                          e.Payload.GeneId == SYS_GENE_GESTATION &&
                                          e.TargetId == newNeuron.Id),
                "Gestation event should have been queued for the next tick (tick 1).");
        }

        [TestMethod]
        public void RemoveNeuron_RemovesNeuronAndConnectedSynapses()
        {
            var n1 = _world.AddNeuron(Vector3.Zero);
            var n2 = _world.AddNeuron(Vector3.One);
            _world.AddSynapse(n1.Id, n2.Id, SignalType.Immediate, 1f, 0f);

            Assert.AreEqual(2, _world.Neurons.Count);
            Assert.AreEqual(1, _world.Synapses.Count);

            bool result = _world.RemoveNeuron(n2.Id);

            Assert.IsTrue(result);
            Assert.AreEqual(1, _world.Neurons.Count, "Neuron n2 should have been removed.");
            Assert.AreEqual(0, _world.Synapses.Count, "The synapse connected to n2 should have been removed.");
        }

        #endregion
        
        #region Simulation Control

        [TestMethod]
        public void RunFor_StepsCorrectNumberOfTicks()
        {
            _world.RunFor(7);
            Assert.AreEqual(7UL, _world.CurrentTick);
        }

        [TestMethod]
        public void RunUntil_StopsWhenConditionIsMet()
        {
            _world.RunUntil(w => w.CurrentTick >= 4);
            Assert.AreEqual(4UL, _world.CurrentTick);
        }

        #endregion

        #region Concurrency Tests

        [TestMethod]
        public async Task AddNeuron_IsThreadSafe()
        {
            // --- ARRANGE ---
            const int taskCount = 100;
            var initialCount = _world.Neurons.Count;
            
            // --- ACT ---
            await RunConcurrent(taskCount, () => 
            {
                _world.AddNeuron(Vector3.Zero);
                return Task.CompletedTask;
            });

            // --- ASSERT ---
            Assert.AreEqual(initialCount + taskCount, _world.Neurons.Count, "The final neuron count should reflect all concurrent additions.");
        }
        
        [TestMethod]
        public async Task RemoveNeuron_IsThreadSafe()
        {
            // --- ARRANGE ---
            const int neuronCount = 100;
            var neuronIds = Enumerable.Range(0, neuronCount)
                                      .Select(_ => _world.AddNeuron(Vector3.Zero).Id)
                                      .ToList();
            
            var initialCount = _world.Neurons.Count;
            Assert.AreEqual(neuronCount, initialCount, "Test setup should have created correct number of neurons.");

            // --- ACT ---
            var removeTasks = neuronIds.Select(id => Task.Run(() => _world.RemoveNeuron(id)));
            await Task.WhenAll(removeTasks);

            // --- ASSERT ---
            Assert.AreEqual(0, _world.Neurons.Count, "All concurrently removed neurons should be gone.");
            foreach (var id in neuronIds)
            {
                Assert.IsNull(_world.GetNeuronById(id), $"Neuron with ID {id} should have been removed.");
            }
        }

        #endregion
    }
}
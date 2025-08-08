// Hidra.Tests/Core/HidraWorldApiTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using System.Numerics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace Hidra.Tests.Core
{
    /// <summary>
    /// Contains tests for the public API of the HidraWorld class. These tests verify
    /// the correctness and thread-safety of methods used for external manipulation of the world state.
    /// </summary>
    [TestClass]
    public class HidraWorldApiTests : BaseTestClass
    {
        private HidraConfig _config = null!;
        private const string MINIMAL_GENOME = "GN0000000000";

        [TestInitialize]
        public void TestInitialize()
        {
            _config = new HidraConfig();
        }

        #region Entity Creation Tests

        /// <summary>
        /// Verifies that AddNeuron correctly creates a neuron with default values
        /// derived from the HidraConfig and queues a Gestation gene execution event.
        /// </summary>
        [TestMethod]
        public void AddNeuron_InitializesWithDefaultConfigValues()
        {
            // Arrange
            _config.DefaultFiringThreshold = 1.5f;
            _config.InitialNeuronHealth = 50f;
            var world = new HidraWorld(_config, MINIMAL_GENOME);
            
            // The world starts with one neuron; we'll add another to test the API method specifically.
            var neuron = world.AddNeuron(Vector3.One);

            // Act & Assert
            Assert.IsNotNull(neuron);
            Assert.AreEqual(1.5f, neuron.LocalVariables[(int)LVarIndex.FiringThreshold]);
            Assert.AreEqual(50f, neuron.LocalVariables[(int)LVarIndex.Health]);
            Assert.IsTrue(neuron.IsActive);
            Assert.IsNotNull(neuron.Brain, "Neuron should be created with a default brain.");
        }

        /// <summary>
        /// Verifies that AddSynapse correctly creates a connection and returns null
        /// if either the source or target ID is invalid.
        /// </summary>
        [TestMethod]
        public void AddSynapse_WithValidIds_CreatesSynapse()
        {
            // Arrange
            var world = new HidraWorld(_config, MINIMAL_GENOME);
            var neuron1 = world.Neurons.Values.First();
            var neuron2 = world.AddNeuron(Vector3.One);
            world.AddInputNode(100);

            // Act
            var validSynapse1 = world.AddSynapse(neuron1.Id, neuron2.Id, SignalType.Delayed, 1f, 1f);
            var validSynapse2 = world.AddSynapse(100, neuron1.Id, SignalType.Immediate, 1f, 1f);
            var invalidSynapse = world.AddSynapse(999, neuron1.Id, SignalType.Delayed, 1f, 1f); // Source 999 does not exist.

            // Assert
            Assert.IsNotNull(validSynapse1);
            Assert.IsNotNull(validSynapse2);
            Assert.IsNull(invalidSynapse, "Synapse creation should fail for non-existent source ID.");
            Assert.AreEqual(2, world.Synapses.Count);
        }

        #endregion

        #region Thread-Safety Tests

        /// <summary>
        /// Verifies that AddNeuron can be called concurrently from multiple threads without
        /// corrupting the internal neuron collection or causing race conditions.
        /// </summary>
        [TestMethod]
        public void AddNeuron_WhenCalledConcurrently_IsThreadSafe()
        {
            // Arrange
            var world = new HidraWorld(_config, MINIMAL_GENOME);
            int initialCount = world.Neurons.Count;
            const int additions = 200;

            // Act
            Parallel.For(0, additions, i =>
            {
                world.AddNeuron(new Vector3(i, 0, 0));
            });

            // Assert
            Assert.AreEqual(initialCount + additions, world.Neurons.Count, "All neurons should be added safely.");
        }

        /// <summary>
        /// Verifies that AddSynapse can be called concurrently without issues. This is a critical
        /// test due to the list sorting that occurs on each addition.
        /// </summary>
        [TestMethod]
        public void AddSynapse_WhenCalledConcurrently_IsThreadSafe()
        {
            // Arrange
            var world = new HidraWorld(_config, MINIMAL_GENOME);
            var neuron1 = world.AddNeuron(Vector3.Zero);
            var neuron2 = world.AddNeuron(Vector3.One);
            const int additions = 200;

            // Act
            Parallel.For(0, additions, i =>
            {
                // Alternate source/target to increase chance of contention
                if (i % 2 == 0)
                {
                    world.AddSynapse(neuron1.Id, neuron2.Id, SignalType.Delayed, 1f, 1f);
                }
                else
                {
                    world.AddSynapse(neuron2.Id, neuron1.Id, SignalType.Immediate, 1f, 1f);
                }
            });

            // Assert
            Assert.AreEqual(additions, world.Synapses.Count, "All synapses should be added safely.");
            // Verify the list remains sorted, which is a key part of the internal logic.
            for (int i = 0; i < world.Synapses.Count - 1; i++)
            {
                Assert.IsTrue(world.Synapses[i].Id < world.Synapses[i+1].Id, "Synapse list must remain sorted by ID.");
            }
        }

        #endregion

        #region I/O Node Tests

        /// <summary>
        /// Verifies that SetInputValues correctly updates the values of multiple input nodes.
        /// </summary>
        [TestMethod]
        public void SetInputValues_UpdatesMultipleNodesCorrectly()
        {
            // Arrange
            var world = new HidraWorld(_config, MINIMAL_GENOME);
            world.AddInputNode(1);
            world.AddInputNode(2);
            world.AddInputNode(3);

            var newValues = new Dictionary<ulong, float>
            {
                { 1, 0.5f },
                { 3, -1.0f }
            };

            // Act
            world.SetInputValues(newValues);

            // Assert
            Assert.AreEqual(0.5f, world.GetInputNodeById(1)!.Value);
            Assert.AreEqual(0f, world.GetInputNodeById(2)!.Value, "Node 2 should remain at its default value.");
            Assert.AreEqual(-1.0f, world.GetInputNodeById(3)!.Value);
        }

        /// <summary>
        /// Verifies that GetOutputValues correctly retrieves the values of specified output nodes.
        /// </summary>
        [TestMethod]
        public void GetOutputValues_RetrievesMultipleNodesCorrectly()
        {
            // Arrange
            var world = new HidraWorld(_config, MINIMAL_GENOME);
            world.AddOutputNode(10);
            world.AddOutputNode(20);
            world.AddOutputNode(30);

            world.GetOutputNodeById(10)!.Value = 0.9f;
            world.GetOutputNodeById(30)!.Value = 0.4f;

            var idsToGet = new List<ulong> { 10, 30 };
            
            // Act
            var results = world.GetOutputValues(idsToGet);
            
            // Assert
            Assert.AreEqual(2, results.Count);
            Assert.AreEqual(0.9f, results[10]);
            Assert.AreEqual(0.4f, results[30]);
        }

        #endregion
    }
}
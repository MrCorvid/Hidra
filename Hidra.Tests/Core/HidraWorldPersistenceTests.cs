// Hidra.Tests/Core/HidraWorldPersistenceTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using System.Numerics;
using System.Linq;
using Hidra.Core.Brain;

namespace Hidra.Tests.Core
{
    /// <summary>
    /// Contains integration tests for the serialization and deserialization
    /// functionality of the HidraWorld.
    /// </summary>
    [TestClass]
    public class HidraWorldPersistenceTests : BaseTestClass
    {
        private const string TEST_GENOME = "GN0000000000";

        /// <summary>
        /// Verifies the entire save/load cycle, ensuring that a deserialized world
        /// is an accurate and functional replica of the original.
        /// </summary>
        [TestMethod]
        public void SaveAndLoad_RestoresWorldState_Accurately()
        {
            // --- ARRANGE ---
            // 1. Create a world with non-default state.
            var originalConfig = new HidraConfig { DefaultDecayRate = 0.8f, CompetitionRadius = 20f };
            var originalWorld = new HidraWorld(originalConfig, TEST_GENOME);

            // 2. Add complex entities.
            var neuron1 = originalWorld.Neurons.Values.First();
            neuron1.LocalVariables[(int)LVarIndex.Health] = 88.8f;
            neuron1.Position = new Vector3(10, 0, 0);

            var neuron2 = originalWorld.AddNeuron(new Vector3(15, 0, 0));
            neuron2.Brain = new NeuralNetworkBrain(); // Test polymorphic brain serialization.

            originalWorld.AddInputNode(101, 0.5f);
            originalWorld.AddOutputNode(202); // Corrected: No second argument
            var outputNode = originalWorld.GetOutputNodeById(202);
            if(outputNode != null) outputNode.Value = 0.25f;


            originalWorld.AddSynapse(101, neuron1.Id, SignalType.Immediate, 1.0f, 1.0f);
            originalWorld.AddSynapse(neuron1.Id, neuron2.Id, SignalType.Delayed, 0.9f, 0f);

            // 3. Advance the world to generate dynamic state.
            originalWorld.Step();
            originalWorld.Step();

            // --- ACT ---
            // 4. Perform the save/load cycle.
            string jsonState = originalWorld.SaveStateToJson();
            var loadedWorld = HidraWorld.LoadStateFromJson(jsonState, TEST_GENOME);

            // --- ASSERT ---
            // 5. Verify that the loaded world's state matches the original.

            // Config and Tick
            Assert.AreEqual(originalWorld.Config.DefaultDecayRate, loadedWorld.Config.DefaultDecayRate, "Config should be restored.");
            Assert.AreEqual(originalWorld.CurrentTick, loadedWorld.CurrentTick, "CurrentTick should be restored.");

            // Entity counts
            Assert.AreEqual(originalWorld.Neurons.Count, loadedWorld.Neurons.Count, "Neuron count must match.");
            Assert.AreEqual(originalWorld.Synapses.Count, loadedWorld.Synapses.Count, "Synapse count must match.");
            Assert.AreEqual(originalWorld.InputNodes.Count, loadedWorld.InputNodes.Count, "Input node count must match.");
            Assert.AreEqual(originalWorld.OutputNodes.Count, loadedWorld.OutputNodes.Count, "Output node count must match.");

            // Detailed Neuron state
            var loadedNeuron1 = loadedWorld.GetNeuronById(neuron1.Id)!;
            Assert.IsNotNull(loadedNeuron1, "Neuron 1 should exist in loaded world.");
            Assert.AreEqual(neuron1.Position, loadedNeuron1.Position, "Neuron position must be restored.");
            Assert.AreEqual(neuron1.LocalVariables[(int)LVarIndex.Health], loadedNeuron1.LocalVariables[(int)LVarIndex.Health], 1e-6f, "Neuron LVar (Health) must be restored.");
            Assert.AreEqual(neuron1.LocalVariables[(int)LVarIndex.Age], loadedNeuron1.LocalVariables[(int)LVarIndex.Age], "Neuron LVar (Age) must be restored.");

            // Brain polymorphism
            var loadedNeuron2 = loadedWorld.GetNeuronById(neuron2.Id)!;
            Assert.IsInstanceOfType(loadedNeuron2.Brain, typeof(NeuralNetworkBrain), "Polymorphic brain type should be restored.");

            // Synapse state (sorted deterministically on load)
            var loadedSynapse = loadedWorld.Synapses.First(s => s.SourceId == neuron1.Id && s.TargetId == neuron2.Id);
            Assert.AreEqual(0.9f, loadedSynapse.Weight, "Synapse properties must be restored.");

            // Verify non-serialized structures were rebuilt (functional test)
            // Corrected: Use GetNeighbors, not FindNeighbors
            var neighbors = loadedWorld.GetNeighbors(loadedNeuron1, 10f).ToList();
            Assert.AreEqual(1, neighbors.Count, "Spatial hash should be rebuilt and functional after load.");
            // Corrected: Simple assertion on the ID
            Assert.AreEqual(neuron2.Id, neighbors[0].Id);
        }
    }
}
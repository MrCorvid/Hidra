// Hidra.Tests/Core/HidraWorldTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System;
using System.Threading.Tasks;
using System.Reflection;

namespace Hidra.Tests.Core
{
    [TestClass]
    public class HidraWorldTests : BaseTestClass
    {
        private HidraConfig _config = null!;
        private const string MINIMAL_GENOME = "GN0000000000";
        private const string INVALID_GENOME_NO_GENESIS = "GN1111111111";

        [TestInitialize]
        public override void BaseInit()
        {
            base.BaseInit();
            _config = TestDefaults.DeterministicConfig();
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_WithMinimalGenome_InitializesCoreProperties()
        {
            // --- ARRANGE ---
            var inputIds = new ulong[] { 1, 10 };
            var outputIds = new ulong[] { 2, 20 };

            // --- ACT ---
            // Use the constructor that takes node IDs to test all initializations.
            var world = new HidraWorld(_config, MINIMAL_GENOME, inputIds, outputIds);
            _ownedWorlds.Add(world);

            // --- ASSERT ---
            Assert.AreEqual(0UL, world.CurrentTick);
            Assert.AreSame(_config, world.Config);
            Assert.AreEqual(256, world.GlobalHormones.Length);
            Assert.AreEqual(2, world.InputNodes.Count, "Should create correct number of input nodes.");
            Assert.IsTrue(world.InputNodes.ContainsKey(10));
            Assert.AreEqual(2, world.OutputNodes.Count, "Should create correct number of output nodes.");
            Assert.IsTrue(world.OutputNodes.ContainsKey(20));
        }
        
        [TestMethod]
        public void Constructor_WithNonCreatingGenesisGene_StartsNeuronless()
        {
            // --- ARRANGE ---
            // This test verifies that if the genome's Genesis gene doesn't create a neuron,
            // the world correctly starts with zero neurons. The core is not responsible
            // for creating a default "progenitor" neuron.

            // --- ACT ---
            var world = CreateWorld(_config, MINIMAL_GENOME);

            // --- ASSERT ---
            Assert.AreEqual(0, world.Neurons.Count, "World should have no neurons if the genesis gene creates none.");
        }

        #endregion

        #region State and Metrics Tests

        [TestMethod]
        public void InitMetrics_WhenMetricsDisabled_DoesNotAllocateRingBuffer()
        {
            // --- ARRANGE ---
            _config.MetricsEnabled = false;

            // --- ACT ---
            var world = CreateWorld(_config, MINIMAL_GENOME);

            // --- ASSERT ---
            var ringField = typeof(HidraWorld).GetField("_metricsRing", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(ringField, "_metricsRing field should exist.");
            var ringValue = ringField.GetValue(world);
            Assert.IsNull(ringValue, "Metrics ring buffer should be null when metrics are disabled.");
        }

        [TestMethod]
        public void ComputeTickMetrics_WithMultipleNeurons_CalculatesCorrectAverages()
        {
            // --- ARRANGE ---
            var world = CreateWorld(_config, MINIMAL_GENOME);
            
            // Access the private neuron collection via reflection to set up the test state.
            var neuronsField = typeof(HidraWorld).GetField("_neurons", BindingFlags.NonPublic | BindingFlags.Instance);
            var neurons = (IDictionary<ulong, Neuron>)neuronsField!.GetValue(world)!;
            neurons.Clear(); // Clear the default neuron

            var n1 = new Neuron { Id = 1, IsActive = true, LocalVariables = new float[256] };
            n1.LocalVariables[240] = 0.1f; // FiringRate
            n1.LocalVariables[241] = 0.2f; // DendriticPotential
            n1.LocalVariables[242] = 0.3f; // SomaPotential
            n1.LocalVariables[243] = 1.0f; // Health
            neurons.Add(n1.Id, n1);

            var n2 = new Neuron { Id = 2, IsActive = false, LocalVariables = new float[256] };
            n2.LocalVariables[240] = 0.5f; 
            n2.LocalVariables[241] = 0.6f;
            n2.LocalVariables[242] = 0.7f;
            n2.LocalVariables[243] = 0.5f;
            neurons.Add(n2.Id, n2);

            // --- ACT ---
            var metricsMethod = typeof(HidraWorld).GetMethod("ComputeTickMetrics", BindingFlags.NonPublic | BindingFlags.Instance);
            var result = (TickMetrics)metricsMethod!.Invoke(world, null)!;

            // --- ASSERT ---
            Assert.AreEqual(2, result.NeuronCount);
            Assert.AreEqual(1, result.ActiveNeuronCount);
            AreClose(0.3f, result.MeanFiringRate);         // (0.1 + 0.5) / 2
            AreClose(0.4f, result.MeanDendriticPotential); // (0.2 + 0.6) / 2
            AreClose(0.5f, result.MeanSomaPotential);      // (0.3 + 0.7) / 2
            AreClose(0.75f, result.MeanHealth);            // (1.0 + 0.5) / 2
        }

        #endregion
    }
}
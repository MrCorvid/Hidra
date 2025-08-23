// Hidra.Tests/Core/WorldDataTypesTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using Hidra.Core.Brain;
using Hidra.Core.Logging; // Required for LogLevel
using System.Numerics;
using System.Collections.Generic;
using System;

namespace Hidra.Tests.Core
{
    [TestClass]
    public class WorldDataTypesTests : BaseTestClass
    {
        private HidraConfig _config = null!;

        [TestInitialize]
        public override void BaseInit()
        {
            base.BaseInit();
            _config = TestDefaults.DeterministicConfig();
        }

        #region Test Helper Mocks

        /// <summary>
        /// A test-local mock for IPrng, needed for testing the IBrain interface.
        /// </summary>
        internal sealed class MockPrng : IPrng
        {
            public ulong NextULong() => 1UL;
            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
            public float NextFloat() => 0.5f;
            public double NextDouble() => 0.5;
            public void GetState(out ulong s0, out ulong s1) { s0 = 1; s1 = 2; }
            public void SetState(ulong s0, ulong s1) { /* no-op */ }
        }

        /// <summary>
        /// Test-local mock for IBrain that implements the full interface, including SetPrng.
        /// </summary>
        internal sealed class MockBrainWithPrng : IBrain
        {
            public IReadOnlyList<BrainInput> InputMap { get; set; } = Array.Empty<BrainInput>();
            public IReadOnlyList<BrainOutput> OutputMap { get; set; } = Array.Empty<BrainOutput>();
            public bool CanLearn => false;
            public IPrng? Prng { get; private set; }
            
            public void Evaluate(float[] inputs, Action<string, LogLevel, string>? logAction = null) 
            {
                // The mock doesn't need to do anything, but the signature must match.
            }
            
            public void Mutate(float rate) { }
            public void Reset() { }
            
            public void SetPrng(IPrng prng)
            {
                Prng = prng;
            }

            // FIX: Added the missing interface member implementation.
            public void InitializeFromLoad() { /* no-op for mock */ }
        }

        #endregion

        #region ConditionContext Tests

        [TestMethod]
        public void Constructor_WithValidArguments_AssignsAllPropertiesCorrectly()
        {
            // --- ARRANGE ---
            var world = CreateWorld(_config);
            var sourceNeuron = new Neuron { Id = 1 };
            var sourceInput = new InputNode { Id = 2 };
            var targetNeuron = new Neuron { Id = 3 };
            var synapse = new Synapse { Id = 100, SourceId = 1, TargetId = 3 };
            const float sourceValue = 1.23f;

            // --- ACT 1 --- (Source is a Neuron)
            var context1 = new ConditionContext(world, synapse, sourceNeuron, null, targetNeuron, sourceValue);

            // --- ASSERT 1 ---
            Assert.AreSame(world, context1.World, "World should be assigned correctly.");
            Assert.AreSame(synapse, context1.Synapse, "Synapse should be assigned correctly.");
            Assert.AreSame(sourceNeuron, context1.SourceNeuron, "SourceNeuron should be assigned correctly.");
            Assert.IsNull(context1.SourceInputNode, "SourceInputNode should be null when source is a neuron.");
            Assert.AreSame(targetNeuron, context1.TargetNeuron, "TargetNeuron should be assigned correctly.");
            AreClose(sourceValue, context1.SourceValue, message: "SourceValue should be assigned correctly.");

            // --- ACT 2 --- (Source is an InputNode)
            var context2 = new ConditionContext(world, synapse, null, sourceInput, targetNeuron, sourceValue);

            // --- ASSERT 2 ---
            Assert.AreSame(sourceInput, context2.SourceInputNode, "SourceInputNode should be assigned correctly.");
            Assert.IsNull(context2.SourceNeuron, "SourceNeuron should be null when source is an input node.");
        }

        #endregion

        #region Neuron Tests

        [TestMethod]
        public void Constructor_WhenCalled_InitializesOwnedSynapsesList()
        {
            // --- ARRANGE ---
            var neuron = new Neuron();

            // --- ASSERT ---
            Assert.IsNotNull(neuron.OwnedSynapses, "OwnedSynapses list should not be null.");
            Assert.AreEqual(0, neuron.OwnedSynapses.Count, "OwnedSynapses list should be empty on creation.");
        }

        [TestMethod]
        public void GetPotential_WithValidLVars_ReturnsSumOfDendriticAndSomaPotentials()
        {
            // --- ARRANGE ---
            var neuron = new Neuron { LocalVariables = new float[256] };
            neuron.LocalVariables[(int)Hidra.Core.LVarIndex.DendriticPotential] = 0.75f;
            neuron.LocalVariables[(int)Hidra.Core.LVarIndex.SomaPotential] = 0.5f;

            // --- ACT ---
            var potential = neuron.GetPotential();

            // --- ASSERT ---
            AreClose(1.25f, potential, message: "Potential should be the sum of dendritic and soma values.");
        }

        #endregion

        #region Synapse Tests

        [TestMethod]
        public void Constructor_WhenCalled_SetsCorrectDefaultValues()
        {
            // --- ARRANGE ---
            var synapse = new Synapse();

            // --- ASSERT ---
            Assert.AreEqual(SignalType.Delayed, synapse.SignalType);
            AreClose(1.0f, synapse.Weight);
            AreClose(1.0f, synapse.Parameter);
            IsZero(synapse.FatigueLevel);
        }

        #endregion

        #region Interface Tests

        [TestMethod]
        public void SetPrng_OnMockBrain_CanBeCalledSuccessfully()
        {
            // --- ARRANGE ---
            var brain = new MockBrainWithPrng();
            var prng = new MockPrng();

            // --- ACT ---
            brain.SetPrng(prng);

            // --- ASSERT ---
            Assert.AreSame(prng, brain.Prng, "The SetPrng method should successfully assign the PRNG instance.");
        }

        #endregion
    }
}
// Hidra.Tests/Core/HidraWorldTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

namespace Hidra.Tests.Core
{
    #region Test Helper Mocks

    /// <summary>
    /// A mock implementation of ICondition for testing conditional synapses.
    /// Its 'Evaluate' result can be controlled by the test.
    /// </summary>
    internal class MockCondition : ICondition
    {
        public bool ConditionResult { get; set; } = true;
        public bool Evaluate(ConditionContext context) => ConditionResult;
    }

    /// <summary>
    /// A mock implementation of IBrain for isolating HidraWorld's activation logic.
    /// It allows tests to define specific inputs and outputs and verify that Evaluate is called.
    /// </summary>
    internal class MockBrain : IBrain
    {
        public IReadOnlyList<BrainInput> InputMap { get; set; } = Array.Empty<BrainInput>();
        public IReadOnlyList<BrainOutput> OutputMap { get; set; } = Array.Empty<BrainOutput>();
        public bool CanLearn => false;
        public int EvaluateCallCount { get; private set; }
        public float[]? ReceivedInputs { get; private set; }

        public void Evaluate(float[] inputs)
        {
            EvaluateCallCount++;
            ReceivedInputs = inputs;
        }

        public void Mutate(float rate) { /* Not implemented for mock */ }
        public void Reset() => EvaluateCallCount = 0;
    }

    #endregion

    /// <summary>
    /// Contains unit tests for the HidraWorld class, focusing on its initialization,
    /// the core Step() method logic, and its handling of the new decoupled
    /// IBrain and ICondition interfaces.
    /// </summary>
    [TestClass]
    public class HidraWorldTests : BaseTestClass
    {
        private HidraConfig _config = null!;
        // A minimal, valid genome containing the required System Genesis gene (ID 0).
        private const string MINIMAL_GENOME = "GN0000000000";

        [TestInitialize]
        public void TestInitialize()
        {
            _config = new HidraConfig
            {
                // Disable decay and tax for predictable test outcomes.
                DefaultDecayRate = 1.0f,
                MetabolicTaxPerTick = 0f
            };
        }

        #region Initialization and Setup Tests

        /// <summary>
        /// Verifies that the HidraWorld constructor successfully initializes
        /// with a valid configuration and a minimal, compliant genome.
        /// </summary>
        [TestMethod]
        public void Constructor_WithValidGenesisGene_Succeeds()
        {
            // Act
            var world = new HidraWorld(_config, MINIMAL_GENOME);

            // Assert
            Assert.IsNotNull(world.Config, "Configuration should be set.");
            Assert.AreEqual(0f, world.CurrentTick, 1e-6f, "Initial tick should be 0.");
            Assert.AreEqual(1, world.Neurons.Count, "World should add a default neuron if Genesis creates none.");
            Assert.IsNotNull(world.Neurons.First().Value, "The default neuron should not be null.");
        }

        /// <summary>
        /// Verifies that the constructor successfully initializes when the Genesis gene (ID 0)
        /// is present but empty, as this is a valid state.
        /// </summary>
        [TestMethod]
        public void Constructor_WithEmptyGenesisGene_Succeeds()
        {
            // Arrange
            // This genome represents an empty Gene 0 and a non-empty Gene 1.
            string genomeWithEmptyGenesis = "GN0000000100"; 

            // Act
            // The constructor should execute without throwing an exception.
            var world = new HidraWorld(_config, genomeWithEmptyGenesis);
            
            // Assert
            Assert.IsNotNull(world, "The world should be created successfully.");
            Assert.IsTrue(world.Neurons.Any(), "The world should still contain at least one neuron.");
        }

        #endregion

        #region Core Step Logic Tests

        /// <summary>
        /// Verifies that the Step() method correctly applies passive state changes
        /// like decay, aging, and metabolic tax to neurons.
        /// </summary>
        [TestMethod]
        public void Step_AppliesPassiveStateChanges()
        {
            // Arrange
            _config.DefaultDecayRate = 0.9f;
            _config.MetabolicTaxPerTick = 0.1f;
            var world = new HidraWorld(_config, MINIMAL_GENOME);
            var neuron = world.Neurons.First().Value;

            neuron.LocalVariables[(int)LVarIndex.SomaPotential] = 1.0f;
            neuron.LocalVariables[(int)LVarIndex.DendriticPotential] = 0.5f; // Should be reset
            neuron.LocalVariables[(int)LVarIndex.Health] = 100f;
            neuron.LocalVariables[(int)LVarIndex.Age] = 10;

            // Act
            world.Step();

            // Assert
            Assert.AreEqual(1f, world.CurrentTick, 1e-6f, "CurrentTick should increment.");
            Assert.AreEqual(0.9f, neuron.LocalVariables[(int)LVarIndex.SomaPotential], 1e-6f, "Soma potential should decay.");
            Assert.AreEqual(0f, neuron.LocalVariables[(int)LVarIndex.DendriticPotential], "Dendritic potential should be reset to zero.");
            Assert.AreEqual(99.9f, neuron.LocalVariables[(int)LVarIndex.Health], 1e-6f, "Health should decrease by metabolic tax.");
            Assert.AreEqual(11, neuron.LocalVariables[(int)LVarIndex.Age], "Age should increment.");
        }

        /// <summary>
        /// Verifies that a neuron is correctly deactivated when its health drops to zero or below.
        /// </summary>
        [TestMethod]
        public void Step_DeactivatesNeuronWhenHealthIsDepleted()
        {
            // Arrange
            _config.MetabolicTaxPerTick = 1.0f;
            var world = new HidraWorld(_config, MINIMAL_GENOME);
            var neuron = world.Neurons.First().Value;
            neuron.LocalVariables[(int)LVarIndex.Health] = 0.5f;

            // Act
            world.Step();

            // Assert
            Assert.IsFalse(neuron.IsActive, "Neuron should be inactive after health is depleted.");
        }

        #endregion

        #region Firing and Activation Tests

        /// <summary>
        /// Verifies the complete fire-event-process cycle. A neuron firing in one tick
        /// should trigger an 'Activate' event that is processed in the subsequent tick.
        /// </summary>
        [TestMethod]
        public void Step_WhenNeuronFires_QueuesAndProcessesActivationEvent()
        {
            // Arrange
            var world = new HidraWorld(_config, MINIMAL_GENOME);
            var neuron = world.Neurons.First().Value;

            var mockBrain = new MockBrain();
            neuron.Brain = mockBrain;
            neuron.LocalVariables[(int)LVarIndex.SomaPotential] = _config.DefaultFiringThreshold + 0.1f;
            
            // The RefractoryPeriod LVar (index 2) is read by the Step() method when firing occurs.
            // It uses this value to set the RefractoryTimeLeft LVar.
            neuron.LocalVariables[2] = _config.DefaultRefractoryPeriod;


            // --- ACT 1: Firing Tick ---
            world.Step();

            // Assert 1: State changes on the firing tick.
            Assert.AreEqual(0f, neuron.LocalVariables[(int)LVarIndex.SomaPotential], "Soma potential should be reset after firing.");
            Assert.AreEqual(_config.DefaultRefractoryPeriod, neuron.LocalVariables[(int)LVarIndex.RefractoryTimeLeft], "Refractory period should be set.");
            Assert.AreEqual(0, mockBrain.EvaluateCallCount, "Brain should not be evaluated on the same tick it fires.");

            // --- ACT 2: Activation Tick ---
            world.Step();

            // Assert 2: Brain is evaluated on the following tick.
            Assert.AreEqual(1, mockBrain.EvaluateCallCount, "Brain should be evaluated on the tick after firing.");
        }

        /// <summary>
        /// Verifies that a neuron cannot fire if it is within its refractory period,
        /// even if its potential exceeds the firing threshold.
        /// </summary>
        [TestMethod]
        public void Step_NeuronInRefractoryPeriod_DoesNotFire()
        {
            // Arrange
            var world = new HidraWorld(_config, MINIMAL_GENOME);
            var neuron = world.Neurons.First().Value;
            var mockBrain = new MockBrain();
            neuron.Brain = mockBrain;

            // Set potential high enough to fire, but also put it in a refractory state.
            neuron.LocalVariables[(int)LVarIndex.SomaPotential] = _config.DefaultFiringThreshold + 0.5f;
            neuron.LocalVariables[(int)LVarIndex.RefractoryTimeLeft] = 2;

            // Act
            world.Step();

            // Assert
            Assert.AreNotEqual(0f, neuron.LocalVariables[(int)LVarIndex.SomaPotential], "Soma potential should not be reset.");
            Assert.AreEqual(1, neuron.LocalVariables[(int)LVarIndex.RefractoryTimeLeft], "Refractory time should decrement.");
            Assert.AreEqual(0, mockBrain.EvaluateCallCount, "Brain should not be evaluated if the neuron cannot fire.");
        }

        #endregion

        #region Signal and Synapse Behavior Tests

        /// <summary>
        /// Verifies that an 'Immediate' synapse continuously drives the target's
        /// dendritic potential based on the source's total potential within the same tick.
        /// </summary>
        [TestMethod]
        public void Step_ImmediateSynapse_DrivesDendriticPotential()
        {
            // Arrange
            var world = new HidraWorld(_config, MINIMAL_GENOME);
            world.AddInputNode(10);
            var input = world.GetInputNodeById(10)!;
            var neuron = world.Neurons.First().Value;
            world.AddSynapse(10, neuron.Id, SignalType.Immediate, 0.5f, 0f);
            
            // Act
            input.Value = 1.0f;
            world.Step();

            // Assert
            // Expected: Dendritic Potential = input.Value * weight = 1.0 * 0.5 = 0.5
            Assert.AreEqual(0.5f, neuron.LocalVariables[(int)LVarIndex.DendriticPotential], 1e-6f);
        }

                /// <summary>
        /// Verifies that a 'Delayed' synapse, upon source firing, correctly queues a
        /// PotentialPulse event that increases the target's soma potential on the next tick.
        /// </summary>
        [TestMethod]
        public void Step_DelayedSynapse_WhenSourceFires_IncreasesTargetSomaPotentialOnNextTick()
        {
            // Arrange
            var world = new HidraWorld(_config, MINIMAL_GENOME);
            var sourceNeuron = world.Neurons.First().Value;
            var targetNeuron = world.AddNeuron(Vector3.One);
            world.AddSynapse(sourceNeuron.Id, targetNeuron.Id, SignalType.Delayed, 0.7f, 0f);

            sourceNeuron.LocalVariables[(int)LVarIndex.SomaPotential] = _config.DefaultFiringThreshold + 0.1f;
            
            // FIX: Isolate the synapse logic by removing the brain from the source neuron.
            // This ensures the raw activation potential is used for the synapse calculation.
            sourceNeuron.Brain = null;
            
            // Act
            world.Step(); // Tick 1: Source fires, queues Activate event for Tick 2.
            Assert.AreEqual(0f, targetNeuron.LocalVariables[(int)LVarIndex.SomaPotential], "Target potential should be 0 after Tick 1.");

            world.Step(); // Tick 2: Activate event is processed, which queues the PotentialPulse event for Tick 3.
            Assert.AreEqual(0f, targetNeuron.LocalVariables[(int)LVarIndex.SomaPotential], "Target potential should still be 0 after Tick 2.");

            // FIX: Add a third step to process the queued PotentialPulse event.
            world.Step(); // Tick 3: PotentialPulse event is processed, increasing target's potential.

            // Assert
            // The payload is based on the source's raw activation potential, as the brain was bypassed.
            // Expected = (Activation Potential) * (Synapse Weight) = (1.0 + 0.1) * 0.7 = 0.77
            float expectedPotential = (_config.DefaultFiringThreshold + 0.1f) * 0.7f;
            Assert.AreEqual(expectedPotential, targetNeuron.LocalVariables[(int)LVarIndex.SomaPotential], 1e-6f);
        }
        
                /// <summary>
        /// Verifies that a 'Persistent' synapse sets and holds a value after the source
        /// fires, continuously driving the target's dendritic potential on subsequent ticks.
        /// </summary>
        [TestMethod]
        public void Step_PersistentSynapse_WhenSourceFires_SetsAndHoldsValue()
        {
            // Arrange
            var world = new HidraWorld(_config, MINIMAL_GENOME);
            var source = world.AddNeuron(new Vector3(1,0,0));
            var target = world.AddNeuron(new Vector3(2,0,0));
            var synapse = world.AddSynapse(source.Id, target.Id, SignalType.Persistent, 0.8f, 0f)!;
            source.LocalVariables[(int)LVarIndex.SomaPotential] = 1.2f; // Above threshold

            // FIX: Isolate the synapse logic by removing the brain from the source neuron.
            // This ensures the raw activation potential is used for the synapse calculation.
            source.Brain = null;

            // Act 1: Fire the source neuron
            world.Step(); // Tick 1: Source fires, Activate event queued for Tick 2.
            world.Step(); // Tick 2: Activate event is processed, synapse's PersistentValue is set.
            
            // Assert 1: Synapse now holds the value.
            // Expected = (Activation Potential) * (Synapse Weight) = 1.2 * 0.8 = 0.96
            float expectedHeldValue = 1.2f * 0.8f;
            Assert.IsTrue(synapse.IsPersistentValueSet, "The persistent value should be set.");
            Assert.AreEqual(expectedHeldValue, synapse.PersistentValue, 1e-6f, "The held value is incorrect.");
            
            // Act 2: Let the simulation run for another tick to see the effect.
            world.Step(); // Tick 3: Persistent synapse should drive target potential.

            // Assert 2: Target's dendritic potential is driven by the held value.
            Assert.AreEqual(expectedHeldValue, target.LocalVariables[(int)LVarIndex.DendriticPotential], 1e-6f, "Target dendritic potential was not driven by the held value.");
        }

        /// <summary>
        /// Verifies that a conditional synapse only transmits a signal when its
        /// ICondition.Evaluate method returns true.
        /// </summary>
        [TestMethod]
        public void Step_ConditionalSynapse_OnlyTransmitsWhenConditionIsTrue()
        {
            // Arrange
            var world = new HidraWorld(_config, MINIMAL_GENOME);
            world.AddInputNode(10);
            var input = world.GetInputNodeById(10)!;
            var neuron = world.Neurons.First().Value;

            var mockCondition = new MockCondition();
            var synapse = world.AddSynapse(10, neuron.Id, SignalType.Immediate, 1.0f, 0f)!;
            synapse.Condition = mockCondition;

            // --- THIS IS THE FIX ---
            // To purely test the condition, we disable fatigue which is on by default.
            synapse.FatigueRate = 0f;

            input.Value = 1.0f;

            // Act 1: Condition is false
            mockCondition.ConditionResult = false;
            world.Step();

            // Assert 1: No potential transmitted
            Assert.AreEqual(0f, neuron.LocalVariables[(int)LVarIndex.DendriticPotential], "Potential should not be transmitted when condition is false.");

            // Act 2: Condition is true
            mockCondition.ConditionResult = true;
            world.Step();

            // Assert 2: Potential is transmitted
            // The expected value is now exactly 1.0 because fatigue is disabled.
            Assert.AreEqual(1.0f, neuron.LocalVariables[(int)LVarIndex.DendriticPotential], 1e-6f, "Potential should be transmitted when condition is true.");
        }

        #endregion

        #region Concurrency Tests

        /// <summary>
        /// Verifies that public API methods for modifying the world state, like AddNeuron,
        /// are thread-safe and do not lead to race conditions or corrupted collections.
        /// </summary>
        [TestMethod]
        public async Task AddNeuron_FromMultipleThreads_IsThreadSafe()
        {
            // Arrange
            var world = new HidraWorld(_config, MINIMAL_GENOME);
            int initialNeuronCount = world.Neurons.Count;
            const int neuronsToAdd = 100;

            // Act
            var tasks = new List<Task>();
            for (int i = 0; i < neuronsToAdd; i++)
            {
                tasks.Add(Task.Run(() => world.AddNeuron(Vector3.Zero)));
            }
            await Task.WhenAll(tasks);

            // Assert
            Assert.AreEqual(initialNeuronCount + neuronsToAdd, world.Neurons.Count, "All neurons should be added without corruption.");
        }

        #endregion
    }
}
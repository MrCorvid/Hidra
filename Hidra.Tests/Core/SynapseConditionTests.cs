// Hidra.Tests/Core/SynapseConditionTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using System.Numerics;

namespace Hidra.Tests.Core
{
    /// <summary>
    /// Contains unit tests for the various ICondition implementations, verifying their
    /// evaluation logic against different world states.
    /// </summary>
    [TestClass]
    public class SynapseConditionTests : BaseTestClass
    {
        private HidraWorld _world = null!;
        private Neuron _sourceNeuron = null!;
        private Neuron _targetNeuron = null!;
        private InputNode _sourceInput = null!;
        private Synapse _synapse = null!;
        private const string MINIMAL_GENOME = "GN0000000000";

        [TestInitialize]
        public void TestInitialize()
        {
            var config = new HidraConfig();
            _world = new HidraWorld(config, MINIMAL_GENOME);
            _sourceNeuron = _world.Neurons.Values.First();
            _targetNeuron = _world.AddNeuron(Vector3.One);
            
            _world.AddInputNode(99);
            _sourceInput = _world.GetInputNodeById(99)!;
            
            _synapse = _world.AddSynapse(
                sourceId: _sourceNeuron.Id, 
                targetId: _targetNeuron.Id, 
                signalType: SignalType.Delayed,
                weight: 1.0f,
                parameter: 1.0f
            )!;
        }

        /// <summary>
        /// Creates the context object required by ICondition.Evaluate.
        /// </summary>
        private ConditionContext CreateContext(float sourceValue)
        {
            return new ConditionContext(_world, _synapse, _sourceNeuron, null, _targetNeuron, sourceValue);
        }

        #region State-Based Condition Tests

        /// <summary>
        /// Verifies that LVarCondition correctly compares a value on the source neuron.
        /// </summary>
        [TestMethod]
        public void LVarCondition_TargetingSource_EvaluatesCorrectly()
        {
            // Arrange
            _sourceNeuron.LocalVariables[(int)LVarIndex.Health] = 50f;
            var condition = new LVarCondition
            {
                Target = ConditionTarget.Source,
                LVarIndex = (int)LVarIndex.Health,
                Operator = ComparisonOperator.LessThan,
                Value = 60f
            };
            var context = CreateContext(0f);

            // Act & Assert
            Assert.IsTrue(condition.Evaluate(context), "Condition should be true when Health (50) < 60.");
            condition.Operator = ComparisonOperator.GreaterThan;
            Assert.IsFalse(condition.Evaluate(context), "Condition should be false when Health (50) > 60 is checked.");
        }

        /// <summary>
        /// Verifies that GVarCondition correctly reads and compares a global hormone value.
        /// </summary>
        [TestMethod]
        public void GVarCondition_EvaluatesCorrectly()
        {
            // Arrange
            _world.GlobalHormones[10] = 0.75f;
            var condition = new GVarCondition { GVarIndex = 10, Operator = ComparisonOperator.GreaterThanOrEqualTo, Value = 0.7f };
            var context = CreateContext(0f);

            // Act & Assert
            Assert.IsTrue(condition.Evaluate(context), "Condition should be true when GVar (0.75) >= 0.7.");
            condition.Value = 0.8f;
            Assert.IsFalse(condition.Evaluate(context), "Condition should be false when GVar (0.75) >= 0.8 is checked.");
        }

        #endregion

        #region Temporal Condition Tests

        /// <summary>
        /// Verifies that the RisingEdge operator triggers only on the tick the source value
        /// crosses the threshold from below.
        /// </summary>
        [TestMethod]
        public void TemporalCondition_RisingEdge_TriggersCorrectly()
        {
            // Arrange
            var condition = new TemporalCondition { Operator = TemporalOperator.RisingEdge, Threshold = 0.5f };
            var context = CreateContext(0f);
            context.Synapse.PreviousSourceValue = 0.2f; // Start below threshold

            // Act 1: Value rises above threshold
            context = new ConditionContext(context.World, context.Synapse, context.SourceNeuron, context.SourceInputNode, context.TargetNeuron, 0.8f);
            bool result1 = condition.Evaluate(context);

            // Act 2: Value stays above threshold
            context.Synapse.PreviousSourceValue = 0.8f;
            context = new ConditionContext(context.World, context.Synapse, context.SourceNeuron, context.SourceInputNode, context.TargetNeuron, 0.9f);
            bool result2 = condition.Evaluate(context);

            // Assert
            Assert.IsTrue(result1, "Rising edge should trigger when crossing threshold.");
            Assert.IsFalse(result2, "Rising edge should not trigger when already above threshold.");
        }

        /// <summary>
        /// Verifies that the Sustained operator triggers only after the source value has
        /// remained above the threshold for the required duration.
        /// </summary>
        [TestMethod]
        public void TemporalCondition_Sustained_TriggersAfterDuration()
        {
            // Arrange
            var condition = new TemporalCondition { Operator = TemporalOperator.Sustained, Threshold = 0.5f, Duration = 3 };
            var context = CreateContext(1.0f); // Value is above threshold
            context.Synapse.SustainedCounter = 0;

            // Act & Assert
            Assert.IsFalse(condition.Evaluate(context), "Sustained (Tick 1) should be false. Counter is now 1.");
            Assert.AreEqual(1, context.Synapse.SustainedCounter);

            Assert.IsFalse(condition.Evaluate(context), "Sustained (Tick 2) should be false. Counter is now 2.");
            Assert.AreEqual(2, context.Synapse.SustainedCounter);

            Assert.IsTrue(condition.Evaluate(context), "Sustained (Tick 3) should be true. Counter is now 3.");
            Assert.AreEqual(3, context.Synapse.SustainedCounter);
            
            // Act & Assert: Drop below threshold should reset
            context = new ConditionContext(context.World, context.Synapse, context.SourceNeuron, context.SourceInputNode, context.TargetNeuron, 0.1f);
            Assert.IsFalse(condition.Evaluate(context), "Sustained should be false and reset when value drops.");
            Assert.AreEqual(0, context.Synapse.SustainedCounter, "Counter should reset to 0.");
        }

        #endregion

        #region Composite Condition Tests

        /// <summary>
        /// Verifies that a CompositeCondition using AND logic only returns true
        /// if all of its sub-conditions are true.
        /// </summary>
        [TestMethod]
        public void CompositeCondition_WithAndLogic_RequiresAllChildrenToBeTrue()
        {
            // Arrange
            var trueCondition = new GVarCondition { Operator = ComparisonOperator.EqualTo, Value = 0f }; // Always true
            var falseCondition = new GVarCondition { Operator = ComparisonOperator.NotEqualTo, Value = 0f }; // Always false
            var context = CreateContext(0f);
            
            // Act & Assert
            var composite1 = new CompositeCondition { IsAndLogic = true, Conditions = { trueCondition, trueCondition } };
            Assert.IsTrue(composite1.Evaluate(context), "TRUE and TRUE should be TRUE.");

            var composite2 = new CompositeCondition { IsAndLogic = true, Conditions = { trueCondition, falseCondition } };
            Assert.IsFalse(composite2.Evaluate(context), "TRUE and FALSE should be FALSE.");
        }

        /// <summary>
        /// Verifies that a CompositeCondition using OR logic returns true
        /// if any of its sub-conditions are true.
        /// </summary>
        [TestMethod]
        public void CompositeCondition_WithOrLogic_RequiresAnyChildToBeTrue()
        {
            // Arrange
            var trueCondition = new GVarCondition { Operator = ComparisonOperator.EqualTo, Value = 0f }; // Always true
            var falseCondition = new GVarCondition { Operator = ComparisonOperator.NotEqualTo, Value = 0f }; // Always false
            var context = CreateContext(0f);
            
            // Act & Assert
            var composite1 = new CompositeCondition { IsAndLogic = false, Conditions = { trueCondition, falseCondition } };
            Assert.IsTrue(composite1.Evaluate(context), "TRUE or FALSE should be TRUE.");

            var composite2 = new CompositeCondition { IsAndLogic = false, Conditions = { falseCondition, falseCondition } };
            Assert.IsFalse(composite2.Evaluate(context), "FALSE or FALSE should be FALSE.");
        }

        #endregion
    }
}
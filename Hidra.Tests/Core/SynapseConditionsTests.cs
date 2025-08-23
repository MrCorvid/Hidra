// Hidra.Tests/Core/SynapseConditionsTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using System.Collections.Generic;
using System.Reflection;
using System;
using System.Linq;

namespace Hidra.Tests.Core
{
    [TestClass]
    public class SynapseConditionsTests : BaseTestClass
    {
        private HidraWorld _world = null!;
        private Neuron _sourceNeuron = null!;
        private Neuron _targetNeuron = null!;
        private Synapse _synapse = null!;

        [TestInitialize]
        public override void BaseInit()
        {
            base.BaseInit();
            _world = CreateWorld();
            
            // The LocalVariables arrays must be large enough to
            // accommodate all LVar indices, including the high-numbered system variables.
            // 256 is a safe size used elsewhere in the production code.
            _sourceNeuron = new Neuron { Id = 1, LocalVariables = new float[256] };
            _targetNeuron = new Neuron { Id = 2, LocalVariables = new float[256] };

            _synapse = new Synapse { Id = 101, SourceId = 1, TargetId = 2 };
        }

        /// <summary>
        /// Helper to create a standard context for tests.
        /// </summary>
        private ConditionContext CreateContext(float sourceValue = 1.0f)
        {
            return new ConditionContext(_world, _synapse, _sourceNeuron, null, _targetNeuron, sourceValue);
        }

        #region LVarCondition Tests

        [TestMethod]
        public void LVarCondition_SourceLVarMet_ReturnsTrue()
        {
            // --- ARRANGE ---
            var condition = new LVarCondition
            {
                Target = ConditionTarget.Source,
                LVarIndex = (int)LVarIndex.Health,
                Operator = ComparisonOperator.GreaterThan,
                Value = 0.5f
            };
            SetLVar(_sourceNeuron, LVarIndex.Health, 0.8f);
            var context = CreateContext();

            // --- ACT ---
            var result = condition.Evaluate(context);

            // --- ASSERT ---
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void LVarCondition_TargetLVarNotMet_ReturnsFalse()
        {
            // --- ARRANGE ---
            var condition = new LVarCondition
            {
                Target = ConditionTarget.Target,
                LVarIndex = (int)LVarIndex.SomaPotential,
                Operator = ComparisonOperator.EqualTo,
                Value = 1.0f
            };
            _targetNeuron.LocalVariables[(int)LVarIndex.SomaPotential] = 0.5f;
            var context = CreateContext();

            // --- ACT ---
            var result = condition.Evaluate(context);

            // --- ASSERT ---
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void LVarCondition_LVarIndexOutOfBounds_ReturnsFalse()
        {
            // --- ARRANGE ---
            var condition = new LVarCondition
            {
                Target = ConditionTarget.Source,
                LVarIndex = 999, // Out of bounds for _sourceNeuron
                Operator = ComparisonOperator.EqualTo,
                Value = 1.0f
            };
            var context = CreateContext();

            // --- ACT ---
            var result = condition.Evaluate(context);

            // --- ASSERT ---
            Assert.IsFalse(result, "Evaluation should fail for out-of-bounds LVar index.");
        }

        [TestMethod]
        public void LVarCondition_TargetNeuronIsNull_ReturnsFalse()
        {
            // --- ARRANGE ---
            var condition = new LVarCondition { Target = ConditionTarget.Target };
            // Create a context where the target neuron is null
            var context = new ConditionContext(_world, _synapse, _sourceNeuron, null, null, 1.0f);

            // --- ACT ---
            var result = condition.Evaluate(context);

            // --- ASSERT ---
            Assert.IsFalse(result);
        }

        #endregion

        #region GVarCondition Tests

        [TestMethod]
        public void GVarCondition_ConditionMet_ReturnsTrue()
        {
            // --- ARRANGE ---
            var condition = new GVarCondition
            {
                GVarIndex = 10,
                Operator = ComparisonOperator.LessThan,
                Value = 0.8f
            };
            _world.GlobalHormones[10] = 0.5f;
            var context = CreateContext();

            // --- ACT ---
            var result = condition.Evaluate(context);

            // --- ASSERT ---
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void GVarCondition_ConditionNotMet_ReturnsFalse()
        {
            // --- ARRANGE ---
            var condition = new GVarCondition
            {
                GVarIndex = 10,
                Operator = ComparisonOperator.GreaterThan,
                Value = 0.8f
            };
            _world.GlobalHormones[10] = 0.5f;
            var context = CreateContext();

            // --- ACT ---
            var result = condition.Evaluate(context);

            // --- ASSERT ---
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void GVarCondition_GVarIndexOutOfBounds_ReturnsFalse()
        {
            // --- ARRANGE ---
            var condition = new GVarCondition { GVarIndex = -1 };
            var context = CreateContext();

            // --- ACT ---
            var result = condition.Evaluate(context);

            // --- ASSERT ---
            Assert.IsFalse(result);
        }

        #endregion

        #region RelationalCondition Tests

        [TestMethod]
        public void RelationalCondition_SourceGreaterThanTarget_ReturnsTrue()
        {
            // --- ARRANGE ---
            var condition = new RelationalCondition { Operator = ComparisonOperator.GreaterThan };
            // Target potential is Dendritic (241) + Soma (242)
            _targetNeuron.LocalVariables[241] = 0.2f;
            _targetNeuron.LocalVariables[242] = 0.3f; // Total potential = 0.5f
            var context = CreateContext(sourceValue: 1.0f); // Source value > target potential

            // --- ACT ---
            var result = condition.Evaluate(context);

            // --- ASSERT ---
            Assert.IsTrue(result);
        }
        
        [TestMethod]
        public void RelationalCondition_TargetNeuronIsNull_ReturnsFalse()
        {
            // --- ARRANGE ---
            var condition = new RelationalCondition { Operator = ComparisonOperator.GreaterThan };
            var context = new ConditionContext(_world, _synapse, _sourceNeuron, null, null, 1.0f);

            // --- ACT ---
            var result = condition.Evaluate(context);

            // --- ASSERT ---
            Assert.IsFalse(result);
        }

        #endregion

        #region TemporalCondition Tests

        [TestMethod]
        public void TemporalCondition_RisingEdge_WhenCrossingThreshold_ReturnsTrue()
        {
            // --- ARRANGE ---
            var condition = new TemporalCondition { Operator = TemporalOperator.RisingEdge, Threshold = 0.5f };
            _synapse.PreviousSourceValue = 0.4f; // Below threshold
            var context = CreateContext(sourceValue: 0.6f); // Above threshold

            // --- ACT ---
            var result = condition.Evaluate(context);

            // --- ASSERT ---
            Assert.IsTrue(result);
        }
        
        [TestMethod]
        public void TemporalCondition_RisingEdge_WhenAlreadyAboveThreshold_ReturnsFalse()
        {
            // --- ARRANGE ---
            var condition = new TemporalCondition { Operator = TemporalOperator.RisingEdge, Threshold = 0.5f };
            _synapse.PreviousSourceValue = 0.6f; // Already above
            var context = CreateContext(sourceValue: 0.7f); // Still above

            // --- ACT ---
            var result = condition.Evaluate(context);

            // --- ASSERT ---
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TemporalCondition_FallingEdge_WhenCrossingThreshold_ReturnsTrue()
        {
            // --- ARRANGE ---
            var condition = new TemporalCondition { Operator = TemporalOperator.FallingEdge, Threshold = 0.5f };
            _synapse.PreviousSourceValue = 0.6f; // Above threshold
            var context = CreateContext(sourceValue: 0.4f); // Below threshold

            // --- ACT ---
            var result = condition.Evaluate(context);

            // --- ASSERT ---
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void TemporalCondition_Changed_WhenDifferenceExceedsThreshold_ReturnsTrue()
        {
            // --- ARRANGE ---
            var condition = new TemporalCondition { Operator = TemporalOperator.Changed, Threshold = 0.2f };
            _synapse.PreviousSourceValue = 0.5f;
            var context = CreateContext(sourceValue: 0.8f); // Change is 0.3f, > threshold

            // --- ACT ---
            var result = condition.Evaluate(context);

            // --- ASSERT ---
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void TemporalCondition_Sustained_MeetsDuration_ReturnsTrueAndResetsOnFall()
        {
            // --- ARRANGE ---
            var condition = new TemporalCondition { Operator = TemporalOperator.Sustained, Threshold = 0.5f, Duration = 3 };
            var context = CreateContext(sourceValue: 0.6f); // Value is above threshold
            _synapse.SustainedCounter = 0;

            // --- ACT 1 & ASSERT 1 --- (Tick 1)
            Assert.IsFalse(condition.Evaluate(context), "Should be false on first tick.");
            Assert.AreEqual(1, _synapse.SustainedCounter);

            // --- ACT 2 & ASSERT 2 --- (Tick 2)
            Assert.IsFalse(condition.Evaluate(context), "Should be false on second tick.");
            Assert.AreEqual(2, _synapse.SustainedCounter);

            // --- ACT 3 & ASSERT 3 --- (Tick 3 - Duration met)
            Assert.IsTrue(condition.Evaluate(context), "Should be true when duration is met.");
            Assert.AreEqual(3, _synapse.SustainedCounter);

            // --- ACT 4 & ASSERT 4 --- (Value falls below threshold)
            var fallingContext = CreateContext(sourceValue: 0.4f);
            Assert.IsFalse(condition.Evaluate(fallingContext), "Should be false when value falls.");
            Assert.AreEqual(0, _synapse.SustainedCounter, "Counter should reset when condition breaks.");
        }

        #endregion

        #region CompositeCondition Tests

        [TestMethod]
        public void CompositeCondition_AndLogic_AllTrue_ReturnsTrue()
        {
            // --- ARRANGE ---
            var condition = new CompositeCondition
            {
                IsAndLogic = true,
                Conditions = new List<ICondition>
                {
                    new MockCondition { ConditionResult = true },
                    new MockCondition { ConditionResult = true }
                }
            };
            var context = CreateContext();

            // --- ACT ---
            var result = condition.Evaluate(context);
            
            // --- ASSERT ---
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void CompositeCondition_AndLogic_OneFalse_ReturnsFalse()
        {
            // --- ARRANGE ---
            var condition = new CompositeCondition
            {
                IsAndLogic = true,
                Conditions = new List<ICondition>
                {
                    new MockCondition { ConditionResult = true },
                    new MockCondition { ConditionResult = false }
                }
            };
            var context = CreateContext();

            // --- ACT ---
            var result = condition.Evaluate(context);
            
            // --- ASSERT ---
            Assert.IsFalse(result);
        }
        
        [TestMethod]
        public void CompositeCondition_OrLogic_OneTrue_ReturnsTrue()
        {
            // --- ARRANGE ---
            var condition = new CompositeCondition
            {
                IsAndLogic = false, // OR logic
                Conditions = new List<ICondition>
                {
                    new MockCondition { ConditionResult = false },
                    new MockCondition { ConditionResult = true }
                }
            };
            var context = CreateContext();

            // --- ACT ---
            var result = condition.Evaluate(context);
            
            // --- ASSERT ---
            Assert.IsTrue(result);
        }
        
        [TestMethod]
        public void CompositeCondition_OrLogic_AllFalse_ReturnsFalse()
        {
            // --- ARRANGE ---
            var condition = new CompositeCondition
            {
                IsAndLogic = false, // OR logic
                Conditions = new List<ICondition>
                {
                    new MockCondition { ConditionResult = false },
                    new MockCondition { ConditionResult = false }
                }
            };
            var context = CreateContext();

            // --- ACT ---
            var result = condition.Evaluate(context);
            
            // --- ASSERT ---
            Assert.IsFalse(result);
        }
        
        [TestMethod]
        public void CompositeCondition_EmptyList_ReturnsTrue()
        {
            // --- ARRANGE ---
            var condition = new CompositeCondition(); // Empty by default
            var context = CreateContext();
            
            // --- ACT ---
            var result = condition.Evaluate(context);

            // --- ASSERT ---
            Assert.IsTrue(result, "An empty composite condition should always evaluate to true.");
        }

        #endregion

        #region ConditionHelper Tests

        [TestMethod]
        public void ConditionHelper_Compare_EvaluatesAllOperatorsCorrectly()
        {
            // --- ARRANGE ---
            // Use reflection to get the internal static method
            var conditionHelperType = typeof(HidraWorld).Assembly.GetType("Hidra.Core.ConditionHelper");
            Assert.IsNotNull(conditionHelperType, "Could not find internal type ConditionHelper.");
            var compareMethod = conditionHelperType.GetMethod("Compare", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(compareMethod, "Could not find internal method Compare.");

            float a = 10f;
            float b = 20f;
            float c = 10f + 1e-7f; // Value very close to a for Epsilon testing

            // Helper to invoke the internal method
            bool InvokeCompare(float val1, float val2, ComparisonOperator op) =>
                (bool)compareMethod.Invoke(null, new object[] { val1, val2, op })!;

            // --- ACT & ASSERT ---
            Assert.IsTrue(InvokeCompare(b, a, ComparisonOperator.GreaterThan));
            Assert.IsFalse(InvokeCompare(a, b, ComparisonOperator.GreaterThan));
            
            Assert.IsTrue(InvokeCompare(a, b, ComparisonOperator.LessThan));
            Assert.IsFalse(InvokeCompare(b, a, ComparisonOperator.LessThan));

            Assert.IsTrue(InvokeCompare(a, c, ComparisonOperator.EqualTo));
            Assert.IsFalse(InvokeCompare(a, b, ComparisonOperator.EqualTo));
            
            Assert.IsTrue(InvokeCompare(a, b, ComparisonOperator.NotEqualTo));
            Assert.IsFalse(InvokeCompare(a, c, ComparisonOperator.NotEqualTo));

            Assert.IsTrue(InvokeCompare(b, a, ComparisonOperator.GreaterThanOrEqualTo));
            Assert.IsTrue(InvokeCompare(a, c, ComparisonOperator.GreaterThanOrEqualTo));

            Assert.IsTrue(InvokeCompare(a, b, ComparisonOperator.LessThanOrEqualTo));
            Assert.IsTrue(InvokeCompare(a, c, ComparisonOperator.LessThanOrEqualTo));
        }

        #endregion
    }
}
// Hidra.Tests/Genome/Bridge/HidraSprakBridgeSynapseApiTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using System.Linq;
using System.Numerics;

namespace Hidra.Tests.Core.Genome
{
    [TestClass]
    public class HidraSprakBridgeSynapseApiTests : BaseTestClass
    {
        private HidraWorld _world = null!;
        private Neuron _selfNeuron = null!;
        private Neuron _otherNeuron = null!;
        private HidraSprakBridge _bridge = null!;

        [TestInitialize]
        public override void BaseInit()
        {
            base.BaseInit();
            _world = CreateWorld();
            _world.AddInputNode(100);
            _selfNeuron = _world.AddNeuron(new Vector3(1, 1, 1));
            _otherNeuron = _world.AddNeuron(new Vector3(2, 2, 2));
            _bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);
        }

        #region API_AddSynapse Tests

        [TestMethod]
        public void API_AddSynapse_ToNeuron_CreatesSynapseAndReturnsId()
        {
            float synapseId = _bridge.API_AddSynapse(0f, (float)_otherNeuron.Id, (float)SignalType.Persistent, 0.75f, 1.25f);
            
            Assert.IsTrue(synapseId > 0, "A valid non-zero ID should be returned.");
            var synapse = _world.GetSynapseById((ulong)synapseId);
            Assert.IsNotNull(synapse);
            Assert.AreEqual(_selfNeuron.Id, synapse.SourceId);
            Assert.AreEqual(_otherNeuron.Id, synapse.TargetId);
            Assert.AreEqual(SignalType.Persistent, synapse.SignalType);
            AreClose(0.75f, synapse.Weight);
            AreClose(1.25f, synapse.Parameter);
        }
        
        [TestMethod]
        public void API_AddSynapse_FromInputNode_CreatesReversedSynapse()
        {
            const ulong inputNodeId = 999;
            _world.AddInputNode(inputNodeId);
            float synapseId = _bridge.API_AddSynapse(2f, (float)inputNodeId, (float)SignalType.Immediate, 1f, 1f);

            var synapse = _world.GetSynapseById((ulong)synapseId);
            Assert.IsNotNull(synapse);
            Assert.AreEqual(_selfNeuron.Id, synapse.TargetId, "Target should be self when source is an InputNode.");
            Assert.AreEqual(inputNodeId, synapse.SourceId, "Source should be the specified InputNode.");
        }

        [TestMethod]
        public void API_AddSynapse_WithInvalidTargetId_UsesModulusFallback()
        {
            // FIX: This test now verifies the INTENDED fallback behavior of the bridge.
            const ulong invalidTargetId = 99999;

            // ACT: Attempt to create a synapse to a non-existent neuron.
            float synapseId = _bridge.API_AddSynapse(0f, (float)invalidTargetId, (float)SignalType.Immediate, 1f, 1f);
            
            // ASSERT: The bridge should fall back to creating a synapse to the only *other* available neuron.
            Assert.IsTrue(synapseId > 0, "A valid synapse ID should be returned.");
            var synapse = _world.GetSynapseById((ulong)synapseId);
            Assert.IsNotNull(synapse);
            Assert.AreEqual(_selfNeuron.Id, synapse.SourceId);
            Assert.AreEqual(_otherNeuron.Id, synapse.TargetId, "Target should fall back to the other neuron via modulus.");
        }

        #endregion

        #region API_ModifySynapse Tests

        [TestMethod]
        public void API_ModifySynapse_UpdatesCoreProperties()
        {
            var synapse = _world.AddSynapse(_selfNeuron.Id, _otherNeuron.Id, SignalType.Immediate, 1f, 1f)!;
            synapse.Condition = new GVarCondition();
            
            _bridge.API_ModifySynapse(0f, 0.5f, 2.5f, (float)SignalType.Delayed);
            
            AreClose(0.5f, synapse.Weight);
            AreClose(2.5f, synapse.Parameter);
            Assert.AreEqual(SignalType.Delayed, synapse.SignalType);
            Assert.IsNotNull(synapse.Condition, "Condition should not be cleared by this call.");
        }

        #endregion

        #region API_ClearSynapseCondition Tests

        [TestMethod]
        public void API_ClearSynapseCondition_RemovesExistingCondition()
        {
            var synapse = _world.AddSynapse(_selfNeuron.Id, _otherNeuron.Id, SignalType.Immediate, 1f, 1f)!;
            synapse.Condition = new LVarCondition();
            
            _bridge.API_ClearSynapseCondition(0f);
            
            Assert.IsNull(synapse.Condition, "Condition should be set to null.");
        }

        #endregion

        #region API_SetSynapseCondition Tests
        
        [TestMethod]
        public void API_SetSynapseCondition_CanSetLVarCondition_ImplicitlyOnSource()
        {
            var synapse = _world.AddSynapse(_selfNeuron.Id, _otherNeuron.Id, SignalType.Immediate, 1f, 1f)!;
            
            _bridge.API_SetSynapseCondition(0f, 0f, 5f, (float)ComparisonOperator.LessThan, 100f);
            
            Assert.IsInstanceOfType(synapse.Condition, typeof(LVarCondition));
            var condition = (LVarCondition)synapse.Condition!;
            Assert.AreEqual(ConditionTarget.Source, condition.Target);
            Assert.AreEqual(5, condition.LVarIndex);
            Assert.AreEqual(ComparisonOperator.LessThan, condition.Operator);
            AreClose(100f, condition.Value);
        }

        [TestMethod]
        public void API_SetSynapseCondition_LVarOnInputNodeSynapse_ImplicitlyTargetsTargetNeuron()
        {
            var synapse = _world.AddSynapse(100, _selfNeuron.Id, SignalType.Immediate, 1f, 1f)!;
            
            _bridge.API_SetSynapseCondition(0f, 0f, 5f, (float)ComparisonOperator.EqualTo, 42f);
            
            Assert.IsInstanceOfType(synapse.Condition, typeof(LVarCondition));
            var condition = (LVarCondition)synapse.Condition!;
            Assert.AreEqual(ConditionTarget.Target, condition.Target);
            Assert.AreEqual(5, condition.LVarIndex);
            Assert.AreEqual(ComparisonOperator.EqualTo, condition.Operator);
            AreClose(42f, condition.Value);
        }

        [TestMethod]
        public void API_SetSynapseCondition_CanSetGVarCondition()
        {
            var synapse = _world.AddSynapse(_selfNeuron.Id, _otherNeuron.Id, SignalType.Immediate, 1f, 1f)!;
            
            _bridge.API_SetSynapseCondition(0f, 1f, 10f, (float)ComparisonOperator.GreaterThan, 0.9f);
            
            Assert.IsInstanceOfType(synapse.Condition, typeof(GVarCondition));
            var condition = (GVarCondition)synapse.Condition!;
            Assert.AreEqual(10, condition.GVarIndex);
            Assert.AreEqual(ComparisonOperator.GreaterThan, condition.Operator);
            AreClose(0.9f, condition.Value);
        }

        [TestMethod]
        public void API_SetSynapseCondition_CanSetTemporalCondition()
        {
            var synapse = _world.AddSynapse(_selfNeuron.Id, _otherNeuron.Id, SignalType.Immediate, 1f, 1f)!;
            
            _bridge.API_SetSynapseCondition(0f, 2f, (float)TemporalOperator.Sustained, 0.5f, 10f);
            
            Assert.IsInstanceOfType(synapse.Condition, typeof(TemporalCondition));
            var condition = (TemporalCondition)synapse.Condition!;
            Assert.AreEqual(TemporalOperator.Sustained, condition.Operator);
            AreClose(0.5f, condition.Threshold);
            Assert.AreEqual(10, condition.Duration);
        }

        #endregion

        #region API_SetSynapseSimpleProperty Tests

        [TestMethod]
        public void API_SetSynapseSimpleProperty_SetsWeightCorrectly()
        {
            var synapse = _world.AddSynapse(_selfNeuron.Id, _otherNeuron.Id, SignalType.Immediate, 1f, 1f)!;
            
            _bridge.API_SetSynapseSimpleProperty(0f, (float)SynapseProperty.Weight, 0.123f);
            
            AreClose(0.123f, synapse.Weight);
        }

        [TestMethod]
        public void API_SetSynapseSimpleProperty_SetsParameterCorrectly()
        {
            var synapse = _world.AddSynapse(_selfNeuron.Id, _otherNeuron.Id, SignalType.Immediate, 1f, 1f)!;
            
            _bridge.API_SetSynapseSimpleProperty(0f, (float)SynapseProperty.Parameter, 5.5f);
            
            AreClose(5.5f, synapse.Parameter);
        }

        [TestMethod]
        public void API_SetSynapseSimpleProperty_SetsSignalTypeCorrectly()
        {
            var synapse = _world.AddSynapse(_selfNeuron.Id, _otherNeuron.Id, SignalType.Immediate, 1f, 1f)!;

            _bridge.API_SetSynapseSimpleProperty(0f, (float)SynapseProperty.SignalType, (float)SignalType.Persistent);
            
            Assert.AreEqual(SignalType.Persistent, synapse.SignalType);
        }
        
        #endregion
    }
}
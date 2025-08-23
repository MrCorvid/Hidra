// Hidra.Tests/Genome/Bridge/HidraSprakBridgeCoreApiTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;

namespace Hidra.Tests.Core.Genome
{
    [TestClass]
    public class HidraSprakBridgeCoreApiTests : BaseTestClass
    {
        private HidraConfig _config = null!;
        private HidraWorld _world = null!;
        private Neuron _selfNeuron = null!;
        private Neuron _otherNeuron = null!;

        [TestInitialize]
        public override void BaseInit()
        {
            base.BaseInit();
            _config = TestDefaults.DeterministicConfig();
            _world = CreateWorld(_config);

            _selfNeuron = _world.AddNeuron(new Vector3(10f, 20f, 30f));
            _otherNeuron = _world.AddNeuron(new Vector3(2, 2, 2));
        }

        #region LVar/GVar API Tests

        [TestMethod]
        public void API_StoreLVar_WritesToWritableLVar()
        {
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);
            bridge.API_StoreLVar(10f, 1.23f);
            AreClose(1.23f, _selfNeuron.LocalVariables[10]);
        }
        
        [TestMethod]
        public void API_StoreLVar_DoesNotWriteToProtectedLVar()
        {
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);
            _selfNeuron.LocalVariables[240] = 5f;
            bridge.API_StoreLVar(240f, 99f);
            AreClose(5f, _selfNeuron.LocalVariables[240], message: "Protected LVar should not be changed.");
        }

        [TestMethod]
        public void API_LoadLVar_ReadsCorrectValue()
        {
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);
            _selfNeuron.LocalVariables[15] = 3.14f;
            float result = bridge.API_LoadLVar(15f);
            AreClose(3.14f, result);
        }
        
        [TestMethod]
        public void API_StoreGVar_WritesToGlobalHormoneArray()
        {
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);
            bridge.API_StoreGVar(50f, 0.77f);
            AreClose(0.77f, _world.GlobalHormones[50]);
        }

        [TestMethod]
        public void API_LoadGVar_ReadsFromGlobalHormoneArray()
        {
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);
            _world.GlobalHormones[55] = 0.88f;
            float result = bridge.API_LoadGVar(55f);
            AreClose(0.88f, result);
        }

        #endregion

        #region Context-Sensitive API Tests

        [TestMethod]
        public void API_GetSelfId_ReturnsCorrectIdForContext()
        {
            var generalBridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);
            Assert.AreEqual((float)_selfNeuron.Id, generalBridge.API_GetSelfId());

            var systemBridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.System);
            systemBridge.API_SetSystemTarget((float)_otherNeuron.Id);
            Assert.AreEqual((float)_otherNeuron.Id, systemBridge.API_GetSelfId());
        }
        
        [TestMethod]
        public void API_GetPosition_ReturnsCorrectComponents()
        {
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);
            AreClose(10f, bridge.API_GetPosition(0f), message: "X component");
            AreClose(20f, bridge.API_GetPosition(1f), message: "Y component");
            AreClose(30f, bridge.API_GetPosition(2f), message: "Z component");
            IsZero(bridge.API_GetPosition(99f), message: "Invalid axis");
        }

        [TestMethod]
        public void API_CreateNeuron_RespectsSystemContext()
        {
            var systemBridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.System);
            int initialCount = _world.Neurons.Count;
            float newId = systemBridge.API_CreateNeuron(5f, 6f, 7f);
            Assert.IsTrue(newId > 0);
            Assert.AreEqual(initialCount + 1, _world.Neurons.Count);

            var generalBridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);
            float zeroId = generalBridge.API_CreateNeuron(5f, 6f, 7f);
            Assert.AreEqual(0f, zeroId);
            Assert.AreEqual(initialCount + 1, _world.Neurons.Count);
        }
        
        [TestMethod]
        public void API_Mitosis_RespectsProtectedContext()
        {
            // Library semantics: Protected context CANNOT perform mitosis; General CAN.
            var protectedBridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.Protected);
            int initialCount = _world.Neurons.Count;

            float childIdProtected = protectedBridge.API_Mitosis(1f, 1f, 1f);
            Assert.AreEqual(0f, childIdProtected, "Protected context must not create a child neuron.");
            Assert.AreEqual(initialCount, _world.Neurons.Count, "Neuron count must be unchanged after Protected mitosis attempt.");

            var generalBridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);
            float childIdGeneral = generalBridge.API_Mitosis(1f, 1f, 1f);
            Assert.IsTrue(childIdGeneral > 0f, "General context should create a child neuron.");
            Assert.AreEqual(initialCount + 1, _world.Neurons.Count, "Neuron count should increase by one after General mitosis.");
        }
        
        [TestMethod]
        public void API_Apoptosis_RespectsGeneralContext()
        {
            var deactivationListField = typeof(HidraWorld).GetField("_neuronsToDeactivate", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var list = (List<Neuron>)deactivationListField.GetValue(_world)!;

            var generalBridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);
            generalBridge.API_Apoptosis();
            Assert.AreEqual(1, list.Count);
            
            var systemBridge = new HidraSprakBridge(_world, _otherNeuron, Hidra.Core.ExecutionContext.System);
            systemBridge.API_Apoptosis();
            Assert.AreEqual(1, list.Count, message: "Call from wrong context should have no effect.");
        }

        #endregion

        #region Stability API Tests

        [TestMethod]
        public void API_SetRefractoryPeriod_SetsLVarAndClampsToZero()
        {
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);
            bridge.API_SetRefractoryPeriod(5f);
            AreClose(5f, _selfNeuron.LocalVariables[2]);
            bridge.API_SetRefractoryPeriod(-10f);
            AreClose(0f, _selfNeuron.LocalVariables[2], message: "Negative refractory period should be clamped to zero.");
        }

        [TestMethod]
        public void API_SetThresholdAdaptation_SetsLVarsAndClamps()
        {
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);
            
            bridge.API_SetThresholdAdaptation(0.5f, 0.1f);
            AreClose(0.5f, _selfNeuron.LocalVariables[3]);
            AreClose(0.1f, _selfNeuron.LocalVariables[4]);
            
            bridge.API_SetThresholdAdaptation(-1.0f, 1.5f);
            AreClose(0f, _selfNeuron.LocalVariables[3], message: "Negative adaptation factor should be clamped to zero.");
            AreClose(1.0f, _selfNeuron.LocalVariables[4], message: "Recovery rate > 1 should be clamped to 1.0.");
        }

        [TestMethod]
        public void API_GetFiringRate_ReturnsCorrectLVar()
        {
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);
            _selfNeuron.LocalVariables[240] = 0.987f;
            float rate = bridge.API_GetFiringRate();
            AreClose(0.987f, rate);
        }

        #endregion
    }
}

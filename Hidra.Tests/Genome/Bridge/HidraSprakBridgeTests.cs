// Hidra.Tests/Core/Genome/HidraSprakBridgeTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using ProgrammingLanguageNr1; // Required for Sprak types
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;

namespace Hidra.Tests.Core.Genome
{
    [TestClass]
    public class HidraSprakBridgeTests : BaseTestClass
    {
        private HidraConfig _config = null!;
        private HidraWorld _world = null!;
        private Neuron _selfNeuron = null!;

        [TestInitialize]
        public override void BaseInit()
        {
            base.BaseInit();
            _config = TestDefaults.DeterministicConfig();
            _world = CreateWorld(_config);
            _selfNeuron = _world.AddNeuron(new Vector3(10, 10, 10));

            // Ensure any pre-seeded/non-self neurons cannot affect nearest-neighbor style tests.
            MoveAwayBaselineNeurons(_world, _selfNeuron);
        }

        private static void MoveAwayBaselineNeurons(HidraWorld world, Neuron self)
        {
            foreach (var n in world.Neurons.Values)
            {
                if (n.Id != self.Id)
                {
                    n.Position = new Vector3(1_000_000f, 1_000_000f, 1_000_000f);
                }
            }
        }

        #region Reflection Helpers

        private Neuron? InvokeGetTargetNeuron(HidraSprakBridge bridge)
        {
            var methodInfo = typeof(HidraSprakBridge).GetMethod("GetTargetNeuron", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(methodInfo, "Private method 'GetTargetNeuron' not found.");
            return (Neuron?)methodInfo.Invoke(bridge, null);
        }

        #endregion

        #region Initialization Tests

        [TestMethod]
        public void Constructor_WithValidArgs_InitializesFieldsCorrectly()
        {
            // --- ARRANGE ---
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);

            // --- ASSERT ---
            var worldField = typeof(HidraSprakBridge).GetField("_world", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var selfField = typeof(HidraSprakBridge).GetField("_self", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.AreSame(_world, worldField.GetValue(bridge));
            Assert.AreSame(_selfNeuron, selfField.GetValue(bridge));
        }

        [TestMethod]
        public void SetInterpreter_AssignsInterpreterFieldCorrectly()
        {
            // --- ARRANGE ---
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);
            var interpreterField = typeof(HidraSprakBridge).GetField("_interpreter", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var sourceReader = new StringReader("");

            // FunctionDefinitionCreator.CreateDefinitions returns FunctionDefinition[], not a Dictionary.
            var functionArray = FunctionDefinitionCreator.CreateDefinitions(bridge, typeof(HidraSprakBridge));
            
            var runner = new SprakRunner(sourceReader, functionArray);
            var interpreter = runner.GetInterpreter();
            Assert.IsNotNull(interpreter, "SprakRunner should successfully create an interpreter instance.");

            // --- ACT ---
            bridge.SetInterpreter(interpreter);
            
            // --- ASSERT ---
            var assignedInterpreter = interpreterField.GetValue(bridge);
            Assert.AreSame(interpreter, assignedInterpreter, "The interpreter field should be set to the provided instance.");
        }

        #endregion

        #region GetTargetNeuron Tests

        [TestMethod]
        public void GetTargetNeuron_InGeneralContext_ReturnsSelf()
        {
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);
            var target = InvokeGetTargetNeuron(bridge);
            Assert.AreSame(_selfNeuron, target);
        }

        [TestMethod]
        public void GetTargetNeuron_InSystemContext_AfterSetSystemTarget_ReturnsNewTarget()
        {
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.System);
            var newTarget = _world.AddNeuron(Vector3.One);
            var systemTargetField = typeof(HidraSprakBridge).GetField("_systemTargetNeuron", BindingFlags.NonPublic | BindingFlags.Instance)!;
            systemTargetField.SetValue(bridge, newTarget);
            var targetAfterChange = InvokeGetTargetNeuron(bridge);
            Assert.AreSame(newTarget, targetAfterChange);
        }

        #endregion

        #region Nearest Neighbor (via public API)

        [TestMethod]
        public void FindNearestNeighbor_WhenNeighborsExist_ReturnsClosestNeuron()
        {
            // Use the public API to avoid depending on internal spatial index timing.
            _config.CompetitionRadius = 100f;

            // Build a fresh world and self for this test case
            _world = CreateWorld(_config);
            _selfNeuron = _world.AddNeuron(new Vector3(0, 0, 0));

            // Push away any pre-seeded / baseline neurons so they don't interfere
            MoveAwayBaselineNeurons(_world, _selfNeuron);

            // Now add controlled neighbors
            var closestNeuron = _world.AddNeuron(new Vector3(5, 0, 0));
            _world.AddNeuron(new Vector3(10, 0, 0));

            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);

            var nearestId = bridge.API_GetNearestNeighborId();
            Assert.IsTrue(nearestId > 0, "A nearest neighbor should be found within the competition radius.");
            Assert.AreEqual((float)closestNeuron.Id, nearestId, "The ID of the mathematically closest neuron should be returned.");
        }
        
        #endregion
    }
}
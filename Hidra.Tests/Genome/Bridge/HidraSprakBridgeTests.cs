// Hidra.Tests/Genome/Bridge/HidraSprakBridgeTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using Hidra.Core.Brain;
using System.Numerics;
using System.Linq;

namespace Hidra.Tests.Genome.Bridge
{
    /// <summary>
    /// Contains unit tests for the HidraSprakBridge, which implements the API
    /// for HGL scripts. These tests validate API behavior, context-based security,
    /// and correct world state modification.
    /// </summary>
    [TestClass]
    public class HidraSprakBridgeTests : BaseTestClass
    {
        private HidraWorld _world = null!;
        private Neuron _neuron1 = null!;
        private HidraConfig _config = null!;
        private const string MINIMAL_GENOME = "GN0000000000";

        [TestInitialize]
        public void Setup()
        {
            _config = new HidraConfig { CompetitionRadius = 10f };
            _world = new HidraWorld(_config, MINIMAL_GENOME);
            _neuron1 = _world.Neurons.First().Value;
        }

        #region Context and Targeting Tests

        /// <summary>
        /// Verifies that API_StoreLVar correctly writes to the 'self' neuron's local
        /// variables when called within a General context.
        /// </summary>
        [TestMethod]
        public void API_StoreLVar_InGeneralContext_WritesToSelf()
        {
            // Arrange
            var generalBridge = new HidraSprakBridge(_world, _neuron1, Hidra.Core.ExecutionContext.General);

            // Act: Note the reversed parameter order to match stack-based calling convention.
            generalBridge.API_StoreLVar(value: 42.0f, index: 10f);

            // Assert
            Assert.AreEqual(42.0f, _neuron1.LocalVariables[10]);
        }
        
        /// <summary>
        /// Verifies that API_StoreLVar is blocked from writing to protected system LVars
        /// (like Health, Age) to prevent script-based exploits.
        /// </summary>
        [TestMethod]
        public void API_StoreLVar_InAnyContext_IsBlockedFromWritingToReservedLVars()
        {
            // Arrange
            var generalBridge = new HidraSprakBridge(_world, _neuron1, Hidra.Core.ExecutionContext.General);
            _neuron1.LocalVariables[(int)LVarIndex.Health] = 100f;
            
            // Act
            generalBridge.API_StoreLVar(value: 999f, index: (int)LVarIndex.Health);

            // Assert
            Assert.AreEqual(100f, _neuron1.LocalVariables[(int)LVarIndex.Health], "Health LVar should not be changed by script.");
        }

        /// <summary>
        /// Verifies that in a System context, API_SetSystemTarget correctly changes the
        /// active target for subsequent API calls.
        /// </summary>
        [TestMethod]
        public void API_SetSystemTarget_InSystemContext_ChangesApiTarget()
        {
            // Arrange
            var systemBridge = new HidraSprakBridge(_world, null, Hidra.Core.ExecutionContext.System);
            var neuron2 = _world.AddNeuron(Vector3.One);
            neuron2.LocalVariables[10] = 0f;
            _neuron1.LocalVariables[10] = 0f;

            // Act
            systemBridge.API_SetSystemTarget((float)neuron2.Id); // Target neuron2
            systemBridge.API_StoreLVar(value: 99f, index: 10f);  // Should affect neuron2

            // Assert
            Assert.AreEqual(99f, neuron2.LocalVariables[10], "The new system target (neuron2) should have been modified.");
            Assert.AreEqual(0f, _neuron1.LocalVariables[10], "The original/un-targeted neuron should be unaffected.");
        }

        /// <summary>
        /// Verifies that a security-critical function like API_Apoptosis only succeeds
        /// when called in the correct (General) context.
        /// </summary>
        [TestMethod]
        public void API_Apoptosis_OnlySucceedsInGeneralContext()
        {
            // Arrange
            var systemBridge = new HidraSprakBridge(_world, _neuron1, Hidra.Core.ExecutionContext.System);
            var protectedBridge = new HidraSprakBridge(_world, _neuron1, Hidra.Core.ExecutionContext.Protected);
            var generalBridge = new HidraSprakBridge(_world, _neuron1, Hidra.Core.ExecutionContext.General);

            // Act & Assert (System should fail)
            _neuron1.IsActive = true;
            systemBridge.API_Apoptosis();
            Assert.IsTrue(_neuron1.IsActive, "Apoptosis should be blocked in System context.");

            // Act & Assert (Protected should fail)
            protectedBridge.API_Apoptosis();
            Assert.IsTrue(_neuron1.IsActive, "Apoptosis should be blocked in Protected context.");

            // Act & Assert (General should succeed)
            generalBridge.API_Apoptosis();
            Assert.IsFalse(_neuron1.IsActive, "Apoptosis should succeed in General context.");
        }

        #endregion

        #region Brain API Tests

        /// <summary>
        /// Verifies that API_SetBrainType can correctly switch a neuron's brain between
        /// NeuralNetworkBrain and LogicGateBrain instances.
        /// </summary>
        [TestMethod]
        public void API_SetBrainType_SwitchesBrainImplementation()
        {
            // Arrange
            var bridge = new HidraSprakBridge(_world, _neuron1, Hidra.Core.ExecutionContext.General);
            Assert.IsInstanceOfType(_neuron1.Brain, typeof(NeuralNetworkBrain), "Initial brain should be NeuralNetworkBrain.");

            // Act 1: Switch to LogicGateBrain
            bridge.API_SetBrainType(1f);
            
            // Assert 1
            Assert.IsInstanceOfType(_neuron1.Brain, typeof(LogicGateBrain), "Brain should be switched to LogicGateBrain.");
            
            // Act 2: Switch back to NeuralNetworkBrain
            bridge.API_SetBrainType(0f);
            
            // Assert 2
            Assert.IsInstanceOfType(_neuron1.Brain, typeof(NeuralNetworkBrain), "Brain should be switched back to NeuralNetworkBrain.");
        }

        /// <summary>
        /// Verifies that the 'SimpleFeedForward' brain constructor generates a network
        /// with the correct number of nodes and fully-connected layers.
        /// </summary>
        [TestMethod]
        public void API_CreateBrain_SimpleFeedForward_GeneratesCorrectTopology()
        {
            // Arrange
            var bridge = new HidraSprakBridge(_world, _neuron1, Hidra.Core.ExecutionContext.General);

            // Act
            bridge.API_CreateBrain_SimpleFeedForward(numInputs: 2, numOutputs: 1, numHiddenLayers: 1, nodesPerLayer: 3);
            
            // Assert
            Assert.IsInstanceOfType(_neuron1.Brain, typeof(NeuralNetworkBrain));
            var nnBrain = _neuron1.Brain as NeuralNetworkBrain;
            var network = nnBrain!.GetInternalNetwork();
            
            // Expected nodes: 2 input + 3 hidden + 1 output = 6
            // Expected connections: (2 inputs * 3 hidden) + (3 hidden * 1 output) = 6 + 3 = 9
            Assert.AreEqual(2, network.InputNodes.Count, "Incorrect number of input nodes.");
            Assert.AreEqual(1, network.OutputNodes.Count, "Incorrect number of output nodes.");
            Assert.AreEqual(3, network.Nodes.Values.Count(n => n.NodeType == NNNodeType.Hidden), "Incorrect number of hidden nodes.");
            Assert.AreEqual(9, network.Connections.Count, "Incorrect number of connections generated.");
        }

        #endregion

        #region Synapse API Tests

        /// <summary>
        /// Verifies that API_ModifySynapse correctly updates a synapse's properties and
        /// attaches a new LVarCondition.
        /// </summary>
        [TestMethod]
        public void API_ModifySynapse_UpdatesPropertiesAndAddsCondition()
        {
            // Arrange
            var targetNeuron = _world.AddNeuron(Vector3.One);
            var synapse = _world.AddSynapse(targetNeuron.Id, _neuron1.Id, SignalType.Delayed, 1f, 1f)!;
            targetNeuron.OwnedSynapses.Add(synapse); // Manually add to owned list for test

            var bridge = new HidraSprakBridge(_world, targetNeuron, Hidra.Core.ExecutionContext.General);

            // Act
            bridge.API_ModifySynapse(
                localIndex: 0,
                newWeight: -0.5f,
                newParameter: 2.0f,
                newSignalType: (float)SignalType.Persistent,
                conditionLVarIndex: (int)LVarIndex.Health,
                conditionOp: (float)ComparisonOperator.GreaterThan,
                conditionValue: 50f,
                conditionTarget: (float)ConditionTarget.Source
            );

            // Assert
            Assert.AreEqual(-0.5f, synapse.Weight);
            Assert.AreEqual(2.0f, synapse.Parameter);
            Assert.AreEqual(SignalType.Persistent, synapse.SignalType);
            Assert.IsNotNull(synapse.Condition, "Condition should have been set.");
            Assert.IsInstanceOfType(synapse.Condition, typeof(LVarCondition));

            var lvarCondition = synapse.Condition as LVarCondition;
            Assert.AreEqual((int)LVarIndex.Health, lvarCondition!.LVarIndex);
        }

        #endregion
    }
}
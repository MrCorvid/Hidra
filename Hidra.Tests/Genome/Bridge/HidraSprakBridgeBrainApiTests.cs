// Hidra.Tests/Genome/Bridge/HidraSprakBridgeBrainApiTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using Hidra.Core.Brain;
using System.Linq;
using System;
using System.Numerics;

namespace Hidra.Tests.Core.Genome
{
    [TestClass]
    public class HidraSprakBridgeBrainApiTests : BaseTestClass
    {
        private HidraWorld _world = null!;
        private Neuron _selfNeuron = null!;
        private HidraSprakBridge _bridge = null!;

        [TestInitialize]
        public override void BaseInit()
        {
            base.BaseInit();
            _world = CreateWorld();
            _selfNeuron = _world.AddNeuron(new Vector3(1, 1, 1));
            _bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);
        }

        #region Brain Type and Configuration Tests

        [TestMethod]
        public void API_SetBrainType_CanSwitchToNeuralNetworkAndLogicGate()
        {
            _bridge.API_SetBrainType(1f); // 1 = LogicGate
            Assert.IsInstanceOfType(_selfNeuron.Brain, typeof(LogicGateBrain));

            _bridge.API_SetBrainType(0f); // 0 = NeuralNetwork
            Assert.IsInstanceOfType(_selfNeuron.Brain, typeof(NeuralNetworkBrain));
        }

        [TestMethod]
        public void API_ConfigureLogicGate_SetsGateTypeAndThreshold()
        {
            _bridge.API_SetBrainType(1f);
            var logicBrain = (LogicGateBrain)_selfNeuron.Brain;
            
            _bridge.API_ConfigureLogicGate((float)LogicGateType.XOR, 0f, 0.75f);

            Assert.AreEqual(LogicGateType.XOR, logicBrain.GateType);
            Assert.IsNull(logicBrain.FlipFlop);
            AreClose(0.75f, logicBrain.Threshold);
        }

        [TestMethod]
        public void API_ConfigureLogicGate_SetsFlipFlopType()
        {
            _bridge.API_SetBrainType(1f);
            var logicBrain = (LogicGateBrain)_selfNeuron.Brain;
            
            var flipFlopValues = Enum.GetValues(typeof(FlipFlopType));
            Assert.IsTrue(flipFlopValues.Length > 0, "FlipFlopType enum must have members for this test.");
            var validFlipFlopType = (FlipFlopType)flipFlopValues.GetValue(0)!;
            
            _bridge.API_ConfigureLogicGate((float)validFlipFlopType, 1f, 0.5f);

            Assert.AreEqual(validFlipFlopType, logicBrain.FlipFlop);
        }

        #endregion

        #region Brain Structure (Neural Network Specific)

        [TestMethod]
        public void API_ClearBrain_OnNeuralNetwork_RemovesAllNodesAndConnections()
        {
            _selfNeuron.Brain = new NeuralNetworkBrain();
            var nnBrain = (NeuralNetworkBrain)_selfNeuron.Brain;
            // FIX: Use the correct method to get the network
            var network = nnBrain.GetInternalNetwork();
            network.AddNode(new NNNode(0, NNNodeType.Input));
            network.AddNode(new NNNode(1, NNNodeType.Output));
            network.AddConnection(new NNConnection(0, 1, 1f));
            Assert.AreEqual(2, network.Nodes.Count);
            Assert.AreEqual(1, network.Connections.Count);
            
            _bridge.API_ClearBrain();
            
            Assert.AreEqual(0, network.Nodes.Count);
            Assert.AreEqual(0, network.Connections.Count);
        }

        [TestMethod]
        public void API_AddBrainNode_AddsNodeAndReturnsValidId()
        {
            _selfNeuron.Brain = new NeuralNetworkBrain();
            _bridge.API_ClearBrain();
            
            float nodeId = _bridge.API_AddBrainNode((float)NNNodeType.Hidden, 0.5f);

            Assert.IsTrue(nodeId >= 0);
            // FIX: Use the correct method to get the network
            var node = ((NeuralNetworkBrain)_selfNeuron.Brain).GetInternalNetwork().Nodes[(int)nodeId];
            Assert.AreEqual(NNNodeType.Hidden, node.NodeType);
            AreClose(0.5f, node.Bias);
        }

        [TestMethod]
        public void API_AddBrainConnection_AddsConnectionBetweenNodes()
        {
            _selfNeuron.Brain = new NeuralNetworkBrain();
            _bridge.API_ClearBrain();
            float n1 = _bridge.API_AddBrainNode(0, 0);
            float n2 = _bridge.API_AddBrainNode(2, 0);

            _bridge.API_AddBrainConnection(n1, n2, 0.77f);
            
            // FIX: Use the correct method to get the network
            var network = ((NeuralNetworkBrain)_selfNeuron.Brain).GetInternalNetwork();
            Assert.AreEqual(1, network.Connections.Count);
            var conn = network.Connections.First();
            Assert.AreEqual((int)n1, conn.FromNodeId);
            Assert.AreEqual((int)n2, conn.ToNodeId);
            AreClose(0.77f, conn.Weight);
        }

        [TestMethod]
        public void API_RemoveBrainNode_RemovesNodeAndConnections()
        {
            _selfNeuron.Brain = new NeuralNetworkBrain();
            _bridge.API_ClearBrain();
            float n1 = _bridge.API_AddBrainNode(0, 0);
            float n2 = _bridge.API_AddBrainNode(1, 0);
            _bridge.API_AddBrainConnection(n1, n2, 1f);

            _bridge.API_RemoveBrainNode(n1);

            // FIX: Use the correct method to get the network
            var network = ((NeuralNetworkBrain)_selfNeuron.Brain).GetInternalNetwork();
            Assert.AreEqual(1, network.Nodes.Count, "The other node should remain.");
            Assert.IsFalse(network.Nodes.ContainsKey((int)n1), "The specified node should be gone.");
            Assert.AreEqual(0, network.Connections.Count, "The associated connection should be gone.");
        }

        [TestMethod]
        public void API_RemoveBrainConnection_RemovesSpecificConnection()
        {
            _selfNeuron.Brain = new NeuralNetworkBrain();
            _bridge.API_ClearBrain();
            float n1 = _bridge.API_AddBrainNode(0, 0);
            float n2 = _bridge.API_AddBrainNode(2, 0);
            _bridge.API_AddBrainConnection(n1, n2, 1f);

            _bridge.API_RemoveBrainConnection(n1, n2);

            // FIX: Use the correct method to get the network
            var network = ((NeuralNetworkBrain)_selfNeuron.Brain).GetInternalNetwork();
            Assert.AreEqual(0, network.Connections.Count);
        }

        #endregion

        #region Brain Configuration

        [TestMethod]
        public void API_ConfigureOutputNode_SetsActionType()
        {
            _selfNeuron.Brain = new NeuralNetworkBrain();
            _bridge.API_ClearBrain();
            float nId = _bridge.API_AddBrainNode((float)NNNodeType.Output, 0);

            _bridge.API_ConfigureOutputNode(nId, (float)OutputActionType.ExecuteGene);
            
            // FIX: Use the correct method to get the network
            var node = ((NeuralNetworkBrain)_selfNeuron.Brain).GetInternalNetwork().Nodes[(int)nId];
            Assert.AreEqual(OutputActionType.ExecuteGene, node.ActionType);
        }
        
        [TestMethod]
        public void API_SetBrainInputSource_OnNeuralNetworkBrain_SetsSource()
        {
            _selfNeuron.Brain = new NeuralNetworkBrain();
            _bridge.API_ClearBrain();
            float nId = _bridge.API_AddBrainNode((float)NNNodeType.Input, 0);

            _bridge.API_SetBrainInputSource(nId, (float)InputSourceType.Age, 5f);
            
            // FIX: Use the correct method to get the network
            var node = ((NeuralNetworkBrain)_selfNeuron.Brain).GetInternalNetwork().Nodes[(int)nId];
            Assert.AreEqual(InputSourceType.Age, node.InputSource);
            Assert.AreEqual(5, node.SourceIndex);
        }

        [TestMethod]
        public void API_SetBrainInputSource_OnLogicGateBrain_AddsInput()
        {
            _bridge.API_SetBrainType(1f);
            var logicBrain = (LogicGateBrain)_selfNeuron.Brain;
            logicBrain.ClearInputs();
            var inputsBefore = logicBrain.InputMap.Count;

            _bridge.API_SetBrainInputSource(0f, (float)InputSourceType.GlobalHormone, 10f);

            Assert.AreEqual(inputsBefore + 1, logicBrain.InputMap.Count);
            Assert.IsTrue(logicBrain.InputMap.Any(i => i.SourceType == InputSourceType.GlobalHormone && i.SourceIndex == 10));
        }
        
        [TestMethod]
        public void API_SetNodeActivationFunction_SetsFunctionType()
        {
            _selfNeuron.Brain = new NeuralNetworkBrain();
            _bridge.API_ClearBrain();
            float nId = _bridge.API_AddBrainNode((float)NNNodeType.Hidden, 0);

            _bridge.API_SetNodeActivationFunction(nId, (float)ActivationFunctionType.ReLU);
            
            // FIX: Use the correct method to get the network
            var node = ((NeuralNetworkBrain)_selfNeuron.Brain).GetInternalNetwork().Nodes[(int)nId];
            Assert.AreEqual(ActivationFunctionType.ReLU, node.ActivationFunction);
        }

        [TestMethod]
        public void API_SetBrainConnectionWeight_UpdatesWeight()
        {
            _selfNeuron.Brain = new NeuralNetworkBrain();
            _bridge.API_ClearBrain();
            float n1 = _bridge.API_AddBrainNode(0, 0);
            float n2 = _bridge.API_AddBrainNode(2, 0);
            _bridge.API_AddBrainConnection(n1, n2, 0.5f);

            _bridge.API_SetBrainConnectionWeight(n1, n2, -0.99f);
            
            // FIX: Use the correct method to get the network
            var conn = ((NeuralNetworkBrain)_selfNeuron.Brain).GetInternalNetwork().Connections.First();
            AreClose(-0.99f, conn.Weight);
        }
        
        [TestMethod]
        public void API_SetBrainNodeProperty_SetsBias()
        {
            _selfNeuron.Brain = new NeuralNetworkBrain();
            _bridge.API_ClearBrain();
            float nId = _bridge.API_AddBrainNode((float)NNNodeType.Hidden, 0.1f);
            
            _bridge.API_SetBrainNodeProperty(nId, 0f, 0.88f); // 0 = Bias

            // FIX: Use the correct method to get the network
            var node = ((NeuralNetworkBrain)_selfNeuron.Brain).GetInternalNetwork().Nodes[(int)nId];
            AreClose(0.88f, node.Bias);
        }
        
        [TestMethod]
        public void API_SetBrainNodeProperty_SetsActivationFunction()
        {
            _selfNeuron.Brain = new NeuralNetworkBrain();
            _bridge.API_ClearBrain();
            float nId = _bridge.API_AddBrainNode((float)NNNodeType.Hidden, 0.1f);
            
            _bridge.API_SetBrainNodeProperty(nId, 1f, (float)ActivationFunctionType.Sigmoid); // 1 = ActivationFunction

            // FIX: Use the correct method to get the network
            var node = ((NeuralNetworkBrain)_selfNeuron.Brain).GetInternalNetwork().Nodes[(int)nId];
            Assert.AreEqual(ActivationFunctionType.Sigmoid, node.ActivationFunction);
        }

        #endregion

        #region Brain Constructors ("Hammers")

        [TestMethod]
        public void API_CreateBrain_SimpleFeedForward_BuildsCorrectTopology()
        {
            int numInputs = 2, numHidden = 3, numOutputs = 1;
            int expectedNodes = numInputs + numHidden + numOutputs;
            int expectedConnections = (numInputs * numHidden) + (numHidden * numOutputs);
            
            _bridge.API_CreateBrain_SimpleFeedForward(numInputs, numHidden, numOutputs);

            Assert.IsNotNull(_selfNeuron.Brain);
            // FIX: Use the correct method to get the network
            var network = ((NeuralNetworkBrain)_selfNeuron.Brain).GetInternalNetwork();
            Assert.AreEqual(expectedNodes, network.Nodes.Count);
            Assert.AreEqual(expectedConnections, network.Connections.Count);
        }
        
        [TestMethod]
        public void API_CreateBrain_SimpleFeedForward_WithZeroHidden_ConnectsInputToOutput()
        {
            int numInputs = 4, numHidden = 0, numOutputs = 2;
            int expectedNodes = numInputs + numHidden + numOutputs;
            int expectedConnections = numInputs * numOutputs;
            
            _bridge.API_CreateBrain_SimpleFeedForward(numInputs, numHidden, numOutputs);

            Assert.IsNotNull(_selfNeuron.Brain);
            // FIX: Use the correct method to get the network
            var network = ((NeuralNetworkBrain)_selfNeuron.Brain).GetInternalNetwork();
            Assert.AreEqual(expectedNodes, network.Nodes.Count);
            Assert.AreEqual(expectedConnections, network.Connections.Count);
        }
        
        [TestMethod]
        public void API_CreateBrain_Competitive_BuildsCorrectTopology()
        {
            int numInputs = 3, numCompetitors = 4;
            int expectedNodes = numInputs + numCompetitors;
            // The number of lateral connections is complex due to cycle prevention.
            // We just need to check that some were made.
            int expectedForwardConnections = numInputs * numCompetitors; // 3 * 4 = 12
            
            _bridge.API_CreateBrain_Competitive(numInputs, numCompetitors);

            Assert.IsNotNull(_selfNeuron.Brain);
            // FIX: Use the correct method to get the network
            var network = ((NeuralNetworkBrain)_selfNeuron.Brain).GetInternalNetwork();
            Assert.AreEqual(expectedNodes, network.Nodes.Count);
            Assert.IsTrue(network.Connections.Count > expectedForwardConnections, "Lateral inhibition connections were not created.");
        }
        
        #endregion
    }
}
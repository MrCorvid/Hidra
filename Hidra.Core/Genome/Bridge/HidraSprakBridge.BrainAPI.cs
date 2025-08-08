// Hidra.Core/Genome/Bridge/HidraSprakBridge.BrainAPI.cs
using Hidra.Core.Brain;
using ProgrammingLanguageNr1;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hidra.Core
{
    public partial class HidraSprakBridge
    {
        #region Brain API - Type and Global Configuration

        [SprakAPI("Sets the brain type for the target neuron.", "brain_type (0=NeuralNetwork, 1=LogicGate)")]
        public void API_SetBrainType(float brainType)
        {
            var target = GetTargetNeuron();
            if (target == null) return;

            switch ((int)brainType)
            {
                case 0:
                    if (target.Brain is not NeuralNetworkBrain)
                        target.Brain = new NeuralNetworkBrain();
                    break;
                case 1:
                    if (target.Brain is not LogicGateBrain)
                        target.Brain = new LogicGateBrain();
                    break;
            }
        }

        [SprakAPI("Configures the properties of a LogicGateBrain.", "gate_type", "is_flipflop (0=No, 1=Yes)", "threshold")]
        public void API_ConfigureLogicGate(float gateType, float isFlipFlop, float threshold)
        {
            var target = GetTargetNeuron();
            if (target?.Brain is LogicGateBrain logicBrain)
            {
                bool useFlipFlop = isFlipFlop >= 1.0f;

                if (useFlipFlop)
                {
                    var ffType = (FlipFlopType)(int)gateType;
                    if (Enum.IsDefined(ffType)) logicBrain.FlipFlop = ffType;
                }
                else
                {
                    var lgType = (LogicGateType)(int)gateType;
                    if (Enum.IsDefined(lgType))
                    {
                        logicBrain.GateType = lgType;
                        logicBrain.FlipFlop = null;
                    }
                }
                logicBrain.Threshold = threshold;
            }
        }

        #endregion

        #region Brain API - Structure (Neural Network Specific)

        [SprakAPI("Removes all nodes and connections from the target neuron's brain.")]
        public void API_ClearBrain()
        {
            if (GetTargetNeuron()?.Brain is NeuralNetworkBrain nnBrain)
            {
                nnBrain.GetInternalNetwork().Clear();
            }
            else if (GetTargetNeuron()?.Brain is LogicGateBrain lgBrain)
            {
                lgBrain.ClearInputs();
            }
        }

        [SprakAPI("Adds a node to the target neuron's brain.", "node_type (0=Input, 1=Hidden, 2=Output)", "bias")]
        public float API_AddBrainNode(float nodeType, float bias)
        {
            if (GetTargetNeuron()?.Brain is NeuralNetworkBrain nnBrain)
            {
                var network = nnBrain.GetInternalNetwork();
                var type = (NNNodeType)(int)nodeType;
                if (!Enum.IsDefined(type)) return -1f;

                int nodeId = network.Nodes.Count > 0 ? network.Nodes.Keys.Max() + 1 : 0;
                var newNode = new NNNode(nodeId, type) { Bias = bias };
                network.AddNode(newNode);
                return newNode.Id;
            }
            return -1f;
        }

        [SprakAPI("Adds a connection between two existing nodes in the target neuron's brain.", "from_node_id", "to_node_id", "weight")]
        public void API_AddBrainConnection(float fromNodeId, float toNodeId, float weight)
        {
            if (GetTargetNeuron()?.Brain is NeuralNetworkBrain nnBrain)
            {
                try
                {
                    nnBrain.GetInternalNetwork().AddConnection(new NNConnection((int)fromNodeId, (int)toNodeId, weight));
                }
                catch (KeyNotFoundException) { /* Handled by NeuralNetwork class */ }
            }
        }

        [SprakAPI("Removes a node and its associated connections from the brain.", "node_id")]
        public void API_RemoveBrainNode(float nodeId)
        {
            if (GetTargetNeuron()?.Brain is NeuralNetworkBrain nnBrain)
            {
                nnBrain.GetInternalNetwork().RemoveNode((int)nodeId);
            }
        }

        [SprakAPI("Removes a connection between two nodes from the brain.", "from_node_id", "to_node_id")]
        public void API_RemoveBrainConnection(float fromNodeId, float toNodeId)
        {
            if (GetTargetNeuron()?.Brain is NeuralNetworkBrain nnBrain)
            {
                nnBrain.GetInternalNetwork().RemoveConnection((int)fromNodeId, (int)toNodeId);
            }
        }

        #endregion

        #region Brain API - Configuration (Neural Network Specific)

        [SprakAPI("Configures an output node's action type.", "node_id", "action_type (0=Move, 1=ExecuteGene, 2=SetOutputValue)")]
        public void API_ConfigureOutputNode(float nodeId, float actionType)
        {
            if (GetTargetNeuron()?.Brain is NeuralNetworkBrain nnBrain)
            {
                if (nnBrain.GetInternalNetwork().Nodes.TryGetValue((int)nodeId, out var node) && node.NodeType == NNNodeType.Output)
                {
                    if (Enum.IsDefined(typeof(OutputActionType), (int)actionType))
                    {
                        node.ActionType = (OutputActionType)(int)actionType;
                    }
                }
            }
        }

        [SprakAPI("Maps a brain input node to a data source from the world or neuron.", "node_id", "source_type", "source_index (for LVar/GVar)")]
        public void API_SetBrainInputSource(float nodeId, float sourceType, float sourceIndex)
        {
            // This can apply to both brain types
            var target = GetTargetNeuron();
            if (target == null) return;
            
            var srcType = (InputSourceType)(int)sourceType;
            if (!Enum.IsDefined(srcType)) return;

            if (target.Brain is NeuralNetworkBrain nnBrain)
            {
                if (nnBrain.GetInternalNetwork().Nodes.TryGetValue((int)nodeId, out var node) && node.NodeType == NNNodeType.Input)
                {
                    node.InputSource = srcType;
                    node.SourceIndex = (int)sourceIndex;
                }
            }
            else if (target.Brain is LogicGateBrain lgBrain)
            {
                // For a logic gate, node_id can be the input index (e.g., 0 for clock, 1 for J, 2 for K)
                // We will just add a new input source.
                lgBrain.AddInput(srcType, (int)sourceIndex);
            }
        }

        [SprakAPI("Sets the activation function for a specific node in the brain.", "node_id", "function_type (0=Tanh, 1=Linear, 2=Sigmoid, 3=ReLU)")]
        public void API_SetNodeActivationFunction(float nodeId, float functionType)
        {
            if (GetTargetNeuron()?.Brain is NeuralNetworkBrain nnBrain)
            {
                if (nnBrain.GetInternalNetwork().Nodes.TryGetValue((int)nodeId, out var node))
                {
                    var funcType = (ActivationFunctionType)(int)functionType;
                    if (Enum.IsDefined(funcType))
                    {
                        node.ActivationFunction = funcType;
                    }
                }
            }
        }

        [SprakAPI("Sets the weight of an existing connection in the brain.", "from_node_id", "to_node_id", "new_weight")]
        public void API_SetBrainConnectionWeight(float fromNodeId, float toNodeId, float newWeight)
        {
            if (GetTargetNeuron()?.Brain is NeuralNetworkBrain nnBrain)
            {
                var connection = nnBrain.GetInternalNetwork().Connections.FirstOrDefault(c => c.FromNodeId == (int)fromNodeId && c.ToNodeId == (int)toNodeId);
                if (connection != null)
                {
                    connection.Weight = newWeight;
                }
            }
        }

        [SprakAPI("Sets a specific property of a node in the brain.", "node_id", "property_id (0=Bias, 1=ActivationFunction)", "value")]
        public void API_SetBrainNodeProperty(float nodeId, float propertyId, float value)
        {
            if (GetTargetNeuron()?.Brain is NeuralNetworkBrain nnBrain)
            {
                if (nnBrain.GetInternalNetwork().Nodes.TryGetValue((int)nodeId, out var node))
                {
                    if (!Enum.IsDefined(typeof(BrainNodeProperty), (int)propertyId)) return;
                    var prop = (BrainNodeProperty)propertyId;

                    switch (prop)
                    {
                        case BrainNodeProperty.Bias:
                            node.Bias = value;
                            break;
                        // --- THIS IS THE CORRECTED LINE ---
                        case BrainNodeProperty.ActivationFunction:
                            var funcType = (ActivationFunctionType)(int)value;
                            if (Enum.IsDefined(funcType))
                            {
                                node.ActivationFunction = funcType;
                            }
                            break;
                    }
                }
            }
        }

        #endregion

        #region Brain API - Constructors (The "Hammers")

        [SprakAPI("Generates a standard fully-connected feed-forward network, replacing any existing brain.", "num_inputs", "num_outputs", "num_hidden_layers", "nodes_per_layer")]
        public void API_CreateBrain_SimpleFeedForward(float numInputs, float numOutputs, float numHiddenLayers, float nodesPerLayer)
        {
            var target = GetTargetNeuron();
            if (target == null) return;

            // This constructor creates a NeuralNetworkBrain.
            var nnBrain = new NeuralNetworkBrain();
            target.Brain = nnBrain;
            var network = nnBrain.GetInternalNetwork();

            int n_in = (int)numInputs, n_out = (int)numOutputs, n_hidden = (int)numHiddenLayers, n_per_layer = (int)nodesPerLayer;
            if (n_in <= 0 || n_out <= 0 || n_hidden < 0 || n_per_layer < 0) return;

            int nodeIdCounter = 0;
            var layers = new List<List<NNNode>>();

            layers.Add(CreateBrainLayer(network, ref nodeIdCounter, n_in, NNNodeType.Input));
            for (int i = 0; i < n_hidden; i++)
            {
                layers.Add(CreateBrainLayer(network, ref nodeIdCounter, n_per_layer, NNNodeType.Hidden));
            }
            layers.Add(CreateBrainLayer(network, ref nodeIdCounter, n_out, NNNodeType.Output));

            for (int i = 0; i < layers.Count - 1; i++)
            {
                ConnectBrainLayers(network, layers[i], layers[i+1], (p,c) => 1.0f);
            }
        }

        [SprakAPI("Generates a competitive 'winner-take-all' brain layer, replacing any existing brain.", "num_inputs", "num_competitors")]
        public void API_CreateBrain_Competitive(float numInputs, float numCompetitors)
        {
            var target = GetTargetNeuron();
            if (target == null) return;
            
            var nnBrain = new NeuralNetworkBrain();
            target.Brain = nnBrain;
            var network = nnBrain.GetInternalNetwork();

            int n_in = (int)numInputs, n_comp = (int)numCompetitors;
            if (n_in <= 0 || n_comp <= 1) return;

            int nodeIdCounter = 0;
            
            var inputNodes = CreateBrainLayer(network, ref nodeIdCounter, n_in, NNNodeType.Input);
            var competitorNodes = CreateBrainLayer(network, ref nodeIdCounter, n_comp, NNNodeType.Output, 0.5f);

            ConnectBrainLayers(network, inputNodes, competitorNodes, (p,c) => 1.0f);
            ConnectBrainLayers(network, competitorNodes, competitorNodes, (p,c) => p.Id == c.Id ? 0f : -2.0f);
        }

        #endregion

        #region Private Brain Construction Helpers

        private List<NNNode> CreateBrainLayer(NeuralNetwork brain, ref int nodeIdCounter, int nodeCount, NNNodeType type, float bias = 0f)
        {
            var layer = new List<NNNode>(nodeCount);
            for (int i = 0; i < nodeCount; i++)
            {
                var node = new NNNode(nodeIdCounter++, type) { Bias = bias };
                brain.AddNode(node);
                layer.Add(node);
            }
            return layer;
        }

        private void ConnectBrainLayers(NeuralNetwork brain, List<NNNode> fromLayer, List<NNNode> toLayer, Func<NNNode, NNNode, float> weightFunc)
        {
            foreach (var parent in fromLayer)
            {
                foreach (var child in toLayer)
                {
                    float weight = weightFunc(parent, child);
                    if (weight != 0f)
                    {
                        brain.AddConnection(new NNConnection(parent.Id, child.Id, weight));
                    }
                }
            }
        }
        
        #endregion
    }
}
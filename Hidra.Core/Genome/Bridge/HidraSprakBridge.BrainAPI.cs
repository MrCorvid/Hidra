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
            LogDbg("BRIDGE.BRAIN", $"API_SetBrainType(type={brainType}) -> target={target?.Id.ToString() ?? "null"}");
            if (target == null) return;

            try
            {
                IBrain newBrain;
                switch ((int)brainType)
                {
                    case 0:
                        if (target.Brain is NeuralNetworkBrain) { LogDbg("BRIDGE.BRAIN", "Already NeuralNetworkBrain"); return; }
                        newBrain = new NeuralNetworkBrain();
                        break;
                    case 1:
                        if (target.Brain is LogicGateBrain) { LogDbg("BRIDGE.BRAIN", "Already LogicGateBrain"); return; }
                        newBrain = new LogicGateBrain();
                        break;
                    default:
                        LogWarn("BRIDGE.BRAIN", $"Unknown brain type {(int)brainType}; no-op.");
                        return;
                }
                newBrain.SetPrng(_world.InternalRng);
                target.Brain = newBrain;
                LogDbg("BRIDGE.BRAIN", $"Brain set to {newBrain.GetType().Name}");
            }
            catch (Exception ex)
            {
                LogErr("BRIDGE.BRAIN", $"API_SetBrainType exception: {ex.Message}");
            }
        }

        [SprakAPI("Configures the properties of a LogicGateBrain.", "gate_type", "is_flipflop (0=No, 1=Yes)", "threshold")]
        public void API_ConfigureLogicGate(float gateType, float isFlipFlop, float threshold)
        {
            LogDbg("BRIDGE.BRAIN", $"API_ConfigureLogicGate(type={gateType}, flipflop={isFlipFlop}, thr={threshold})");
            if (GetTargetNeuron()?.Brain is LogicGateBrain logicBrain)
            {
                bool useFlipFlop = isFlipFlop >= 1.0f;
                int gateTypeValue = (int)gateType;

                if (useFlipFlop)
                {
                    if (Enum.IsDefined(typeof(FlipFlopType), gateTypeValue))
                    {
                        logicBrain.FlipFlop = (FlipFlopType)gateTypeValue;
                        LogDbg("BRIDGE.BRAIN", $"FlipFlop set to {logicBrain.FlipFlop}");
                    }
                    else
                    {
                        LogWarn("BRIDGE.BRAIN", $"Invalid FlipFlopType {gateTypeValue}");
                    }
                }
                else
                {
                    if (Enum.IsDefined(typeof(LogicGateType), gateTypeValue))
                    {
                        logicBrain.GateType = (LogicGateType)gateTypeValue;
                        logicBrain.FlipFlop = null;
                        LogDbg("BRIDGE.BRAIN", $"GateType set to {logicBrain.GateType}");
                    }
                    else
                    {
                        LogWarn("BRIDGE.BRAIN", $"Invalid LogicGateType {gateTypeValue}");
                    }
                }
                logicBrain.Threshold = threshold;
                LogDbg("BRIDGE.BRAIN", $"Threshold set to {threshold}");
            }
            else
            {
                LogWarn("BRIDGE.BRAIN", "ConfigureLogicGate called but target is not LogicGateBrain; ignored.");
            }
        }

        #endregion

        #region Brain API - Structure (Neural Network Specific)

        [SprakAPI("Removes all nodes and connections from the target neuron's brain.")]
        public void API_ClearBrain()
        {
            var target = GetTargetNeuron();
            LogDbg("BRIDGE.BRAIN", $"API_ClearBrain() -> target={target?.Id.ToString() ?? "null"}");

            if (target?.Brain is NeuralNetworkBrain nnBrain)
            {
                nnBrain.GetInternalNetwork().Clear();
                LogDbg("BRIDGE.BRAIN", "Neural network cleared.");
            }
            else if (target?.Brain is LogicGateBrain lgBrain)
            {
                lgBrain.ClearInputs();
                LogDbg("BRIDGE.BRAIN", "Logic gate inputs cleared.");
            }
            else
            {
                LogWarn("BRIDGE.BRAIN", "No brain present; nothing to clear.");
            }
        }

        [SprakAPI("Adds a node to the target neuron's brain.", "node_type (0=Input, 1=Hidden, 2=Output)", "bias")]
        public float API_AddBrainNode(float nodeType, float bias)
        {
            LogDbg("BRIDGE.BRAIN", $"API_AddBrainNode(type={nodeType}, bias={bias})");

            if (GetTargetNeuron()?.Brain is NeuralNetworkBrain nnBrain)
            {
                var network = nnBrain.GetInternalNetwork();
                int nodeTypeValue = (int)nodeType;
                if (!Enum.IsDefined(typeof(NNNodeType), nodeTypeValue))
                {
                    LogWarn("BRIDGE.BRAIN", $"Invalid NNNodeType {nodeTypeValue}; returning -1.");
                    return -1f;
                }

                var type = (NNNodeType)nodeTypeValue;
                int nodeId = network.Nodes.Count > 0 ? network.Nodes.Keys.Max() + 1 : 0;
                
                var newNode = new NNNode(nodeId, type) { Bias = bias };
                network.AddNode(newNode);

                LogDbg("BRIDGE.BRAIN", $"Added NN node id={nodeId}, type={type}, bias={bias}");
                return nodeId;
            }

            LogWarn("BRIDGE.BRAIN", "AddBrainNode called but target is not NeuralNetworkBrain; returning -1.");
            return -1f;
        }

        [SprakAPI("Adds a connection between two brain nodes.", "src_id", "tgt_id", "weight")]
        public void API_AddBrainConnection(float srcId, float tgtId, float weight)
        {
            LogDbg("BRIDGE.BRAIN", $"API_AddBrainConnection(src={srcId}, tgt={tgtId}, w={weight})");

            if (GetTargetNeuron()?.Brain is NeuralNetworkBrain nnBrain)
            {
                var network = nnBrain.GetInternalNetwork();
                var s = (int)srcId;
                var t = (int)tgtId;
                
                network.AddConnection(new NNConnection(s, t, weight));

                LogDbg("BRIDGE.BRAIN", $"Added NN connection {s}->{t} w={weight}");
            }
            else
            {
                LogWarn("BRIDGE.BRAIN", "AddBrainConnection called but target is not NeuralNetworkBrain; ignored.");
            }
        }

        #endregion

        #region Brain API - High-Level Constructors

        [SprakAPI("Constructs a simple, fully-connected feed-forward neural network, clearing any existing brain.", "input_count", "hidden_count", "output_count")]
        public void API_CreateBrain_SimpleFeedForward(float inputCount, float hiddenCount, float outputCount)
        {
            LogDbg("BRIDGE.BRAIN", $"API_CreateBrain_SimpleFeedForward(in={inputCount}, hidden={hiddenCount}, out={outputCount})");
            var target = GetTargetNeuron();
            if (target == null) return;

            if (target.Brain is not NeuralNetworkBrain)
            {
                var newBrain = new NeuralNetworkBrain();
                newBrain.SetPrng(_world.InternalRng);
                target.Brain = newBrain;
                LogDbg("BRIDGE.BRAIN", "Target was not NeuralNetworkBrain; created a new one.");
            }
            var nnBrain = (NeuralNetworkBrain)target.Brain;

            var network = nnBrain.GetInternalNetwork();
            network.Clear();

            var rng = _world.InternalRng;
            var inputIds = new List<int>();
            var hiddenIds = new List<int>();
            var outputIds = new List<int>();

            int nextId = 0;

            for (int i = 0; i < (int)inputCount; i++) { inputIds.Add(nextId); network.AddNode(new NNNode(nextId++, NNNodeType.Input)); }
            for (int i = 0; i < (int)hiddenCount; i++) { hiddenIds.Add(nextId); network.AddNode(new NNNode(nextId++, NNNodeType.Hidden) { Bias = (rng.NextFloat() - 0.5f) * 2f }); }
            for (int i = 0; i < (int)outputCount; i++) { outputIds.Add(nextId); network.AddNode(new NNNode(nextId++, NNNodeType.Output) { Bias = (rng.NextFloat() - 0.5f) * 2f }); }
            
            if (hiddenIds.Any())
            {
                foreach (var inputId in inputIds) { foreach (var hiddenId in hiddenIds) { network.AddConnection(new NNConnection(inputId, hiddenId, (rng.NextFloat() - 0.5f) * 2f)); } }
            }
            if (hiddenIds.Any() && outputIds.Any())
            {
                 foreach (var hiddenId in hiddenIds) { foreach (var outputId in outputIds) { network.AddConnection(new NNConnection(hiddenId, outputId, (rng.NextFloat() - 0.5f) * 2f)); } }
            }
            else if (!hiddenIds.Any() && outputIds.Any())
            {
                 foreach (var inputId in inputIds) { foreach (var outputId in outputIds) { network.AddConnection(new NNConnection(inputId, outputId, (rng.NextFloat() - 0.5f) * 2f)); } }
            }
            
            LogDbg("BRIDGE.BRAIN", $"Built SimpleFeedForward brain with {inputIds.Count} inputs, {hiddenIds.Count} hidden, {outputIds.Count} outputs.");
        }

        [SprakAPI("Constructs a competitive (winner-take-all) layer with lateral inhibition, clearing any existing brain.", "input_count", "competitive_count")]
        public void API_CreateBrain_Competitive(float inputCount, float competitiveCount)
        {
            LogDbg("BRIDGE.BRAIN", $"API_CreateBrain_Competitive(in={inputCount}, comp={competitiveCount})");
            var target = GetTargetNeuron();
            if (target == null) return;
            
            if (target.Brain is not NeuralNetworkBrain)
            {
                var newBrain = new NeuralNetworkBrain();
                newBrain.SetPrng(_world.InternalRng);
                target.Brain = newBrain;
                LogDbg("BRIDGE.BRAIN", "Target was not NeuralNetworkBrain; created a new one.");
            }
            var nnBrain = (NeuralNetworkBrain)target.Brain;

            var network = nnBrain.GetInternalNetwork();
            network.Clear();

            var rng = _world.InternalRng;
            var inputIds = new List<int>();
            var competitiveIds = new List<int>();

            int nextId = 0;

            for (int i = 0; i < (int)inputCount; i++) { inputIds.Add(nextId); network.AddNode(new NNNode(nextId++, NNNodeType.Input)); }
            for (int i = 0; i < (int)competitiveCount; i++) { competitiveIds.Add(nextId); network.AddNode(new NNNode(nextId++, NNNodeType.Hidden) { Bias = (rng.NextFloat() - 0.5f) * 2f }); }

            foreach (var inputId in inputIds) { foreach (var compId in competitiveIds) { network.AddConnection(new NNConnection(inputId, compId, rng.NextFloat())); } }
            foreach (var fromId in competitiveIds) { foreach (var toId in competitiveIds) { if (fromId != toId) { network.AddConnection(new NNConnection(fromId, toId, -1.0f)); } } }

            LogDbg("BRIDGE.BRAIN", $"Built Competitive brain with {inputIds.Count} inputs and {competitiveIds.Count} competing nodes.");
        }

        #endregion

        #region Implemented Brain API Functions

        private enum BrainNodeProperty { Bias = 0, ActivationFunction = 1 }
        private static readonly int _brainNodePropertyCount = Enum.GetValues(typeof(BrainNodeProperty)).Length;
        private static readonly int _outputActionTypeCount = Enum.GetValues(typeof(OutputActionType)).Length;
        private static readonly int _inputSourceTypeCount = Enum.GetValues(typeof(InputSourceType)).Length;
        private static readonly int _activationFunctionTypeCount = Enum.GetValues(typeof(ActivationFunctionType)).Length;

        [SprakAPI("Removes a node and its associated connections from the brain.", "node_id")]
        public void API_RemoveBrainNode(float nodeId)
        {
            LogDbg("BRIDGE.BRAIN", $"API_RemoveBrainNode(nodeId={nodeId})");
            if (GetTargetNeuron()?.Brain is NeuralNetworkBrain nnBrain)
            {
                nnBrain.GetInternalNetwork().RemoveNode((int)nodeId);
            }
        }

        [SprakAPI("Removes a connection between two nodes from the brain.", "from_node_id", "to_node_id")]
        public void API_RemoveBrainConnection(float fromNodeId, float toNodeId)
        {
            LogDbg("BRIDGE.BRAIN", $"API_RemoveBrainConnection(from={fromNodeId}, to={toNodeId})");
            if (GetTargetNeuron()?.Brain is NeuralNetworkBrain nnBrain)
            {
                nnBrain.GetInternalNetwork().RemoveConnection((int)fromNodeId, (int)toNodeId);
            }
        }

        [SprakAPI("Configures an output node's action type.", "node_id", "action_type")]
        public void API_ConfigureOutputNode(float nodeId, float actionType)
        {
            LogDbg("BRIDGE.BRAIN", $"API_ConfigureOutputNode(nodeId={nodeId}, actionType={actionType})");
            if (GetTargetNeuron()?.Brain is not NeuralNetworkBrain nnBrain) return;

            if (nnBrain.GetInternalNetwork().Nodes.TryGetValue((int)nodeId, out var node) && node.NodeType == NNNodeType.Output)
            {
                var resolvedActionType = (OutputActionType)((int)Math.Abs(actionType) % _outputActionTypeCount);
                node.ActionType = resolvedActionType;
            }
        }
        
        [SprakAPI("Maps a brain input node to a data source from the world or neuron.", "node_id", "source_type", "source_index")]
        public void API_SetBrainInputSource(float nodeId, float sourceType, float sourceIndex)
        {
            LogDbg("BRIDGE.BRAIN", $"API_SetBrainInputSource(nodeId={nodeId}, sourceType={sourceType}, sourceIndex={sourceIndex})");
            var target = GetTargetNeuron();
            if (target == null) return;
            
            var resolvedSrcType = (InputSourceType)((int)Math.Abs(sourceType) % _inputSourceTypeCount);

            if (target.Brain is NeuralNetworkBrain nnBrain)
            {
                if (nnBrain.GetInternalNetwork().Nodes.TryGetValue((int)nodeId, out var node) && node.NodeType == NNNodeType.Input)
                {
                    node.InputSource = resolvedSrcType;
                    node.SourceIndex = (int)sourceIndex;
                }
            }
            else if (target.Brain is LogicGateBrain lgBrain)
            {
                lgBrain.AddInput(resolvedSrcType, (int)sourceIndex);
            }
        }

        [SprakAPI("Sets the activation function for a specific node in the brain.", "node_id", "function_type")]
        public void API_SetNodeActivationFunction(float nodeId, float functionType)
        {
            LogDbg("BRIDGE.BRAIN", $"API_SetNodeActivationFunction(nodeId={nodeId}, functionType={functionType})");
            if (GetTargetNeuron()?.Brain is not NeuralNetworkBrain nnBrain) return;

            if (nnBrain.GetInternalNetwork().Nodes.TryGetValue((int)nodeId, out var node))
            {
                var resolvedFuncType = (ActivationFunctionType)((int)Math.Abs(functionType) % _activationFunctionTypeCount);
                node.ActivationFunction = resolvedFuncType;
            }
        }

        [SprakAPI("Sets the weight of an existing connection in the brain.", "from_node_id", "to_node_id", "new_weight")]
        public void API_SetBrainConnectionWeight(float fromNodeId, float toNodeId, float newWeight)
        {
            LogDbg("BRIDGE.BRAIN", $"API_SetBrainConnectionWeight(from={fromNodeId}, to={toNodeId}, weight={newWeight})");
            if (GetTargetNeuron()?.Brain is not NeuralNetworkBrain nnBrain) return;
            
            var connection = nnBrain.GetInternalNetwork().Connections
                .FirstOrDefault(c => c.FromNodeId == (int)fromNodeId && c.ToNodeId == (int)toNodeId);
                
            if (connection != null)
            {
                connection.Weight = newWeight;
            }
        }

        [SprakAPI("Sets a specific property of a node in the brain.", "node_id", "property_id (0=Bias, 1=ActivationFunction)", "value")]
        public void API_SetBrainNodeProperty(float nodeId, float propertyId, float value)
        {
            LogDbg("BRIDGE.BRAIN", $"API_SetBrainNodeProperty(nodeId={nodeId}, propId={propertyId}, val={value})");
            if (GetTargetNeuron()?.Brain is not NeuralNetworkBrain nnBrain) return;

            if (nnBrain.GetInternalNetwork().Nodes.TryGetValue((int)nodeId, out var node))
            {
                var prop = (BrainNodeProperty)((int)Math.Abs(propertyId) % _brainNodePropertyCount);
                
                switch (prop)
                {
                    case BrainNodeProperty.Bias:
                        node.Bias = value;
                        break;
                    case BrainNodeProperty.ActivationFunction:
                        var resolvedFuncType = (ActivationFunctionType)((int)Math.Abs(value) % _activationFunctionTypeCount);
                        node.ActivationFunction = resolvedFuncType;
                        break;
                }
            }
        }

        #endregion

        #region Brain API - Placeholders
        
        [SprakAPI("Constructs a sparsely connected feed-forward network.", "input_count", "hidden_count", "output_count", "connection_density")]
        public void API_CreateBrain_SparseFeedForward(float a, float b, float c, float d)
        {
            LogWarn("BRIDGE.BRAIN", "'API_CreateBrain_SparseFeedForward' is not yet implemented.");
        }

        [SprakAPI("Constructs an autoencoder network.", "visible_count", "hidden_count")]
        public void API_CreateBrain_Autoencoder(float a, float b)
        {
            LogWarn("BRIDGE.BRAIN", "'API_CreateBrain_Autoencoder' is not yet implemented.");
        }
        
        [SprakAPI("Constructs a feed-forward network with randomized topology.", "input_count", "hidden_count", "output_count", "randomness_factor")]
        public void API_CreateBrain_RandomizedFeedForward(float a, float b, float c, float d)
        {
            LogWarn("BRIDGE.BRAIN", "'API_CreateBrain_RandomizedFeedForward' is not yet implemented.");
        }

        #endregion
    }
}
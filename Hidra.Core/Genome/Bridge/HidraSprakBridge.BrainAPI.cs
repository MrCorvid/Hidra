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
        /// <summary>Finds a brain node using the hybrid lookup strategy.</summary>
        private NNNode? GetNodeByHybridLookup(NeuralNetworkBrain nnBrain, float nodeId, string apiName)
        {
            var network = nnBrain.GetInternalNetwork();
            if (!network.Nodes.Any())
            {
                LogWarn("BRIDGE.BRAIN", $"{apiName}: Target brain has no nodes to select from.");
                return null;
            }

            int intNodeId = (int)nodeId;

            // 1. Literal ID Check
            if (network.Nodes.TryGetValue(intNodeId, out var literalNode))
            {
                LogTrace("BRIDGE.BRAIN", $"{apiName}: Found node via literal ID {intNodeId}.");
                return literalNode;
            }
            else // 2. Relative Modulus Fallback
            {
                // --- FIX --- Replaced all instances of '.NodeId' with the correct property name '.Id'
                var orderedNodes = network.Nodes.Values.OrderBy(n => n.Id).ToList();
                int count = orderedNodes.Count;
                int safeIndex = (intNodeId % count + count) % count;
                var fallbackNode = orderedNodes[safeIndex];
                LogTrace("BRIDGE.BRAIN", $"{apiName}: Literal ID {intNodeId} not found. Used modulus fallback to get node {fallbackNode.Id} at index {safeIndex}.");
                return fallbackNode;
            }
        }

        #region Brain API - Type and Global Configuration
        [SprakAPI("Sets the brain type for the target neuron.", "brain_type (0=NeuralNetwork, 1=LogicGate)")]
        public void API_SetBrainType(float brainType)
        {
            LogTrace("BRIDGE.BRAIN", $"API_SetBrainType(type={brainType})");
            var target = GetTargetNeuron();
            if (target == null) return;

            try
            {
                int intBrainType = (int)brainType;
                // 1. Literal Enum Check (via switch cases)
                if (intBrainType < 0 || intBrainType > 1)
                {
                    // 2. Modulus Fallback
                    intBrainType = (intBrainType % 2 + 2) % 2;
                    LogTrace("BRIDGE.BRAIN", $"Literal brain_type invalid; fell back to {intBrainType}.");
                }

                IBrain newBrain;
                switch (intBrainType)
                {
                    case 0:
                        if (target.Brain is NeuralNetworkBrain) return;
                        newBrain = new NeuralNetworkBrain();
                        break;
                    case 1:
                        if (target.Brain is LogicGateBrain) return;
                        newBrain = new LogicGateBrain();
                        break;
                    default: // Should be unreachable due to modulus
                        return;
                }
                newBrain.SetPrng(_world.InternalRng);
                target.Brain = newBrain;
            }
            catch (Exception ex)
            {
                LogErr("BRIDGE.BRAIN", $"API_SetBrainType exception: {ex.Message}");
            }
        }

        [SprakAPI("Configures the properties of a LogicGateBrain.", "gate_type", "is_flipflop (0=No, 1=Yes)", "threshold")]
        public void API_ConfigureLogicGate(float gateType, float isFlipFlop, float threshold)
        {
            LogTrace("BRIDGE.BRAIN", $"API_ConfigureLogicGate(type={gateType}, flipflop={isFlipFlop}, thr={threshold})");
            if (GetTargetNeuron()?.Brain is LogicGateBrain logicBrain)
            {
                bool useFlipFlop = isFlipFlop >= 1.0f;
                int gateTypeValue = (int)gateType;

                if (useFlipFlop)
                {
                    // 1. Literal Enum Check
                    if (Enum.IsDefined(typeof(FlipFlopType), gateTypeValue))
                    {
                        logicBrain.FlipFlop = (FlipFlopType)gateTypeValue;
                    }
                    else // 2. Modulus Fallback
                    {
                        var fallbackType = (FlipFlopType)((gateTypeValue % Enum.GetValues(typeof(FlipFlopType)).Length + Enum.GetValues(typeof(FlipFlopType)).Length) % Enum.GetValues(typeof(FlipFlopType)).Length);
                        logicBrain.FlipFlop = fallbackType;
                        LogWarn("BRIDGE.BRAIN", $"Invalid FlipFlopType {gateTypeValue}, fell back to {fallbackType}.");
                    }
                }
                else
                {
                    // 1. Literal Enum Check
                    if (Enum.IsDefined(typeof(LogicGateType), gateTypeValue))
                    {
                        logicBrain.GateType = (LogicGateType)gateTypeValue;
                        logicBrain.FlipFlop = null;
                    }
                    else // 2. Modulus Fallback
                    {
                        var fallbackType = (LogicGateType)((gateTypeValue % Enum.GetValues(typeof(LogicGateType)).Length + Enum.GetValues(typeof(LogicGateType)).Length) % Enum.GetValues(typeof(LogicGateType)).Length);
                        logicBrain.GateType = fallbackType;
                        logicBrain.FlipFlop = null;
                        LogWarn("BRIDGE.BRAIN", $"Invalid LogicGateType {gateTypeValue}, fell back to {fallbackType}.");
                    }
                }
                logicBrain.Threshold = threshold;
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
            LogTrace("BRIDGE.BRAIN", "API_ClearBrain()");
            var target = GetTargetNeuron();

            if (target?.Brain is NeuralNetworkBrain nnBrain)
            {
                nnBrain.GetInternalNetwork().Clear();
            }
            else if (target?.Brain is LogicGateBrain lgBrain)
            {
                lgBrain.ClearInputs();
            }
        }

        [SprakAPI("Adds a node to the target neuron's brain.", "node_type (0=Input, 1=Hidden, 2=Output)", "bias")]
        public float API_AddBrainNode(float nodeType, float bias)
        {
            LogTrace("BRIDGE.BRAIN", $"API_AddBrainNode(type={nodeType}, bias={bias})");

            if (GetTargetNeuron()?.Brain is NeuralNetworkBrain nnBrain)
            {
                var network = nnBrain.GetInternalNetwork();
                int nodeTypeValue = (int)nodeType;
                NNNodeType resolvedType;

                // 1. Literal Enum Check
                if (Enum.IsDefined(typeof(NNNodeType), nodeTypeValue))
                {
                    resolvedType = (NNNodeType)nodeTypeValue;
                }
                else // 2. Modulus Fallback
                {
                    int count = Enum.GetValues(typeof(NNNodeType)).Length;
                    resolvedType = (NNNodeType)((nodeTypeValue % count + count) % count);
                    LogWarn("BRIDGE.BRAIN", $"Invalid NNNodeType {nodeTypeValue}, fell back to {resolvedType}.");
                }
                
                int nodeId = network.Nodes.Count > 0 ? network.Nodes.Keys.Max() + 1 : 0;
                
                var newNode = new NNNode(nodeId, resolvedType) { Bias = bias };
                network.AddNode(newNode);
                
                return nodeId;
            }

            LogWarn("BRIDGE.BRAIN", "AddBrainNode called but target is not NeuralNetworkBrain; returning -1.");
            return -1f;
        }

        [SprakAPI("Adds a connection between two brain nodes.", "src_id", "tgt_id", "weight")]
        public void API_AddBrainConnection(float srcId, float tgtId, float weight)
        {
            LogTrace("BRIDGE.BRAIN", $"API_AddBrainConnection(src={srcId}, tgt={tgtId}, w={weight})");

            if (GetTargetNeuron()?.Brain is NeuralNetworkBrain nnBrain)
            {
                var sourceNode = GetNodeByHybridLookup(nnBrain, srcId, "API_AddBrainConnection (source)");
                var targetNode = GetNodeByHybridLookup(nnBrain, tgtId, "API_AddBrainConnection (target)");

                if (sourceNode != null && targetNode != null)
                {
                    var network = nnBrain.GetInternalNetwork();
                    // --- FIX --- Replaced '.NodeId' with '.Id'
                    network.AddConnection(new NNConnection(sourceNode.Id, targetNode.Id, weight));
                }
                else
                {
                    LogWarn("BRIDGE.BRAIN", "Could not resolve one or both nodes for AddBrainConnection; ignored.");
                }
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
            LogTrace("BRIDGE.BRAIN", $"API_CreateBrain_SimpleFeedForward(in={inputCount}, hidden={hiddenCount}, out={outputCount})");
            var target = GetTargetNeuron();
            if (target == null) return;

            if (target.Brain is not NeuralNetworkBrain)
            {
                var newBrain = new NeuralNetworkBrain();
                newBrain.SetPrng(_world.InternalRng);
                target.Brain = newBrain;
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
        }

        [SprakAPI("Constructs a competitive (winner-take-all) layer with lateral inhibition, clearing any existing brain.", "input_count", "competitive_count")]
        public void API_CreateBrain_Competitive(float inputCount, float competitiveCount)
        {
            LogTrace("BRIDGE.BRAIN", $"API_CreateBrain_Competitive(in={inputCount}, comp={competitiveCount})");
            var target = GetTargetNeuron();
            if (target == null) return;
            
            if (target.Brain is not NeuralNetworkBrain)
            {
                var newBrain = new NeuralNetworkBrain();
                newBrain.SetPrng(_world.InternalRng);
                target.Brain = newBrain;
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
            LogTrace("BRIDGE.BRAIN", $"API_RemoveBrainNode(nodeId={nodeId})");
            if (GetTargetNeuron()?.Brain is NeuralNetworkBrain nnBrain)
            {
                var nodeToRemove = GetNodeByHybridLookup(nnBrain, nodeId, "API_RemoveBrainNode");
                if (nodeToRemove != null)
                {
                    // --- FIX --- Replaced '.NodeId' with '.Id'
                    nnBrain.GetInternalNetwork().RemoveNode(nodeToRemove.Id);
                }
            }
        }

        [SprakAPI("Removes a connection between two nodes from the brain.", "from_node_id", "to_node_id")]
        public void API_RemoveBrainConnection(float fromNodeId, float toNodeId)
        {
            LogTrace("BRIDGE.BRAIN", $"API_RemoveBrainConnection(from={fromNodeId}, to={toNodeId})");
            if (GetTargetNeuron()?.Brain is NeuralNetworkBrain nnBrain)
            {
                var fromNode = GetNodeByHybridLookup(nnBrain, fromNodeId, "API_RemoveBrainConnection (from)");
                var toNode = GetNodeByHybridLookup(nnBrain, toNodeId, "API_RemoveBrainConnection (to)");
                if (fromNode != null && toNode != null)
                {
                    // --- FIX --- Replaced '.NodeId' with '.Id'
                    nnBrain.GetInternalNetwork().RemoveConnection(fromNode.Id, toNode.Id);
                }
            }
        }

        [SprakAPI("Configures an output node's action type.", "node_id", "action_type")]
        public void API_ConfigureOutputNode(float nodeId, float actionType)
        {
            LogTrace("BRIDGE.BRAIN", $"API_ConfigureOutputNode(nodeId={nodeId}, actionType={actionType})");
            if (GetTargetNeuron()?.Brain is not NeuralNetworkBrain nnBrain) return;

            var node = GetNodeByHybridLookup(nnBrain, nodeId, "API_ConfigureOutputNode");

            if (node != null && node.NodeType == NNNodeType.Output)
            {
                int intActionType = (int)actionType;
                OutputActionType resolvedActionType;

                if (Enum.IsDefined(typeof(OutputActionType), intActionType))
                {
                    resolvedActionType = (OutputActionType)intActionType;
                }
                else
                {
                    resolvedActionType = (OutputActionType)((intActionType % _outputActionTypeCount + _outputActionTypeCount) % _outputActionTypeCount);
                }
                node.ActionType = resolvedActionType;
            }
        }
        
        [SprakAPI("Maps a brain input node to a data source from the world or neuron.", "node_id", "source_type", "source_index")]
        public void API_SetBrainInputSource(float nodeId, float sourceType, float sourceIndex)
        {
            LogTrace("BRIDGE.BRAIN", $"API_SetBrainInputSource(nodeId={nodeId}, sourceType={sourceType}, sourceIndex={sourceIndex})");
            var target = GetTargetNeuron();
            if (target == null) return;
            
            int intSourceType = (int)sourceType;
            InputSourceType resolvedSrcType;

            // First, check if the provided type is a valid, defined enum member.
            if (Enum.IsDefined(typeof(InputSourceType), intSourceType))
            {
                resolvedSrcType = (InputSourceType)intSourceType;
            }
            else // If not, fall back to a modulus to guarantee a valid type.
            {
                resolvedSrcType = (InputSourceType)((intSourceType % _inputSourceTypeCount + _inputSourceTypeCount) % _inputSourceTypeCount);
                LogTrace("BRIDGE.BRAIN", $"Literal sourceType {intSourceType} invalid; fell back to {resolvedSrcType}.");
            }
            
            int finalSourceIndex = (int)sourceIndex;
            int sourceCollectionCount = 0;

            switch (resolvedSrcType)
            {
                case InputSourceType.GlobalHormone:
                    sourceCollectionCount = _world.GetGlobalHormonesDirect().Length;
                    break;
                case InputSourceType.LocalVariable:
                    sourceCollectionCount = target.LocalVariables.Length;
                    break;
                case InputSourceType.SynapseValue:
                    // Use our new public API method to get the count for the modulus.
                    sourceCollectionCount = _world.GetIncomingSynapses(target).Count;
                    break;
            }

            if (sourceCollectionCount > 0)
            {
                finalSourceIndex = (finalSourceIndex % sourceCollectionCount + sourceCollectionCount) % sourceCollectionCount;
            }

            if (target.Brain is NeuralNetworkBrain nnBrain)
            {
                var node = GetNodeByHybridLookup(nnBrain, nodeId, "API_SetBrainInputSource");
                if (node != null && node.NodeType == NNNodeType.Input)
                {
                    node.InputSource = resolvedSrcType;
                    node.SourceIndex = finalSourceIndex;
                }
            }
            else if (target.Brain is LogicGateBrain lgBrain)
            {
                lgBrain.AddInput(resolvedSrcType, finalSourceIndex);
            }
        }

        [SprakAPI("Sets the activation function for a specific node in the brain.", "node_id", "function_type")]
        public void API_SetNodeActivationFunction(float nodeId, float functionType)
        {
            LogTrace("BRIDGE.BRAIN", $"API_SetNodeActivationFunction(nodeId={nodeId}, functionType={functionType})");
            if (GetTargetNeuron()?.Brain is not NeuralNetworkBrain nnBrain) return;

            var node = GetNodeByHybridLookup(nnBrain, nodeId, "API_SetNodeActivationFunction");
            if (node != null)
            {
                int intFuncType = (int)functionType;
                ActivationFunctionType resolvedFuncType;
                if (Enum.IsDefined(typeof(ActivationFunctionType), intFuncType))
                {
                    resolvedFuncType = (ActivationFunctionType)intFuncType;
                }
                else
                {
                    resolvedFuncType = (ActivationFunctionType)((intFuncType % _activationFunctionTypeCount + _activationFunctionTypeCount) % _activationFunctionTypeCount);
                }
                node.ActivationFunction = resolvedFuncType;
            }
        }

        [SprakAPI("Sets the weight of an existing connection in the brain.", "from_node_id", "to_node_id", "new_weight")]
        public void API_SetBrainConnectionWeight(float fromNodeId, float toNodeId, float newWeight)
        {
            LogTrace("BRIDGE.BRAIN", $"API_SetBrainConnectionWeight(from={fromNodeId}, to={toNodeId}, weight={newWeight})");
            if (GetTargetNeuron()?.Brain is not NeuralNetworkBrain nnBrain) return;

            var fromNode = GetNodeByHybridLookup(nnBrain, fromNodeId, "API_SetBrainConnectionWeight (from)");
            var toNode = GetNodeByHybridLookup(nnBrain, toNodeId, "API_SetBrainConnectionWeight (to)");

            if (fromNode == null || toNode == null) return;
            
            // --- FIX --- Replaced '.NodeId' with '.Id'
            var connection = nnBrain.GetInternalNetwork().Connections
                .FirstOrDefault(c => c.FromNodeId == fromNode.Id && c.ToNodeId == toNode.Id);
                
            if (connection != null)
            {
                connection.Weight = newWeight;
            }
        }

        [SprakAPI("Sets a specific property of a node in the brain.", "node_id", "property_id (0=Bias, 1=ActivationFunction)", "value")]
        public void API_SetBrainNodeProperty(float nodeId, float propertyId, float value)
        {
            LogTrace("BRIDGE.BRAIN", $"API_SetBrainNodeProperty(nodeId={nodeId}, propId={propertyId}, val={value})");
            if (GetTargetNeuron()?.Brain is not NeuralNetworkBrain nnBrain) return;

            var node = GetNodeByHybridLookup(nnBrain, nodeId, "API_SetBrainNodeProperty");
            if (node != null)
            {
                int intPropId = (int)propertyId;
                BrainNodeProperty prop;
                if (Enum.IsDefined(typeof(BrainNodeProperty), intPropId))
                {
                    prop = (BrainNodeProperty)intPropId;
                }
                else
                {
                    prop = (BrainNodeProperty)((intPropId % _brainNodePropertyCount + _brainNodePropertyCount) % _brainNodePropertyCount);
                }
                
                switch (prop)
                {
                    case BrainNodeProperty.Bias:
                        node.Bias = value;
                        break;
                    case BrainNodeProperty.ActivationFunction:
                        int intFuncType = (int)value;
                        ActivationFunctionType resolvedFuncType;
                        if (Enum.IsDefined(typeof(ActivationFunctionType), intFuncType))
                        {
                            resolvedFuncType = (ActivationFunctionType)intFuncType;
                        }
                        else
                        {
                            resolvedFuncType = (ActivationFunctionType)((intFuncType % _activationFunctionTypeCount + _activationFunctionTypeCount) % _activationFunctionTypeCount);
                        }
                        node.ActivationFunction = resolvedFuncType;
                        break;
                }
            }
        }

        #endregion

        #region Brain API - Advanced Constructors

        /// <summary>
        /// Helper to prepare a target neuron for brain replacement.
        /// Returns the internal network and the RNG, or null/null if invalid.
        /// </summary>
        private (NeuralNetwork? net, IPrng? rng) PrepareBrainForReset()
        {
            var target = GetTargetNeuron();
            if (target == null) return (null, null);

            // Ensure the brain is a NeuralNetwork
            if (target.Brain is not NeuralNetworkBrain)
            {
                var newBrain = new NeuralNetworkBrain();
                newBrain.SetPrng(_world.InternalRng);
                target.Brain = newBrain;
            }

            var nnBrain = (NeuralNetworkBrain)target.Brain;
            var network = nnBrain.GetInternalNetwork();
            network.Clear(); // Wipe the slate clean

            return (network, _world.InternalRng);
        }

        [SprakAPI("Constructs a sparsely connected feed-forward network. Density is 0.0 to 1.0.", 
                  "input_count", "hidden_count", "output_count", "density")]
        public void API_CreateBrain_SparseFeedForward(float inputCountF, float hiddenCountF, float outputCountF, float density)
        {
            LogTrace("BRIDGE.BRAIN", $"API_CreateBrain_SparseFeedForward(in={inputCountF}, hid={hiddenCountF}, out={outputCountF}, dens={density})");
            
            lock (_world.SyncRoot)
            {
                var (network, rng) = PrepareBrainForReset();
                if (network == null || rng == null) return;

                int inputs = Math.Max(0, (int)inputCountF);
                int hidden = Math.Max(0, (int)hiddenCountF);
                int outputs = Math.Max(0, (int)outputCountF);
                float denseProb = Math.Clamp(density, 0.01f, 1.0f);

                int nextId = 0;
                var inputIds = new List<int>();
                var hiddenIds = new List<int>();
                var outputIds = new List<int>();

                // 1. Create Nodes
                for (int i = 0; i < inputs; i++) 
                { 
                    int id = nextId++; 
                    inputIds.Add(id); 
                    network.AddNode(new NNNode(id, NNNodeType.Input)); 
                }
                for (int i = 0; i < hidden; i++) 
                { 
                    int id = nextId++; 
                    hiddenIds.Add(id); 
                    // Random bias [-1, 1]
                    network.AddNode(new NNNode(id, NNNodeType.Hidden) { Bias = (rng.NextFloat() * 2f) - 1f }); 
                }
                for (int i = 0; i < outputs; i++) 
                { 
                    int id = nextId++; 
                    outputIds.Add(id); 
                    network.AddNode(new NNNode(id, NNNodeType.Output) { Bias = (rng.NextFloat() * 2f) - 1f }); 
                }

                // 2. Create Connections (Input -> Hidden)
                if (hidden > 0)
                {
                    foreach (var src in inputIds)
                    {
                        foreach (var dst in hiddenIds)
                        {
                            if (rng.NextFloat() < denseProb)
                            {
                                network.AddConnection(new NNConnection(src, dst, (rng.NextFloat() * 2f) - 1f));
                            }
                        }
                    }
                    // 3. Create Connections (Hidden -> Output)
                    foreach (var src in hiddenIds)
                    {
                        foreach (var dst in outputIds)
                        {
                            if (rng.NextFloat() < denseProb)
                            {
                                network.AddConnection(new NNConnection(src, dst, (rng.NextFloat() * 2f) - 1f));
                            }
                        }
                    }
                }
                else
                {
                    // Direct Input -> Output if no hidden layer
                    foreach (var src in inputIds)
                    {
                        foreach (var dst in outputIds)
                        {
                            if (rng.NextFloat() < denseProb)
                            {
                                network.AddConnection(new NNConnection(src, dst, (rng.NextFloat() * 2f) - 1f));
                            }
                        }
                    }
                }
            }
        }

        [SprakAPI("Constructs an Autoencoder style network (Input -> Hidden -> Output). Output count matches Visible count.", 
                  "visible_count", "hidden_count")]
        public void API_CreateBrain_Autoencoder(float visibleCountF, float hiddenCountF)
        {
            LogTrace("BRIDGE.BRAIN", $"API_CreateBrain_Autoencoder(vis={visibleCountF}, hid={hiddenCountF})");

            lock (_world.SyncRoot)
            {
                var (network, rng) = PrepareBrainForReset();
                if (network == null || rng == null) return;

                int visible = Math.Max(1, (int)visibleCountF);
                int hidden = Math.Max(1, (int)hiddenCountF);

                int nextId = 0;
                var inputIds = new List<int>();
                var hiddenIds = new List<int>();
                var outputIds = new List<int>();

                // 1. Create Topology
                for (int i = 0; i < visible; i++)
                {
                    int id = nextId++;
                    inputIds.Add(id);
                    network.AddNode(new NNNode(id, NNNodeType.Input));
                }
                for (int i = 0; i < hidden; i++)
                {
                    int id = nextId++;
                    hiddenIds.Add(id);
                    network.AddNode(new NNNode(id, NNNodeType.Hidden) { Bias = (rng.NextFloat() * 2f) - 1f });
                }
                for (int i = 0; i < visible; i++) // Output count == Visible count
                {
                    int id = nextId++;
                    outputIds.Add(id);
                    network.AddNode(new NNNode(id, NNNodeType.Output) { Bias = (rng.NextFloat() * 2f) - 1f });
                }

                // 2. Fully Connect (Input -> Hidden)
                foreach (var src in inputIds)
                {
                    foreach (var dst in hiddenIds)
                    {
                        network.AddConnection(new NNConnection(src, dst, (rng.NextFloat() * 2f) - 1f));
                    }
                }

                // 3. Fully Connect (Hidden -> Output)
                foreach (var src in hiddenIds)
                {
                    foreach (var dst in outputIds)
                    {
                        network.AddConnection(new NNConnection(src, dst, (rng.NextFloat() * 2f) - 1f));
                    }
                }
            }
        }

        [SprakAPI("Constructs a Feed-Forward network with randomized internal topology (no cycles).", 
                  "input_count", "hidden_count", "output_count", "randomness_factor")]
        public void API_CreateBrain_RandomizedFeedForward(float inputCountF, float hiddenCountF, float outputCountF, float randomness)
        {
            LogTrace("BRIDGE.BRAIN", $"API_CreateBrain_RandomizedFeedForward(in={inputCountF}, hid={hiddenCountF}, out={outputCountF}, rnd={randomness})");

            lock (_world.SyncRoot)
            {
                var (network, rng) = PrepareBrainForReset();
                if (network == null || rng == null) return;

                int inputs = Math.Max(0, (int)inputCountF);
                int hidden = Math.Max(0, (int)hiddenCountF);
                int outputs = Math.Max(0, (int)outputCountF);
                float prob = Math.Clamp(randomness, 0.05f, 1.0f);

                int nextId = 0;
                var inputIds = new List<int>();
                var hiddenIds = new List<int>();
                var outputIds = new List<int>();

                // 1. Create Nodes
                for (int i = 0; i < inputs; i++)
                {
                    int id = nextId++;
                    inputIds.Add(id);
                    network.AddNode(new NNNode(id, NNNodeType.Input));
                }
                
                // Note: We store hidden nodes in a list. We will only allow connections from index i -> j where i < j.
                // This strict ordering guarantees NO cycles, satisfying the NeuralNetwork safety check efficiently.
                for (int i = 0; i < hidden; i++)
                {
                    int id = nextId++;
                    hiddenIds.Add(id);
                    network.AddNode(new NNNode(id, NNNodeType.Hidden) { Bias = (rng.NextFloat() * 2f) - 1f });
                }

                for (int i = 0; i < outputs; i++)
                {
                    int id = nextId++;
                    outputIds.Add(id);
                    network.AddNode(new NNNode(id, NNNodeType.Output) { Bias = (rng.NextFloat() * 2f) - 1f });
                }

                // 2. Inputs -> Any Hidden (Random)
                foreach (var src in inputIds)
                {
                    foreach (var dst in hiddenIds)
                    {
                        if (rng.NextFloat() < prob)
                            network.AddConnection(new NNConnection(src, dst, (rng.NextFloat() * 2f) - 1f));
                    }
                }

                // 3. Hidden -> Downstream Hidden (Random, Forward-Only)
                for (int i = 0; i < hiddenIds.Count; i++)
                {
                    for (int j = i + 1; j < hiddenIds.Count; j++)
                    {
                        if (rng.NextFloat() < prob)
                            network.AddConnection(new NNConnection(hiddenIds[i], hiddenIds[j], (rng.NextFloat() * 2f) - 1f));
                    }
                }

                // 4. Any Hidden -> Any Output (Random)
                foreach (var src in hiddenIds)
                {
                    foreach (var dst in outputIds)
                    {
                        if (rng.NextFloat() < prob)
                            network.AddConnection(new NNConnection(src, dst, (rng.NextFloat() * 2f) - 1f));
                    }
                }

                // 5. Direct Input -> Output (Low probability, just to ensure connectivity if hidden is bypassed)
                // We use prob * 0.5 to make direct connections rarer in this specific architecture
                foreach (var src in inputIds)
                {
                    foreach (var dst in outputIds)
                    {
                        if (rng.NextFloat() < (prob * 0.5f))
                            network.AddConnection(new NNConnection(src, dst, (rng.NextFloat() * 2f) - 1f));
                    }
                }
            }
        }

        #endregion
    }
}
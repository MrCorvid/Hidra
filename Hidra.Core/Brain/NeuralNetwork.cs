// Hidra.Core/Brain/NeuralNetwork.cs
using Hidra.Core.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hidra.Core.Brain
{
    /// <summary>
    /// Represents a feed-forward neural network that governs a neuron's behavior.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This network is evaluated using a topological sort of its nodes, ensuring that each node's
    /// value is calculated before it is used as input for subsequent nodes. This structure inherently
    /// prevents cycles. The result of the topological sort is cached for performance and only
    /// recalculated when the network's structure (nodes or connections) changes.
    /// </para>
    /// <para>
    /// **THREAD-SAFETY WARNING:** This class is **NOT THREAD-SAFE**. It is designed to be owned
    /// by a single Neuron and operated on by the main simulation thread. All mutation methods
    /// (`AddNode`, `AddConnection`, `RemoveNode`, etc.) and the `Evaluate` method must be
    /// synchronized externally if used in a concurrent environment.
    /// </para>
    /// </remarks>
    public class NeuralNetwork
    {
        [JsonProperty]
        private readonly Dictionary<int, NNNode> _nodes = new();
        [JsonProperty]
        private readonly List<NNConnection> _connections = new();

        // Runtime caches derived from the serialized collections. They are rebuilt after deserialization.
        [JsonIgnore]
        private readonly List<NNNode> _inputNodes = new();
        [JsonIgnore]
        private readonly List<NNNode> _outputNodes = new();
        [JsonIgnore]
        private readonly Dictionary<int, List<NNConnection>> _outgoingConnections = new();
        
        // The cached result of the topological sort. A null value indicates the cache is "dirty" and needs rebuilding.
        [JsonIgnore]
        private List<NNNode>? _sortedNodes;
        
        // Public, read-only accessors for the network's state.
        [JsonIgnore]
        public IReadOnlyDictionary<int, NNNode> Nodes => _nodes;
        [JsonIgnore]
        public IReadOnlyList<NNConnection> Connections => _connections;
        [JsonIgnore]
        public IReadOnlyList<NNNode> InputNodes => _inputNodes;
        [JsonIgnore]
        public IReadOnlyList<NNNode> OutputNodes => _outputNodes;

        /// <summary>
        /// Rebuilds the non-serialized, cached fields after the network has been loaded from a save state.
        /// This method must be called after deserialization to ensure the network is in a valid, runnable state.
        /// </summary>
        public void InitializeFromLoad()
        {
            Logger.Log("NN_BRAIN", LogLevel.Debug, "Initializing brain from loaded state...");
            _inputNodes.Clear();
            _outputNodes.Clear();
            _outgoingConnections.Clear();
            _sortedNodes = null; // Invalidate the sort cache.

            foreach (var node in _nodes.Values)
            {
                if (node.NodeType == NNNodeType.Input) _inputNodes.Add(node);
                if (node.NodeType == NNNodeType.Output) _outputNodes.Add(node);
            }

            foreach (var conn in _connections)
            {
                if (!_outgoingConnections.TryGetValue(conn.FromNodeId, out var connList))
                {
                    connList = new List<NNConnection>();
                    _outgoingConnections[conn.FromNodeId] = connList;
                }
                connList.Add(conn);
            }
            Logger.Log("NN_BRAIN", LogLevel.Info, $"Brain re-initialized. {_nodes.Count} nodes ({_inputNodes.Count} inputs, {_outputNodes.Count} outputs), {_connections.Count} connections.");
        }
        
        /// <summary>
        /// Adds a node to the neural network.
        /// </summary>
        /// <param name="node">The node to add.</param>
        public void AddNode(NNNode node)
        {
            if (_nodes.TryAdd(node.Id, node))
            {
                if (node.NodeType == NNNodeType.Input) _inputNodes.Add(node);
                if (node.NodeType == NNNodeType.Output) _outputNodes.Add(node);
                _sortedNodes = null; // Invalidate cache, as the graph structure has changed.
                Logger.Log("NN_BRAIN", LogLevel.Debug, $"Added node {node.Id} of type {node.NodeType}.");
            }
        }

        /// <summary>
        /// Adds a connection between two existing nodes in the network.
        /// </summary>
        /// <param name="connection">The connection to add.</param>
        /// <exception cref="KeyNotFoundException">Thrown if the source or target node does not exist in the network.</exception>
        public void AddConnection(NNConnection connection)
        {
            if (!_nodes.ContainsKey(connection.FromNodeId) || !_nodes.ContainsKey(connection.ToNodeId))
            {
                string errorMsg = $"AddConnection failed: Node(s) not found for connection {connection.FromNodeId}->{connection.ToNodeId}.";
                Logger.Log("NN_BRAIN", LogLevel.Warning, errorMsg);
                throw new KeyNotFoundException(errorMsg);
            }

            _connections.Add(connection);

            if (!_outgoingConnections.TryGetValue(connection.FromNodeId, out var connList))
            {
                connList = new List<NNConnection>();
                _outgoingConnections[connection.FromNodeId] = connList;
            }
            connList.Add(connection);
            _sortedNodes = null; // Invalidate cache, as the graph structure has changed.
            Logger.Log("NN_BRAIN", LogLevel.Debug, $"Added connection {connection.FromNodeId}->{connection.ToNodeId} with weight {connection.Weight}.");
        }

        /// <summary>
        /// Evaluates the network in a feed-forward manner. This method populates the `Value` property
        /// on all nodes, particularly the output nodes. It does not execute any actions.
        /// </summary>
        /// <param name="inputs">An array of input values, which must match the order and count of the network's input nodes.</param>
        /// <exception cref="ArgumentException">Thrown if the number of provided inputs does not match the number of input nodes.</exception>
        /// <exception cref="InvalidOperationException">Thrown if a cycle is detected in the network graph during topological sort.</exception>
        public void Evaluate(IReadOnlyList<float> inputs)
        {
            if (inputs.Count != _inputNodes.Count)
            {
                string errorMsg = $"Evaluation failed: Mismatched input count. Expected {_inputNodes.Count}, got {inputs.Count}.";
                Logger.Log("NN_BRAIN", LogLevel.Error, errorMsg);
                throw new ArgumentException(errorMsg);
            }
            
            Logger.Log("NN_BRAIN", LogLevel.Debug, "Evaluating network...");

            if (_sortedNodes == null)
            {
                Logger.Log("NN_BRAIN", LogLevel.Info, "Topological sort cache is dirty, recalculating...");
                _sortedNodes = TopologicalSort();
            }

            // Step 1: Initialize the value of all nodes to their bias.
            foreach (var node in _nodes.Values)
            {
                node.Value = node.Bias;
            }

            // Step 2: Apply the external input values to the input nodes, adding to their bias.
            for (int i = 0; i < _inputNodes.Count; i++)
            {
                _inputNodes[i].Value += inputs[i];
            }

            // Step 3: Propagate values through the topologically sorted graph.
            foreach (var node in _sortedNodes)
            {
                float activatedValue;

                // For hidden and output nodes, apply the configured activation function.
                // Input nodes pass their value through directly (linear activation).
                if (node.NodeType == NNNodeType.Input)
                {
                    activatedValue = node.Value;
                }
                else
                {
                    activatedValue = node.ActivationFunction switch
                    {
                        ActivationFunctionType.Linear => node.Value,
                        ActivationFunctionType.Sigmoid => 1.0f / (1.0f + (float)Math.Exp(-node.Value)),
                        ActivationFunctionType.ReLU => Math.Max(0, node.Value),
                        ActivationFunctionType.Tanh or _ => (float)Math.Tanh(node.Value),
                    };
                    node.Value = activatedValue;
                }
                
                Logger.Log("NN_BRAIN", LogLevel.Debug, $"  Node {node.Id} ({node.NodeType}) final value: {node.Value}");

                // Add the node's final activated value to all its downstream neighbors, scaled by connection weight.
                if (_outgoingConnections.TryGetValue(node.Id, out var connections))
                {
                    foreach (var conn in connections)
                    {
                        _nodes[conn.ToNodeId].Value += activatedValue * conn.Weight;
                    }
                }
            }
            Logger.Log("NN_BRAIN", LogLevel.Debug, "Network evaluation complete.");
        }

        /// <summary>
        /// Performs a topological sort on the network graph using Kahn's algorithm.
        /// This is required for correct feed-forward evaluation and also serves to detect cycles.
        /// </summary>
        /// <returns>A list of nodes in a valid evaluation order.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the graph contains a cycle.</exception>
        private List<NNNode> TopologicalSort()
        {
            Logger.Log("NN_BRAIN", LogLevel.Debug, "Starting topological sort...");
            var sortedList = new List<NNNode>(_nodes.Count);
            var inDegree = _nodes.ToDictionary(kvp => kvp.Key, kvp => 0);

            // Calculate the in-degree for each node.
            foreach (var conn in _connections)
            {
                inDegree[conn.ToNodeId]++;
            }

            // Initialize the queue with all nodes that have an in-degree of 0.
            var queue = new Queue<NNNode>(_nodes.Values.Where(n => inDegree[n.Id] == 0));

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                sortedList.Add(node);

                if (_outgoingConnections.TryGetValue(node.Id, out var neighbors))
                {
                    foreach (var conn in neighbors)
                    {
                        inDegree[conn.ToNodeId]--;
                        if (inDegree[conn.ToNodeId] == 0)
                        {
                            queue.Enqueue(_nodes[conn.ToNodeId]);
                        }
                    }
                }
            }

            // If the sorted list doesn't contain all nodes, a cycle must exist.
            if (sortedList.Count != _nodes.Count)
            {
                string errorMsg = "Cycle detected in neural network graph! Cannot evaluate.";
                Logger.Log("NN_BRAIN", LogLevel.Error, errorMsg);
                throw new InvalidOperationException(errorMsg);
            }

            Logger.Log("NN_BRAIN", LogLevel.Debug, $"Topological sort complete. Sorted {_nodes.Count} nodes.");
            return sortedList;
        }
        
        /// <summary>
        /// Resets the network to an empty state.
        /// </summary>
        public void Clear()
        {
            _nodes.Clear();
            _connections.Clear();
            _inputNodes.Clear();
            _outputNodes.Clear();
            _outgoingConnections.Clear();
            _sortedNodes = null;
            Logger.Log("NN_BRAIN", LogLevel.Info, "Neural network cleared.");
        }

        /// <summary>
        /// Removes a node and all connections associated with it.
        /// </summary>
        /// <param name="nodeId">The ID of the node to remove.</param>
        public void RemoveNode(int nodeId)
        {
            if (_nodes.Remove(nodeId, out var nodeToRemove))
            {
                if (nodeToRemove.NodeType == NNNodeType.Input) _inputNodes.Remove(nodeToRemove);
                if (nodeToRemove.NodeType == NNNodeType.Output) _outputNodes.Remove(nodeToRemove);

                // Remove all connections where this node was the source or target.
                _connections.RemoveAll(c => c.FromNodeId == nodeId || c.ToNodeId == nodeId);
                
                // Update the lookup caches.
                _outgoingConnections.Remove(nodeId);
                foreach (var connList in _outgoingConnections.Values)
                {
                    connList.RemoveAll(c => c.ToNodeId == nodeId);
                }
                
                _sortedNodes = null; // Invalidate cache, as the graph structure has changed.
                Logger.Log("NN_BRAIN", LogLevel.Debug, $"Removed node {nodeId} and its connections.");
            }
        }

        /// <summary>
        /// Removes all connections between two specific nodes.
        /// </summary>
        /// <param name="fromNodeId">The ID of the source node.</param>
        /// <param name="toNodeId">The ID of the target node.</param>
        public void RemoveConnection(int fromNodeId, int toNodeId)
        {
            int removedCount = _connections.RemoveAll(c => c.FromNodeId == fromNodeId && c.ToNodeId == toNodeId);
            
            if (removedCount > 0)
            {
                if (_outgoingConnections.TryGetValue(fromNodeId, out var connList))
                {
                    connList.RemoveAll(c => c.ToNodeId == toNodeId);
                }
                _sortedNodes = null; // Invalidate cache, as the graph structure has changed.
                Logger.Log("NN_BRAIN", LogLevel.Debug, $"Removed {removedCount} connection(s) from {fromNodeId} to {toNodeId}.");
            }
        }
    }
}
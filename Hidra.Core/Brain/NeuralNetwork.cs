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
    /// This implementation is "fail-safe", meaning it actively prevents graph modifications
    /// that would create cycles, ensuring the network is always in a valid, evaluatable state.
    /// </summary>
    public class NeuralNetwork
    {
        [JsonProperty]
        private readonly Dictionary<int, NNNode> _nodes = new();
        [JsonProperty]
        private readonly List<NNConnection> _connections = new();

        [JsonIgnore]
        private readonly List<NNNode> _inputNodes = new();
        [JsonIgnore]
        private readonly List<NNNode> _outputNodes = new();
        [JsonIgnore]
        private readonly Dictionary<int, List<NNConnection>> _outgoingConnections = new();
        
        [JsonIgnore]
        private List<NNNode>? _sortedNodes;
        
        [JsonIgnore]
        public IReadOnlyDictionary<int, NNNode> Nodes => _nodes;
        [JsonIgnore]
        public IReadOnlyList<NNConnection> Connections => _connections;
        [JsonIgnore]
        public IReadOnlyList<NNNode> InputNodes => _inputNodes;
        [JsonIgnore]
        public IReadOnlyList<NNNode> OutputNodes => _outputNodes;

        private void Log(Action<string, LogLevel, string>? logAction, LogLevel level, string message)
        {
            logAction?.Invoke("NN_BRAIN", level, message);
        }

        public void InitializeFromLoad(Action<string, LogLevel, string>? logAction = null)
        {
            Log(logAction, LogLevel.Debug, "Initializing brain from loaded state...");
            _inputNodes.Clear();
            _outputNodes.Clear();
            _outgoingConnections.Clear();
            _sortedNodes = null;

            foreach (var node in _nodes.Values)
            {
                if (node.NodeType == NNNodeType.Input) _inputNodes.Add(node);
                if (node.NodeType == NNNodeType.Output) _outputNodes.Add(node);
            }

            // Rebuild outgoing connections map from the flat list
            foreach (var conn in _connections)
            {
                if (!_nodes.ContainsKey(conn.FromNodeId) || !_nodes.ContainsKey(conn.ToNodeId))
                {
                    Log(logAction, LogLevel.Warning, $"Ignoring orphaned connection from {conn.FromNodeId} to {conn.ToNodeId} during load.");
                    continue; // Skip orphaned connections
                }

                if (!_outgoingConnections.TryGetValue(conn.FromNodeId, out var connList))
                {
                    connList = new List<NNConnection>();
                    _outgoingConnections[conn.FromNodeId] = connList;
                }
                connList.Add(conn);
            }
            Log(logAction, LogLevel.Info, $"Brain re-initialized. {_nodes.Count} nodes ({_inputNodes.Count} inputs, {_outputNodes.Count} outputs), {_connections.Count} connections.");
        }
        
        public void AddNode(NNNode node, Action<string, LogLevel, string>? logAction = null)
        {
            if (_nodes.TryAdd(node.Id, node))
            {
                if (node.NodeType == NNNodeType.Input) _inputNodes.Add(node);
                if (node.NodeType == NNNodeType.Output) _outputNodes.Add(node);
                _sortedNodes = null;
                Log(logAction, LogLevel.Debug, $"Added node {node.Id} of type {node.NodeType}.");
            }
        }

        /// <summary>
        /// Attempts to add a connection to the network. The connection is only added if it does not create a cycle.
        /// </summary>
        /// <param name="connection">The connection to add.</param>
        /// <param name="logAction">Optional logger.</param>
        /// <returns>True if the connection was added successfully; false otherwise.</returns>
        public bool AddConnection(NNConnection connection, Action<string, LogLevel, string>? logAction = null)
        {
            if (!_nodes.ContainsKey(connection.FromNodeId) || !_nodes.ContainsKey(connection.ToNodeId))
            {
                Log(logAction, LogLevel.Warning, $"AddConnection failed: Node(s) not found for connection {connection.FromNodeId}->{connection.ToNodeId}.");
                return false;
            }

            // --- VALIDITY CHECK ---
            if (WouldCreateCycle(connection.FromNodeId, connection.ToNodeId))
            {
                Log(logAction, LogLevel.Warning, $"Rejected connection from {connection.FromNodeId} to {connection.ToNodeId} as it would create a cycle.");
                return false;
            }

            _connections.Add(connection);

            if (!_outgoingConnections.TryGetValue(connection.FromNodeId, out var connList))
            {
                connList = new List<NNConnection>();
                _outgoingConnections[connection.FromNodeId] = connList;
            }
            connList.Add(connection);
            _sortedNodes = null; // Invalidate the sort cache
            Log(logAction, LogLevel.Debug, $"Added connection {connection.FromNodeId}->{connection.ToNodeId} with weight {connection.Weight}.");
            return true;
        }

        /// <summary>
        /// Checks if adding a directed edge from a source node to a target node would introduce a cycle into the graph.
        /// </summary>
        /// <returns>True if a cycle would be created, false otherwise.</returns>
        private bool WouldCreateCycle(int fromNodeId, int toNodeId)
        {
            if (fromNodeId == toNodeId) return true; // Direct self-connection is a cycle of length 1.

            // We check by performing a graph traversal (BFS) starting from the *target* node.
            // If we can traverse the graph and arrive back at the *source* node, then adding an
            // edge from source->target would complete the cycle.
            var visited = new HashSet<int>();
            var queue = new Queue<int>();
            
            queue.Enqueue(toNodeId);
            visited.Add(toNodeId);

            while (queue.Count > 0)
            {
                var currentNodeId = queue.Dequeue();
                if (_outgoingConnections.TryGetValue(currentNodeId, out var neighbors))
                {
                    foreach (var conn in neighbors)
                    {
                        if (conn.ToNodeId == fromNodeId)
                        {
                            return true; // Found a path back to the source node.
                        }
                        if (!visited.Contains(conn.ToNodeId))
                        {
                            visited.Add(conn.ToNodeId);
                            queue.Enqueue(conn.ToNodeId);
                        }
                    }
                }
            }

            return false; // No path found from target back to source. Safe to connect.
        }

        public void Evaluate(IReadOnlyList<float> inputs, Action<string, LogLevel, string>? logAction = null)
        {
            if (InputNodes.Count != inputs.Count)
            {
                Log(logAction, LogLevel.Error, $"Evaluation failed: Mismatched input count. Expected {InputNodes.Count}, got {inputs.Count}.");
                return; // Fail gracefully
            }
            
            if (_sortedNodes == null)
            {
                _sortedNodes = TopologicalSort(logAction);
            }

            // Reset all node values to their bias before evaluation
            foreach (var node in _nodes.Values)
            {
                node.Value = node.Bias;
            }

            // Apply external inputs to the input nodes
            for (int i = 0; i < InputNodes.Count; i++)
            {
                _inputNodes[i].Value += inputs[i];
            }

            // Propagate values through the sorted network
            foreach (var node in _sortedNodes)
            {
                float activatedValue = node.Value; // For input nodes, value is already "activated"
                
                if (node.NodeType != NNNodeType.Input)
                {
                    activatedValue = node.ActivationFunction switch
                    {
                        ActivationFunctionType.Linear => node.Value,
                        ActivationFunctionType.Sigmoid => 1.0f / (1.0f + (float)Math.Exp(-node.Value)),
                        ActivationFunctionType.ReLU => Math.Max(0, node.Value),
                        ActivationFunctionType.Tanh or _ => (float)Math.Tanh(node.Value),
                    };
                }
                node.Value = activatedValue;

                if (_outgoingConnections.TryGetValue(node.Id, out var connections))
                {
                    foreach (var conn in connections)
                    {
                        _nodes[conn.ToNodeId].Value += activatedValue * conn.Weight;
                    }
                }
            }
        }

        /// <summary>
        /// Sorts the network nodes for feed-forward evaluation. In a fail-safe context,
        /// this should not fail, but includes safety checks.
        /// </summary>
        private List<NNNode> TopologicalSort(Action<string, LogLevel, string>? logAction = null)
        {
            var sortedList = new List<NNNode>(_nodes.Count);
            var inDegree = _nodes.ToDictionary(kvp => kvp.Key, kvp => 0);

            foreach (var conn in _connections)
            {
                // Safety check for malformed connections that might have slipped past other guards.
                if (inDegree.ContainsKey(conn.ToNodeId))
                {
                    inDegree[conn.ToNodeId]++;
                }
            }

            var queue = new Queue<NNNode>(_nodes.Values.Where(n => inDegree[n.Id] == 0));

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                sortedList.Add(node);

                if (_outgoingConnections.TryGetValue(node.Id, out var neighbors))
                {
                    foreach (var conn in neighbors)
                    {
                        if (inDegree.ContainsKey(conn.ToNodeId))
                        {
                            inDegree[conn.ToNodeId]--;
                            if (inDegree[conn.ToNodeId] == 0)
                            {
                                queue.Enqueue(_nodes[conn.ToNodeId]);
                            }
                        }
                    }
                }
            }

            if (sortedList.Count != _nodes.Count)
            {
                Log(logAction, LogLevel.Error, "Cycle detected in neural network graph despite preventative measures! Evaluation will be incomplete.");
                // Gracefully handle the error: append the remaining cyclic nodes to allow partial evaluation
                foreach(var node in _nodes.Values)
                {
                    if (!sortedList.Contains(node))
                    {
                        sortedList.Add(node);
                    }
                }
            }
            
            return sortedList;
        }
        
        public void Clear(Action<string, LogLevel, string>? logAction = null)
        {
            _nodes.Clear();
            _connections.Clear();
            _inputNodes.Clear();
            _outputNodes.Clear();
            _outgoingConnections.Clear();
            _sortedNodes = null;
            Log(logAction, LogLevel.Info, "Neural network cleared.");
        }

        public void RemoveNode(int nodeId, Action<string, LogLevel, string>? logAction = null)
        {
            if (_nodes.Remove(nodeId, out var nodeToRemove))
            {
                if (nodeToRemove.NodeType == NNNodeType.Input) _inputNodes.Remove(nodeToRemove);
                if (nodeToRemove.NodeType == NNNodeType.Output) _outputNodes.Remove(nodeToRemove);

                _connections.RemoveAll(c => c.FromNodeId == nodeId || c.ToNodeId == nodeId);
                
                _outgoingConnections.Remove(nodeId);
                foreach (var connList in _outgoingConnections.Values)
                {
                    connList.RemoveAll(c => c.ToNodeId == nodeId);
                }
                
                _sortedNodes = null;
                Log(logAction, LogLevel.Debug, $"Removed node {nodeId} and its connections.");
            }
        }

        public void RemoveConnection(int fromNodeId, int toNodeId, Action<string, LogLevel, string>? logAction = null)
        {
            int removedCount = _connections.RemoveAll(c => c.FromNodeId == fromNodeId && c.ToNodeId == toNodeId);
            
            if (removedCount > 0)
            {
                if (_outgoingConnections.TryGetValue(fromNodeId, out var connList))
                {
                    connList.RemoveAll(c => c.ToNodeId == toNodeId);
                }
                _sortedNodes = null;
                Log(logAction, LogLevel.Debug, $"Removed {removedCount} connection(s) from {fromNodeId} to {toNodeId}.");
            }
        }
    }
}
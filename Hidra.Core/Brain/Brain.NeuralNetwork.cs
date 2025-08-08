// Hidra.Core/Brain/NeuralNetworkBrain.cs
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hidra.Core.Brain
{
    /// <summary>
    /// An adapter class that makes the existing NeuralNetwork conform to the IBrain interface.
    /// This allows the powerful, but complex, neural network to be used as one of many
    /// possible brain types in the simulation.
    /// </summary>
    public class NeuralNetworkBrain : IBrain
    {
        // This is the actual neural network instance. It's marked with [JsonProperty]
        // so its complex internal state is correctly serialized along with this brain wrapper.
        [JsonProperty]
        private readonly NeuralNetwork _neuralNetwork = new();

        // Caches for the input/output maps to avoid re-calculating them on every access.
        // They are invalidated whenever the underlying network structure changes.
        [JsonIgnore]
        private List<BrainInput>? _inputMapCache;
        [JsonIgnore]
        private List<BrainOutput>? _outputMapCache;

        // A flag to indicate that the caches are dirty.
        [JsonIgnore]
        private bool _isDirty = true;
        
        /// <inheritdoc/>
        [JsonIgnore]
        public IReadOnlyList<BrainInput> InputMap
        {
            get
            {
                if (_isDirty || _inputMapCache == null)
                {
                    _inputMapCache = _neuralNetwork.InputNodes
                        .Select(n => new BrainInput { SourceType = n.InputSource, SourceIndex = n.SourceIndex })
                        .ToList();
                }
                return _inputMapCache!;
            }
        }

        /// <inheritdoc/>
        [JsonIgnore]
        public IReadOnlyList<BrainOutput> OutputMap
        {
            get
            {
                // The structure of the output map is cached, but the values must be updated after each evaluation.
                if (_isDirty || _outputMapCache == null)
                {
                    _outputMapCache = _neuralNetwork.OutputNodes
                        .Select(n => new BrainOutput { ActionType = n.ActionType, Value = n.Value })
                        .ToList();
                    _isDirty = false; // Caches are now fresh.
                }

                // Ensure the output values are current.
                for (int i = 0; i < _outputMapCache.Count; i++)
                {
                    _outputMapCache[i].Value = _neuralNetwork.OutputNodes[i].Value;
                }
                
                return _outputMapCache;
            }
        }

        /// <inheritdoc/>
        [JsonIgnore]
        public bool CanLearn => true;

        /// <inheritdoc/>
        public void Evaluate(float[] inputs)
        {
            _neuralNetwork.Evaluate(inputs);
        }

        /// <inheritdoc/>
        public void Mutate(float rate)
        {
            var random = new Random();

            // Mutate a connection weight
            if (_neuralNetwork.Connections.Any())
            {
                var connToMutate = _neuralNetwork.Connections[random.Next(_neuralNetwork.Connections.Count)];
                connToMutate.Weight += ((float)random.NextDouble() * 2f - 1f) * rate;
            }

            // Mutate a node bias
            if (_neuralNetwork.Nodes.Any())
            {
                var nodeToMutate = _neuralNetwork.Nodes.Values.ElementAt(random.Next(_neuralNetwork.Nodes.Count));
                nodeToMutate.Bias += ((float)random.NextDouble() * 2f - 1f) * rate;
            }
        }
        
        /// <inheritdoc/>
        public void Reset()
        {
            // A feed-forward network has no persistent state between evaluations, so there is nothing to reset.
        }
        
        /// <summary>
        /// Provides access to the internal NeuralNetwork object. This is essential for the HGL API
        /// functions that need to modify the brain's structure (e.g., API_AddBrainNode).
        /// </summary>
        /// <returns>The underlying NeuralNetwork instance.</returns>
        public NeuralNetwork GetInternalNetwork()
        {
            // Any call that gets the internal network is assumed to be for modification.
            _isDirty = true;
            return _neuralNetwork;
        }

        /// <summary>
        /// Initializes the brain after being loaded from a save state.
        /// </summary>
        public void InitializeFromLoad()
        {
            _neuralNetwork.InitializeFromLoad();
            _isDirty = true; // Mark caches for rebuilding.
        }
    }
}
// Hidra.Core/Brain/Brain.NeuralNetwork.cs
using Hidra.Core.Logging;
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
        [JsonProperty]
        public readonly NeuralNetwork _neuralNetwork = new();

        [JsonIgnore]
        private List<BrainInput>? _inputMapCache;
        [JsonIgnore]
        private List<BrainOutput>? _outputMapCache;
        [JsonIgnore]
        private bool _isDirty = true;

        [JsonIgnore]
        private IPrng? _prng;
        
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

        [JsonIgnore]
        public IReadOnlyList<BrainOutput> OutputMap
        {
            get
            {
                if (_isDirty || _outputMapCache == null)
                {
                    _outputMapCache = _neuralNetwork.OutputNodes
                        .Select(n => new BrainOutput { ActionType = n.ActionType, Value = n.Value })
                        .ToList();
                    _isDirty = false; // Set dirty flag only when structure changes
                }
                return _outputMapCache;
            }
        }

        [JsonIgnore]
        public bool CanLearn => true;

        public void Evaluate(float[] inputs, Action<string, LogLevel, string>? logAction = null)
        {
            _neuralNetwork.Evaluate(inputs, logAction);
            
            // After evaluation, update the values in the output map cache
            if (_outputMapCache != null)
            {
                var outputNodes = _neuralNetwork.OutputNodes;
                for (int i = 0; i < _outputMapCache.Count && i < outputNodes.Count; i++)
                {
                    _outputMapCache[i].Value = outputNodes[i].Value;
                }
            }
        }

        public void Mutate(float rate)
        {
            if (_prng == null) return;
            _isDirty = true; // A mutation might eventually change the structure, so be safe.

            // Mutate a connection weight
            if (_neuralNetwork.Connections.Any())
            {
                int index = _prng.NextInt(0, _neuralNetwork.Connections.Count);
                _neuralNetwork.Connections[index].Weight += (_prng.NextFloat() * 2f - 1f) * rate;
            }

            // Mutate a node bias
            if (_neuralNetwork.Nodes.Any())
            {
                // Can't guarantee dictionary order, so we convert to a list for deterministic selection.
                var nodeList = _neuralNetwork.Nodes.Values.ToList(); 
                int index = _prng.NextInt(0, nodeList.Count);
                nodeList[index].Bias += (_prng.NextFloat() * 2f - 1f) * rate;
            }
        }
        
        public void Reset()
        {
            // The underlying network has no state to reset between evaluations.
        }
        
        public void SetPrng(IPrng prng)
        {
            _prng = prng;
        }
        
        /// <summary>
        /// Provides direct access to the underlying network for structural modifications.
        /// Marks the cache as dirty.
        /// </summary>
        public NeuralNetwork GetInternalNetwork()
        {
            _isDirty = true;
            return _neuralNetwork;
        }

        public void InitializeFromLoad()
        {
            _neuralNetwork.InitializeFromLoad(null); // Logger isn't available here, which is fine.
            _isDirty = true;
        }
    }
}
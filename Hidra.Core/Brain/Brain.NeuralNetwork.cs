// Hidra.Core/Brain/NeuralNetworkBrain.cs
namespace Hidra.Core.Brain;

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

/// <summary>
/// An adapter class that makes the existing <see cref="NeuralNetwork"/> conform to the <see cref="IBrain"/> interface.
/// </summary>
/// <remarks>
/// This allows the powerful, but complex, neural network to be used as one of many
/// possible brain types in the simulation. It manages caching of the input/output maps
/// for performance.
/// </remarks>
public class NeuralNetworkBrain : IBrain
{
    [JsonProperty]
    private readonly NeuralNetwork _neuralNetwork = new();

    [JsonIgnore]
    private List<BrainInput>? _inputMapCache;
    [JsonIgnore]
    private List<BrainOutput>? _outputMapCache;

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
            if (_isDirty || _outputMapCache == null)
            {
                _outputMapCache = _neuralNetwork.OutputNodes
                    .Select(n => new BrainOutput { ActionType = n.ActionType, Value = n.Value })
                    .ToList();
                _isDirty = false;
            }

            for (var i = 0; i < _outputMapCache.Count; i++)
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
    public void Evaluate(float[] inputs) => _neuralNetwork.Evaluate(inputs);

    /// <inheritdoc/>
    public void Mutate(float rate)
    {
        var random = new Random();

        if (_neuralNetwork.Connections.Any())
        {
            var connToMutate = _neuralNetwork.Connections[random.Next(_neuralNetwork.Connections.Count)];
            connToMutate.Weight += ((float)random.NextDouble() * 2f - 1f) * rate;
        }

        if (_neuralNetwork.Nodes.Any())
        {
            var nodeToMutate = _neuralNetwork.Nodes.Values.ElementAt(random.Next(_neuralNetwork.Nodes.Count));
            nodeToMutate.Bias += ((float)random.NextDouble() * 2f - 1f) * rate;
        }
    }
    
    /// <inheritdoc/>
    /// <remarks>
    /// A feed-forward network has no persistent state between evaluations, so this method is a no-op.
    /// </remarks>
    public void Reset()
    {
    }
    
    /// <summary>
    /// Provides access to the internal <see cref="NeuralNetwork"/> object.
    /// </summary>
    /// <returns>The underlying <see cref="NeuralNetwork"/> instance.</returns>
    /// <remarks>
    /// This is essential for the HGL API functions that need to modify the brain's structure.
    /// Any call that gets the internal network is assumed to be for modification, so the
    /// input/output map caches are marked as dirty.
    /// </remarks>
    public NeuralNetwork GetInternalNetwork()
    {
        _isDirty = true;
        return _neuralNetwork;
    }

    /// <summary>
    /// Initializes the brain after being loaded from a save state.
    /// </summary>
    /// <remarks>
    /// This calls the underlying network's initialization and marks this adapter's
    /// caches as dirty to force them to be rebuilt on next access.
    /// </remarks>
    public void InitializeFromLoad()
    {
        _neuralNetwork.InitializeFromLoad();
        _isDirty = true;
    }
}
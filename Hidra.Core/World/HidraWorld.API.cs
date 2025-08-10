// Hidra.Core/World/HidraWorld.API.cs
namespace Hidra.Core;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Hidra.Core.Brain;

/// <summary>
/// This partial class contains the public Application Programming Interface (API) for the HidraWorld.
/// It exposes thread-safe methods for querying and modifying the world state from external code,
/// such as a user interface or a testing framework.
/// </summary>
public partial class HidraWorld
{
    /// <summary>
    /// Retrieves a neuron by its unique identifier.
    /// </summary>
    /// <param name="id">The ID of the neuron to find.</param>
    /// <returns>The found <see cref="Neuron"/> instance, or null if the ID does not exist.</returns>
    /// <remarks>
    /// This provides direct, read-only access. SortedDictionary lookups are thread-safe for reads.
    /// </remarks>
    public Neuron? GetNeuronById(ulong id) => _neurons.GetValueOrDefault(id);

    /// <summary>
    /// Retrieves a global input node by its unique identifier.
    /// </summary>
    /// <param name="id">The ID of the input node to find.</param>
    /// <returns>The found <see cref="InputNode"/> instance, or null if the ID does not exist.</returns>
    public InputNode? GetInputNodeById(ulong id) => _inputNodes.GetValueOrDefault(id);

    /// <summary>
    /// Retrieves a global output node by its unique identifier.
    /// </summary>
    /// <param name="id">The ID of the output node to find.</param>
    /// <returns>The found <see cref="OutputNode"/> instance, or null if the ID does not exist.</returns>
    public OutputNode? GetOutputNodeById(ulong id) => _outputNodes.GetValueOrDefault(id);

    /// <summary>
    /// Finds all neurons within a given radius of a central point using the spatial hash.
    /// </summary>
    /// <param name="center">The neuron at the center of the search area.</param>
    /// <param name="radius">The search radius.</param>
    /// <returns>An enumeration of all neurons found within the specified radius.</returns>
    /// <remarks>
    /// This read operation is only guaranteed to be thread-safe if no write operations
    /// are being performed on the SpatialHash concurrently. The `HidraWorld.Step()` method
    /// rebuilds the hash, so this method should not be called from an external thread
    /// at the exact same time as the simulation step.
    /// </remarks>
    public IEnumerable<Neuron> GetNeighbors(Neuron center, float radius) => _spatialHash.FindNeighbors(center, radius);

    /// <summary>
    /// Gets a read-only view of the global hormones array.
    /// </summary>
    /// <returns>The array of global hormone values.</returns>
    public IReadOnlyList<float> GetGlobalHormones() => GlobalHormones;
    
    /// <summary>
    /// Gets the current values of the specified output nodes. This is a thread-safe read operation.
    /// </summary>
    /// <param name="ids">A list of unique identifiers for the output nodes to query.</param>
    /// <returns>A dictionary mapping each found ID to its current value.</returns>
    public Dictionary<ulong, float> GetOutputValues(List<ulong> ids)
    {
        var results = new Dictionary<ulong, float>();
        foreach (var id in ids)
        {
            if (_outputNodes.TryGetValue(id, out var node))
            {
                results[id] = node.Value;
            }
        }
        return results;
    }

    /// <summary>
    /// Retrieves a read-only list of events that were processed on a specific tick.
    /// </summary>
    /// <param name="tick">The simulation tick to query for events.</param>
    /// <returns>A read-only list of events for the specified tick, or an empty list if none exist.</returns>
    public IReadOnlyList<Event> GetEventsForTick(ulong tick)
    {
        lock (_eventHistoryLock)
        {
            if (_eventHistory.TryGetValue(tick, out var events))
            {
                return events.ToList();
            }
            return Array.Empty<Event>();
        }
    }

    /// <summary>
    /// Creates and adds a new neuron to the world. This operation is thread-safe.
    /// </summary>
    /// <param name="position">The initial position of the neuron in 3D space.</param>
    /// <returns>The newly created and fully initialized neuron.</returns>
    public Neuron AddNeuron(Vector3 position)
    {
        var newNeuron = new Neuron
        {
            Id = (ulong)Interlocked.Increment(ref _nextNeuronId),
            IsActive = true,
            Position = position,
            LocalVariables = new float[256],
            Brain = CreateDefaultBrain()
        };
        
        newNeuron.LocalVariables[(int)LVarIndex.FiringThreshold] = Config.DefaultFiringThreshold;
        newNeuron.LocalVariables[(int)LVarIndex.DecayRate] = Config.DefaultDecayRate;
        newNeuron.LocalVariables[(int)LVarIndex.SomaPotential] = Config.InitialPotential;
        newNeuron.LocalVariables[(int)LVarIndex.Health] = Config.InitialNeuronHealth;
        newNeuron.LocalVariables[(int)LVarIndex.RefractoryPeriod] = Config.DefaultRefractoryPeriod;
        newNeuron.LocalVariables[(int)LVarIndex.ThresholdAdaptationFactor] = Config.DefaultThresholdAdaptationFactor;
        newNeuron.LocalVariables[(int)LVarIndex.ThresholdRecoveryRate] = Config.DefaultThresholdRecoveryRate;

        lock (_worldApiLock)
        {
            _neurons.Add(newNeuron.Id, newNeuron);
        }

        // Schedule the "Gestation" gene to run on the next tick. This gene is responsible
        // for the neuron's initial integration into the network, such as forming its first synapses.
        var eventId = (ulong)Interlocked.Increment(ref _nextEventId);
        _eventQueue.Push(new Event { Id = eventId, Type = EventType.ExecuteGene, TargetId = newNeuron.Id, ExecutionTick = CurrentTick + 1, Payload = SYS_GENE_GESTATION });

        return newNeuron;
    }

    /// <summary>
    /// Creates a new synapse between any two valid entities (Neuron, InputNode, OutputNode).
    /// This operation is thread-safe.
    /// </summary>
    /// <param name="sourceId">The unique ID of the source entity (Neuron or InputNode).</param>
    /// <param name="targetId">The unique ID of the target entity (Neuron or OutputNode).</param>
    /// <param name="signalType">The behavioral type of the synapse's signal transmission.</param>
    /// <param name="weight">The connection strength, modulating the signal's magnitude.</param>
    /// <param name="parameter">A multi-purpose parameter (e.g., threshold for inputs, smoothing for outputs).</param>
    /// <returns>The newly created synapse, or null if the source or target IDs are invalid.</returns>
    public Synapse? AddSynapse(ulong sourceId, ulong targetId, SignalType signalType, float weight, float parameter)
    {
        bool sourceExists = _neurons.ContainsKey(sourceId) || _inputNodes.ContainsKey(sourceId);
        bool targetExists = _neurons.ContainsKey(targetId) || _outputNodes.ContainsKey(targetId);

        if (!sourceExists || !targetExists)
        {
            return null;
        }

        var synapse = new Synapse
        {
            Id = (ulong)Interlocked.Increment(ref _nextSynapseId),
            SourceId = sourceId,
            TargetId = targetId,
            SignalType = signalType,
            Weight = weight,
            Parameter = parameter,
            IsActive = true
        };
        
        return AddSynapseInternal(synapse);
    }

    /// <summary>
    /// Adds a new global input node to the world. This operation is thread-safe.
    /// </summary>
    /// <param name="id">The unique identifier for the new input node.</param>
    /// <param name="initialValue">The starting value for the input node.</param>
    public void AddInputNode(ulong id, float initialValue = 0f)
    {
        lock (_worldApiLock)
        {
            _inputNodes.TryAdd(id, new InputNode { Id = id, Value = initialValue });
        }
    }

    /// <summary>
    /// Adds a new global output node to the world. This operation is thread-safe.
    /// </summary>
    /// <param name="id">The unique identifier for the new output node.</param>
    public void AddOutputNode(ulong id)
    {
        lock (_worldApiLock)
        {
            _outputNodes.TryAdd(id, new OutputNode { Id = id, Value = 0f });
        }
    }

    /// <summary>
    /// Sets the values of multiple input nodes at once. This operation is thread-safe for individual writes.
    /// </summary>
    /// <param name="values">A dictionary mapping input node IDs to their new values.</param>
    /// <remarks>
    /// While individual float assignments are atomic, setting multiple values this way is not a single atomic
    /// transaction. The simulation might run a tick after some, but not all, values have been updated.
    /// </remarks>
    public void SetInputValues(Dictionary<ulong, float> values)
    {
        foreach (var (key, value) in values)
        {
            if (_inputNodes.TryGetValue(key, out var node))
            {
                node.Value = value;
            }
        }
    }

    /// <summary>
    /// Adds a neuron to the deactivation queue to be processed at the end of the current tick.
    /// </summary>
    /// <param name="neuron">The neuron to mark for deactivation.</param>
    /// <remarks>This is the thread-safe entry point for HGL scripts.</remarks>
    public void MarkNeuronForDeactivation(Neuron neuron)
    {
        // Rationale: Adding to a simple List<> is not thread-safe, so we must lock.
        // A ConcurrentBag<> would also work but a lock is simpler here.
        lock (_worldApiLock)
        {
            _neuronsToDeactivate.Add(neuron);
        }
    }

    /// <summary>
    /// Handles the biological process of mitosis: copying a neuron and scheduling the Mitosis gene
    /// for both parent and child.
    /// </summary>
    /// <param name="parent">The neuron to copy.</param>
    /// <param name="offset">The position offset for the new child neuron relative to the parent.</param>
    /// <returns>The newly created child neuron.</returns>
    /// <remarks>
    /// This method intentionally bypasses the standard 'AddNeuron' logic to avoid queuing the Gestation gene.
    /// </remarks>
    public Neuron PerformMitosis(Neuron parent, Vector3 offset)
    {
        var childNeuron = new Neuron
        {
            Id = (ulong)Interlocked.Increment(ref _nextNeuronId),
            IsActive = true,
            Position = parent.Position + offset,
            // The child inherits a complete copy of the parent's local variables.
            LocalVariables = (float[])parent.LocalVariables.Clone(),
            // This is a shallow copy of the brain reference. This is usually desired.
            // If brains were value types or needed unique state, a deep copy would be required.
            Brain = parent.Brain 
        };
        
        // Reset age for the new neuron.
        childNeuron.LocalVariables[(int)LVarIndex.Age] = 0;

        lock (_worldApiLock)
        {
            _neurons.Add(childNeuron.Id, childNeuron);
        }

        // Queue Mitosis Genes for both the parent and new child neuron.
        var parentEventId = (ulong)Interlocked.Increment(ref _nextEventId);
        _eventQueue.Push(new Event { Id = parentEventId, Type = EventType.ExecuteGene, TargetId = parent.Id, ExecutionTick = CurrentTick + 1, Payload = SYS_GENE_MITOSIS });
        
        var childEventId = (ulong)Interlocked.Increment(ref _nextEventId);
        _eventQueue.Push(new Event { Id = childEventId, Type = EventType.ExecuteGene, TargetId = childNeuron.Id, ExecutionTick = CurrentTick + 1, Payload = SYS_GENE_MITOSIS });

        return childNeuron;
    }

    private static readonly Comparer<Synapse> SynapseIdComparer = Comparer<Synapse>.Create((a, b) => a.Id.CompareTo(b.Id));

    private static void InsertOwnedSynapseSorted(List<Synapse> list, Synapse s)
    {
        // Assumes 'list' is already sorted by Id. BinarySearch finds the insertion point.
        int idx = list.BinarySearch(s, SynapseIdComparer);
        if (idx < 0) idx = ~idx;
        list.Insert(idx, s);
    }

    /// <remarks>
    /// Rationale: This internal helper centralizes synapse creation logic with two key optimizations:
    /// 1. It seeds `PreviousSourceValue` to prevent false Rising/Falling edge detections on the first tick.
    /// 2. It avoids an O(N log N) sort of the global `_synapses` list in the common case where new synapses have ascending IDs.
    /// 3. It uses a more efficient O(log N) binary search to insert into the denormalized `OwnedSynapses` lists, keeping them sorted.
    /// </remarks>
    private Synapse AddSynapseInternal(Synapse synapse)
    {
        lock (_worldApiLock)
        {
            // Seed PreviousSourceValue to avoid false Rising/Falling edges on first evaluation.
            float initial = 0f;
            if (_inputNodes.TryGetValue(synapse.SourceId, out var inNode))
            {
                initial = inNode.Value;
            }
            else if (_neurons.TryGetValue(synapse.SourceId, out var srcNeuron))
            {
                initial = srcNeuron.LocalVariables[(int)LVarIndex.SomaPotential]
                        + srcNeuron.LocalVariables[(int)LVarIndex.DendriticPotential];
            }
            synapse.PreviousSourceValue = initial;

            // Register the synapse globally, avoiding a full sort on every add.
            bool mustResort = _synapses.Count > 0 && synapse.Id < _synapses[^1].Id;
            _synapses.Add(synapse);
            if (mustResort)
            {
                _synapses.Sort((a, b) => a.Id.CompareTo(b.Id));
            }

            // Keep denormalized per-neuron indexes sorted by Id for fast lookups.
            if (_neurons.TryGetValue(synapse.SourceId, out var source))
            {
                InsertOwnedSynapseSorted(source.OwnedSynapses, synapse);
            }
            if (_neurons.TryGetValue(synapse.TargetId, out var target))
            {
                if (synapse.SourceId != synapse.TargetId) // Avoid double-add for autapses.
                {
                    InsertOwnedSynapseSorted(target.OwnedSynapses, synapse);
                }
            }
        }
        return synapse;
    }

    /// <summary>
    /// Creates a simple, default brain for a new neuron.
    /// </summary>
    /// <returns>A new <see cref="IBrain"/> instance configured with pass-through logic.</returns>
    /// <remarks>
    /// This provides a minimal, functional brain for every new neuron. The default brain
    /// simply reads its neuron's `ActivationPotential` and sets it as the brain's primary output value.
    /// This "pass-through" behavior is the simplest possible action and serves as a baseline.
    /// </remarks>
    private IBrain CreateDefaultBrain()
    {
        var brain = new NeuralNetworkBrain();
        var internalNetwork = brain.GetInternalNetwork();

        var input = new NNNode(0, NNNodeType.Input) { InputSource = InputSourceType.ActivationPotential };
        var output = new NNNode(1, NNNodeType.Output) { ActionType = OutputActionType.SetOutputValue };
        
        internalNetwork.AddNode(input);
        internalNetwork.AddNode(output);
        internalNetwork.AddConnection(new NNConnection(input.Id, output.Id, 1.0f));

        return brain;
    }
}
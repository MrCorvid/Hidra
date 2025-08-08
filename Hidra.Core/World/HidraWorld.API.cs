// Hidra.Core/World/HidraWorld.API.cs
using Hidra.Core.Brain;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Hidra.Core
{
    /// <summary>
    /// This partial class contains the public Application Programming Interface (API) for the HidraWorld.
    /// It exposes thread-safe methods for querying and modifying the world state from external code,
    /// such as a user interface or a testing framework.
    /// </summary>
    public partial class HidraWorld
    {
        #region Public Getters (Thread-Safe Reads)

        /// <summary>
        /// Retrieves a neuron by its unique identifier.
        /// </summary>
        /// <param name="id">The ID of the neuron to find.</param>
        /// <returns>The found <see cref="Neuron"/> instance, or null if the ID does not exist.</returns>
        public Neuron? GetNeuronById(ulong id)
        {
            // Intent: Provide direct, read-only access. SortedDictionary lookups are thread-safe for reads.
            return _neurons.GetValueOrDefault(id);
        }

        /// <summary>
        /// Retrieves a global input node by its unique identifier.
        /// </summary>
        /// <param name="id">The ID of the input node to find.</param>
        /// <returns>The found <see cref="InputNode"/> instance, or null if the ID does not exist.</returns>
        public InputNode? GetInputNodeById(ulong id)
        {
            return _inputNodes.GetValueOrDefault(id);
        }

        /// <summary>
        /// Retrieves a global output node by its unique identifier.
        /// </summary>
        /// <param name="id">The ID of the output node to find.</param>
        /// <returns>The found <see cref="OutputNode"/> instance, or null if the ID does not exist.</returns>
        public OutputNode? GetOutputNodeById(ulong id)
        {
            return _outputNodes.GetValueOrDefault(id);
        }

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
        public IEnumerable<Neuron> GetNeighbors(Neuron center, float radius)
        {
            return _spatialHash.FindNeighbors(center, radius);
        }
        
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

        #endregion

        #region Public State Modifiers (Thread-Safe Writes)

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
            
            // Intent: Initialize the neuron's physical properties using the world's default configuration.
            // This ensures that all newly created neurons start with consistent, predictable behavior.
            newNeuron.LocalVariables[(int)LVarIndex.FiringThreshold] = Config.DefaultFiringThreshold;
            newNeuron.LocalVariables[(int)LVarIndex.DecayRate] = Config.DefaultDecayRate;
            newNeuron.LocalVariables[(int)LVarIndex.SomaPotential] = Config.InitialPotential;
            newNeuron.LocalVariables[(int)LVarIndex.Health] = Config.InitialNeuronHealth;
            newNeuron.LocalVariables[(int)LVarIndex.RefractoryPeriod] = Config.DefaultRefractoryPeriod;
            newNeuron.LocalVariables[(int)LVarIndex.ThresholdAdaptationFactor] = Config.DefaultThresholdAdaptationFactor;
            newNeuron.LocalVariables[(int)LVarIndex.ThresholdRecoveryRate] = Config.DefaultThresholdRecoveryRate;

            // Intent: Lock access to the shared collections to prevent corruption from concurrent API calls.
            lock (_worldApiLock)
            {
                _neurons.Add(newNeuron.Id, newNeuron);
            }

            // Intent: Schedule the "Gestation" gene to run on the next tick. This gene is responsible
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
            // Intent: Ensure the synapse connects two existing entities to maintain world integrity.
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
        
        #endregion

        #region Private Helpers

        /// <summary>
        /// Centralizes the logic for adding a pre-constructed Synapse object to the world's collections.
        /// </summary>
        /// <param name="synapse">The synapse to add.</param>
        /// <returns>The added synapse.</returns>
        private Synapse AddSynapseInternal(Synapse synapse)
        {
            lock (_worldApiLock)
            {
                _synapses.Add(synapse);
                
                // Intent: Keep the main synapse list sorted by ID. This ensures deterministic behavior
                // if any logic relies on traversal order and can speed up certain search operations.
                _synapses.Sort((a, b) => a.Id.CompareTo(b.Id));
                
                // Intent: Add a reference to this synapse in the connected neurons' `OwnedSynapses` list.
                // This denormalization provides a fast, direct lookup of all connections for a given neuron,
                // avoiding a full scan of the global synapse list.
                if (_neurons.TryGetValue(synapse.SourceId, out var source))
                {
                    source.OwnedSynapses.Add(synapse);
                }
                if (_neurons.TryGetValue(synapse.TargetId, out var target))
                {
                    // Avoid adding the synapse twice if the source and target are the same neuron (autapse).
                    if (synapse.SourceId != synapse.TargetId)
                    {
                        target.OwnedSynapses.Add(synapse);
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
        /// Intent: To provide a minimal, functional brain for every new neuron. This default brain
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

        #endregion
    }
}
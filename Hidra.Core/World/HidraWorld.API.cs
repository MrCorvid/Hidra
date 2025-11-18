// Hidra.Core/World/HidraWorld.API.cs
using Hidra.Core.Brain;
using Hidra.Core.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
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
        private const int LVAR_COUNT = 256;

        #region Public Getters (Thread-Safe Reads)

        /// <summary>
        /// Gets the total number of genes loaded into the simulation.
        /// </summary>
        /// <returns>The total count of compiled genes.</returns>
        public int GetGeneCount()
        {
            // No lock needed as _compiledGenes is immutable after world creation.
            return _compiledGenes.Count;
        }

        /// <summary>
        /// Retrieves a neuron by its unique identifier.
        /// </summary>
        /// <param name="id">The ID of the neuron to find.</param>
        /// <returns>The found <see cref="Neuron"/> instance, or null if the ID does not exist.</returns>
        public Neuron? GetNeuronById(ulong id)
        {
            lock (_worldApiLock)
            {
                return _neurons.GetValueOrDefault(id);
            }
        }
        
        /// <summary>
        /// Retrieves a synapse by its unique identifier. This is an O(N) operation.
        /// </summary>
        /// <param name="id">The ID of the synapse to find.</param>
        /// <returns>The found <see cref="Synapse"/> instance, or null if the ID does not exist.</returns>
        public Synapse? GetSynapseById(ulong id)
        {
            lock (_worldApiLock)
            {
                return _synapses.FirstOrDefault(s => s.Id == id);
            }
        }

        // Add this new public method within the "Public Getters" region.
        /// <summary>
        /// Retrieves a list of all synapses that target the specified neuron.
        /// </summary>
        /// <param name="targetNeuron">The neuron whose incoming synapses are to be found.</param>
        /// <returns>A new list containing the incoming synapses, sorted by ID.</returns>
        public List<Synapse> GetIncomingSynapses(Neuron targetNeuron)
        {
            lock (_worldApiLock)
            {
                // Ensure the cache is up-to-date before using it.
                EnsureCachesUpToDate(); 
                if (_incomingSynapseCache != null && _incomingSynapseCache.TryGetValue(targetNeuron.Id, out var synapses))
                {
                    // Return a sorted copy to ensure determinism and prevent external modification.
                    return synapses.OrderBy(s => s.Id).ToList();
                }
                return new List<Synapse>();
            }
        }

        /// <summary>
        /// Retrieves a global input node by its unique identifier.
        /// </summary>
        /// <param name="id">The ID of the input node to find.</param>
        /// <returns>The found <see cref="InputNode"/> instance, or null if the ID does not exist.</returns>
        public InputNode? GetInputNodeById(ulong id)
        {
            lock (_worldApiLock)
            {
                return _inputNodes.GetValueOrDefault(id);
            }
        }

        /// <summary>
        /// Retrieves a global output node by its unique identifier.
        /// </summary>
        /// <param name="id">The ID of the output node to find.</param>
        /// <returns>The found <see cref="OutputNode"/> instance, or null if the ID does not exist.</returns>
        public OutputNode? GetOutputNodeById(ulong id)
        {
            lock (_worldApiLock)
            {
                return _outputNodes.GetValueOrDefault(id);
            }
        }

        /// <summary>
        /// Finds all neurons within a given radius of a central neuron using the spatial hash.
        /// </summary>
        /// <param name="center">The neuron at the center of the search area.</param>
        /// <param name="radius">The search radius.</param>
        /// <returns>A list of all neurons found within the specified radius.</returns>
        public IEnumerable<Neuron> GetNeighbors(Neuron center, float radius)
        {
            lock (_worldApiLock)
            {
                return _spatialHash.FindNeighbors(center, radius).ToList(); 
            }
        }

        /// <summary>
        /// Gets a thread-safe, read-only copy of the global hormones array.
        /// </summary>
        /// <returns>A new array containing the current global hormone values.</returns>
        public IReadOnlyList<float> GetGlobalHormones()
        {
            lock (_worldApiLock)
            {
                return (float[])GlobalHormones.Clone();
            }
        }
        
        /// <summary>
        /// Gets the current values of the specified output nodes.
        /// </summary>
        /// <param name="ids">A list of unique identifiers for the output nodes to query.</param>
        /// <returns>A dictionary mapping each found ID to its current value.</returns>
        public Dictionary<ulong, float> GetOutputValues(List<ulong> ids)
        {
            var results = new Dictionary<ulong, float>();
            lock (_worldApiLock)
            {
                foreach (var id in ids)
                {
                    if (_outputNodes.TryGetValue(id, out var node))
                    {
                        results[id] = node.Value;
                    }
                }
            }
            return results;
        }

        /// <summary>
        /// Retrieves a read-only list of all events associated with a specific tick,
        /// including both processed historical events and any pending events in the queue.
        /// </summary>
        /// <param name="tick">The simulation tick to query for events.</param>
        /// <returns>A read-only list of events for the specified tick.</returns>
        public IReadOnlyList<Event> GetEventsForTick(ulong tick)
        {
            var results = new List<Event>();

            lock (_eventHistoryLock)
            {
                if (_eventHistory.TryGetValue(tick, out var historicalEvents))
                {
                    results.AddRange(historicalEvents);
                }
            }
            
            lock (_worldApiLock)
            {
                results.AddRange(_eventQueue.PeekEventsForTick(tick));
            }

            return results;
        }

        /// <summary>
        /// Captures and returns a complete, thread-safe snapshot of all entities in the world.
        /// This method is designed to provide external layers (like an API) with all the data
        /// needed to build a detailed representation of the world's current state.
        /// </summary>
        /// <returns>A <see cref="FullWorldState"/> record containing copies of all world entities.</returns>
        public FullWorldState GetFullWorldState()
        {
            lock (_worldApiLock)
            {
                // NOTE: We use .ToList() to create copies of the collections,
                // preventing the caller from getting a direct reference to the internal lists.
                return new FullWorldState(
                    ExperimentId: this.ExperimentId,
                    CurrentTick: this.CurrentTick,
                    InputNodes: _inputNodes.Values.ToList(),
                    OutputNodes: _outputNodes.Values.ToList(),
                    Neurons: _neurons.Values.ToList(),
                    Synapses: _synapses.ToList()
                );
            }
        }

        #endregion

        #region Public Metrics API (Thread-Safe Reads)

        /// <summary>
        /// Captures and returns a snapshot of the current world state.
        /// </summary>
        /// <param name="includeSynapses">If true, detailed synapse data will be included, which can be resource-intensive.</param>
        /// <returns>A <see cref="WorldSnapshot"/> of the current state.</returns>
        public WorldSnapshot GetWorldSnapshot(bool includeSynapses = false)
        {
            lock (_worldApiLock)
            {
                return BuildWorldSnapshot(includeSynapses);
            }
        }

        /// <summary>
        /// Computes and returns summary metrics for the current world state.
        /// </summary>
        /// <returns>A <see cref="TickMetrics"/> object with aggregated data.</returns>
        public TickMetrics GetTickMetrics()
        {
            lock (_worldApiLock)
            {
                return ComputeTickMetrics();
            }
        }

        /// <summary>
        /// Retrieves a list of recent world snapshots from the metrics ring buffer.
        /// </summary>
        /// <param name="maxCount">The maximum number of snapshots to retrieve.</param>
        /// <returns>A list of the most recent snapshots, ordered from most recent to oldest.</returns>
        public IReadOnlyList<WorldSnapshot> GetRecentSnapshots(int maxCount = 256)
        {
            lock (_worldApiLock)
            {
                if (!Config.MetricsEnabled || _metricsRing == null || Config.MetricsRingCapacity <= 0 || (_metricsHead == 0 && !_metricsWrapped)) 
                    return Array.Empty<WorldSnapshot>();

                var results = new List<WorldSnapshot>(Math.Min(maxCount, Config.MetricsRingCapacity));
                int total = _metricsWrapped ? Config.MetricsRingCapacity : _metricsHead;
                int toTake = Math.Min(maxCount, total);

                for (int i = 0; i < toTake; i++)
                {
                    int idx = (_metricsHead - 1 - i + Config.MetricsRingCapacity) % Config.MetricsRingCapacity;
                    if (_metricsRing[idx] != null)
                    {
                        results.Add(_metricsRing[idx]);
                    }
                }
                return results;
            }
        }

        /// <summary>
        /// Exports recent world snapshots to a JSON formatted string.
        /// </summary>
        /// <param name="maxCount">The maximum number of snapshots to include.</param>
        /// <returns>A JSON string representing an array of snapshots.</returns>
        public string ExportRecentSnapshotsToJson(int maxCount = 256)
        {
            var snaps = GetRecentSnapshots(maxCount);
            return JsonConvert.SerializeObject(snaps, Formatting.Indented);
        }

        /// <summary>
        /// Exports the summary metrics from recent snapshots to a CSV formatted string.
        /// </summary>
        /// <param name="maxCount">The maximum number of snapshots to include.</param>
        /// <returns>A CSV string of the time-series metrics data.</returns>
        public string ExportRecentTickMetricsCsv(int maxCount = 4096)
        {
            var snaps = GetRecentSnapshots(maxCount).OrderBy(s => s.Tick).ToList();
            if (!snaps.Any()) return "";

            var sb = new StringBuilder();
            sb.AppendLine("tick,neuronCount,activeNeuronCount,synapseCount,activeSynapseCount,meanFiringRate,meanHealth,meanSoma,meanDend");

            foreach (var s in snaps)
            {
                var m = s.Summary;
                sb.AppendLine($"{m.Tick},{m.NeuronCount},{m.ActiveNeuronCount},{m.SynapseCount},{m.ActiveSynapseCount},{m.MeanFiringRate},{m.MeanHealth},{m.MeanSomaPotential},{m.MeanDendriticPotential}");
            }
            return sb.ToString();
        }

        #endregion

        #region Public State Modifiers (Thread-Safe Writes)

        /// <summary>
        /// Creates and adds a new neuron to the world.
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
                LocalVariables = new float[LVAR_COUNT],
                Brain = new DummyBrain()
            };
            
            newNeuron.LocalVariables[(int)LVarIndex.FiringThreshold] = Config.DefaultFiringThreshold;
            newNeuron.LocalVariables[(int)LVarIndex.DecayRate] = Config.DefaultDecayRate;
            newNeuron.LocalVariables[(int)LVarIndex.SomaPotential] = Config.InitialPotential;
            newNeuron.LocalVariables[(int)LVarIndex.Health] = Config.InitialNeuronHealth;
            newNeuron.LocalVariables[(int)LVarIndex.RefractoryPeriod] = Config.DefaultRefractoryPeriod;
            newNeuron.LocalVariables[(int)LVarIndex.ThresholdAdaptationFactor] = Config.DefaultThresholdAdaptationFactor;
            newNeuron.LocalVariables[(int)LVarIndex.ThresholdRecoveryRate] = Config.DefaultThresholdRecoveryRate;
            newNeuron.LocalVariables[(int)LVarIndex.GeneExecutionFuel] = Config.DefaultGeneFuel;

            newNeuron.Brain.SetPrng(_rng);

            lock (_worldApiLock)
            {
                _neurons.Add(newNeuron.Id, newNeuron);

                var eventId = (ulong)Interlocked.Increment(ref _nextEventId);
                var newEvent = new Event { 
                    Id = eventId, 
                    Type = EventType.ExecuteGene, 
                    TargetId = newNeuron.Id, 
                    ExecutionTick = CurrentTick + 1, 
                    Payload = new EventPayload{ GeneId = SYS_GENE_GESTATION }
                };
                _eventQueue.Push(newEvent);
                Log("EVENT_QUEUE", LogLevel.Debug, $"Pushed event {newEvent.Type} for target {newEvent.TargetId} at tick {newEvent.ExecutionTick}.");
                
                _topologicallySortedNeurons = null;
                _spatialHash.Insert(newNeuron);
            }

            return newNeuron;
        }

        /// <summary>
        /// Creates a new synapse between any two valid entities (Neuron, InputNode, OutputNode).
        /// </summary>
        /// <param name="sourceId">The unique ID of the source entity (Neuron or InputNode).</param>
        /// <param name="targetId">The unique ID of the target entity (Neuron or OutputNode).</param>
        /// <param name="signalType">The behavioral type of the synapse's signal transmission.</param>
        /// <param name="weight">The connection strength, modulating the signal's magnitude.</param>
        /// <param name="parameter">A multi-purpose parameter (e.g., threshold for inputs, delay for pulses).</param>
        /// <returns>The newly created synapse, or null if the source or target IDs are invalid.</returns>
        public Synapse? AddSynapse(ulong sourceId, ulong targetId, SignalType signalType, float weight, float parameter)
        {
            lock (_worldApiLock)
            {
                bool sourceExists = _neurons.ContainsKey(sourceId) || _inputNodes.ContainsKey(sourceId);
                bool targetExists = _neurons.ContainsKey(targetId) || _outputNodes.ContainsKey(targetId);

                if (!sourceExists || !targetExists)
                {
                    Log("SIM_CORE", LogLevel.Warning, $"AddSynapse failed: Source ({sourceId}) or Target ({targetId}) does not exist.");
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
        }

        /// <summary>
        /// Adds a new global input node to the world.
        /// </summary>
        /// <param name="id">The unique identifier for the new input node.</param>
        /// <param name="initialValue">The starting value for the input node.</param>
        public void AddInputNode(ulong id, float initialValue = 0f)
        {
            lock (_worldApiLock)
            {
                if (_inputNodes.TryAdd(id, new InputNode { Id = id, Value = initialValue }))
                {
                    _topologicallySortedNeurons = null;
                }
            }
        }

        /// <summary>
        /// Adds a new global output node to the world.
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
        /// Removes a global input node and all its outgoing synapses from the world.
        /// </summary>
        /// <param name="id">The ID of the input node to remove.</param>
        /// <returns>True if the node was found and removed, false otherwise.</returns>
        public bool RemoveInputNode(ulong id)
        {
            lock (_worldApiLock)
            {
                if (!_inputNodes.Remove(id)) return false;
                
                var synapsesToRemove = _synapses.Where(s => s.SourceId == id).ToList();
                foreach (var synapse in synapsesToRemove)
                {
                    RemoveSynapseInternal(synapse);
                }
                _topologicallySortedNeurons = null;
            }
            return true;
        }
        
        /// <summary>
        /// Removes a global output node and all its incoming synapses from the world.
        /// </summary>
        /// <param name="id">The ID of the output node to remove.</param>
        /// <returns>True if the node was found and removed, false otherwise.</returns>
        public bool RemoveOutputNode(ulong id)
        {
            lock (_worldApiLock)
            {
                if (!_outputNodes.Remove(id)) return false;

                var synapsesToRemove = _synapses.Where(s => s.TargetId == id).ToList();
                foreach (var synapse in synapsesToRemove)
                {
                    RemoveSynapseInternal(synapse);
                }
            }
            return true;
        }

        /// <summary>
        /// Sets the values of multiple input nodes at once.
        /// </summary>
        /// <param name="values">A dictionary mapping input node IDs to their new values.</param>
        public void SetInputValues(Dictionary<ulong, float> values)
        {
            lock (_worldApiLock)
            {
                foreach (var (key, value) in values)
                {
                    if (_inputNodes.TryGetValue(key, out var node))
                    {
                        node.Value = value;
                    }
                }
            }
        }
        
        /// <summary>
        /// Sets the values of multiple global hormones at once.
        /// </summary>
        /// <param name="values">A dictionary mapping hormone indices to their new values.</param>
        public void SetGlobalHormones(Dictionary<int, float> values)
        {
            lock (_worldApiLock)
            {
                foreach (var(index, value) in values)
                {
                    if (index >= 0 && index < GlobalHormones.Length)
                    {
                        GlobalHormones[index] = value;
                    }
                }
            }
        }
        
        /// <summary>
        /// Sets the values of multiple local variables for a specific neuron.
        /// Only user-writable LVars can be modified.
        /// </summary>
        /// <param name="neuronId">The ID of the target neuron.</param>
        /// <param name="values">A dictionary mapping LVar indices to their new values.</param>
        public void SetLocalVariables(ulong neuronId, Dictionary<int, float> values)
        {
            lock (_worldApiLock)
            {
                if (!_neurons.TryGetValue(neuronId, out var neuron)) return;

                foreach (var(index, value) in values)
                {
                    if (index >= 0 && index < (int)LVarIndex.RefractoryTimeLeft)
                    {
                        if (index < neuron.LocalVariables.Length)
                        {
                            neuron.LocalVariables[index] = value;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Adds a neuron to the deactivation queue to be processed at the end of the current tick.
        /// </summary>
        /// <param name="neuron">The neuron to mark for deactivation.</param>
        public void MarkNeuronForDeactivation(Neuron neuron)
        {
            lock (_worldApiLock)
            {
                if (neuron.IsActive && !_neuronsToDeactivate.Contains(neuron))
                {
                    _neuronsToDeactivate.Add(neuron);
                }
            }
        }

        /// <summary>
        /// Handles the biological process of mitosis: copying a neuron and scheduling the Mitosis gene
        /// for both parent and child.
        /// </summary>
        /// <param name="parent">The neuron to copy.</param>
        /// <param name="offset">The position offset for the new child neuron relative to the parent.</param>
        /// <returns>The newly created child neuron.</returns>
        public Neuron PerformMitosis(Neuron parent, Vector3 offset)
        {
            var childNeuron = new Neuron
            {
                Id = (ulong)Interlocked.Increment(ref _nextNeuronId),
                IsActive = true,
                Position = parent.Position + offset,
                LocalVariables = (float[])parent.LocalVariables.Clone(),
                Brain = parent.Brain 
            };
            
            childNeuron.Brain.SetPrng(_rng);
            childNeuron.LocalVariables[(int)LVarIndex.Age] = 0;

            lock (_worldApiLock)
            {
                _neurons.Add(childNeuron.Id, childNeuron);

                var parentEventId = (ulong)Interlocked.Increment(ref _nextEventId);
                var parentEvent = new Event { Id = parentEventId, Type = EventType.ExecuteGene, TargetId = parent.Id, ExecutionTick = CurrentTick + 1, Payload = new EventPayload{ GeneId = SYS_GENE_MITOSIS } };
                _eventQueue.Push(parentEvent);

                var childEventId = (ulong)Interlocked.Increment(ref _nextEventId);
                var childEvent = new Event { Id = childEventId, Type = EventType.ExecuteGene, TargetId = childNeuron.Id, ExecutionTick = CurrentTick + 1, Payload = new EventPayload{ GeneId = SYS_GENE_MITOSIS } };
                _eventQueue.Push(childEvent);
                
                _topologicallySortedNeurons = null;
                _spatialHash.Insert(childNeuron);
            }

            return childNeuron;
        }

        /// <summary>
        /// Removes a neuron and all its associated synapses from the world.
        /// </summary>
        /// <param name="id">The ID of the neuron to remove.</param>
        /// <returns>True if the neuron was found and removed, false otherwise.</returns>
        public bool RemoveNeuron(ulong id)
        {
            lock (_worldApiLock)
            {
                if (!_neurons.Remove(id, out var neuronToRemove))
                {
                    return false;
                }

                RebuildSpatialHash(); 

                var synapsesToRemove = _synapses.Where(s => s.SourceId == id || s.TargetId == id).ToList();
                foreach (var synapse in synapsesToRemove)
                {
                    RemoveSynapseInternal(synapse);
                }

                _topologicallySortedNeurons = null;
            }
            return true;
        }

        /// <summary>
        /// Removes a synapse from the world.
        /// </summary>
        /// <param name="id">The ID of the synapse to remove.</param>
        /// <returns>True if the synapse was found and removed, false otherwise.</returns>
        public bool RemoveSynapse(ulong id)
        {
            lock (_worldApiLock)
            {
                var synapseToRemove = _synapses.FirstOrDefault(s => s.Id == id);
                if (synapseToRemove == null)
                {
                    return false;
                }

                RemoveSynapseInternal(synapseToRemove);
                _topologicallySortedNeurons = null;
            }
            return true;
        }
        
        #endregion
        
        #region Public Simulation Control

        /// <summary>
        /// Applies a set of input values and then advances the simulation by one tick.
        /// </summary>
        /// <param name="inputs">A dictionary mapping input node IDs to their new values.</param>
        public void ApplyInputsAndStep(Dictionary<ulong, float> inputs)
        {
            SetInputValues(inputs);
            Step();
        }

        /// <summary>
        /// Runs the simulation for a specified number of ticks.
        /// </summary>
        /// <param name="ticks">The number of ticks to run.</param>
        public void RunFor(ulong ticks)
        {
            for (ulong i = 0; i < ticks; i++) Step();
        }

        /// <summary>
        /// Runs the simulation continuously until a specified condition is met.
        /// </summary>
        /// <param name="stopCondition">A function that returns true when the simulation should stop.</param>
        public void RunUntil(Func<HidraWorld, bool> stopCondition)
        {
            while (!stopCondition(this)) 
            {
                Step();
            }
        }
        
        #endregion

        #region Private Helpers

        /// <summary>
        /// Provides direct, internal access to the live global hormones array.
        /// This should only be used by trusted internal systems like the HidraSprakBridge
        /// that need to read intra-tick state changes. The 'internal' keyword ensures
        /// it is not visible to external assemblies.
        /// </summary>
        /// <returns>A direct reference to the GlobalHormones array.</returns>
        internal float[] GetGlobalHormonesDirect()
        {
            // This method intentionally bypasses the defensive clone for performance
            // and to allow reading of live state within a single tick.
            return this.GlobalHormones;
        }

        /// <summary>
        /// Gets a snapshot of all neuron IDs, sorted for deterministic ordering.
        /// </summary>
        public IReadOnlyList<ulong> GetNeuronIdsSnapshot()
        {
            lock (_worldApiLock)
            {
                return _neurons.Count == 0
                    ? Array.Empty<ulong>()
                    : _neurons.Keys.OrderBy(k => k).ToList();
            }
        }

        /// <summary>
        /// Gets a snapshot of all input node IDs, sorted for deterministic ordering.
        /// </summary>
        public IReadOnlyList<ulong> GetInputIdsSnapshot()
        {
            lock (_worldApiLock)
            {
                return _inputNodes.Count == 0
                    ? Array.Empty<ulong>()
                    : _inputNodes.Keys.OrderBy(k => k).ToList();
            }
        }

        /// <summary>
        /// Gets a snapshot of all output node IDs, sorted for deterministic ordering.
        /// </summary>
        public IReadOnlyList<ulong> GetOutputIdsSnapshot()
        {
            lock (_worldApiLock)
            {
                return _outputNodes.Count == 0
                    ? Array.Empty<ulong>()
                    : _outputNodes.Keys.OrderBy(k => k).ToList();
            }
        }

        private static readonly Comparer<Synapse> SynapseIdComparer = Comparer<Synapse>.Create((a, b) => a.Id.CompareTo(b.Id));

        private static void InsertOwnedSynapseSorted(List<Synapse> list, Synapse s)
        {
            int idx = list.BinarySearch(s, SynapseIdComparer);
            if (idx < 0) idx = ~idx;
            list.Insert(idx, s);
        }
        
        private void RemoveSynapseInternal(Synapse synapse)
        {
            _synapses.Remove(synapse);
            
            if (_neurons.TryGetValue(synapse.SourceId, out var sourceNeuron))
            {
                sourceNeuron.OwnedSynapses.Remove(synapse);
            }
            else if (_neurons.TryGetValue(synapse.TargetId, out var targetNeuron))
            {
                targetNeuron.OwnedSynapses.Remove(synapse);
            }
        }

        private Synapse AddSynapseInternal(Synapse synapse)
        {
            float initial = 0f;
            if (_inputNodes.TryGetValue(synapse.SourceId, out var inNode))
            {
                initial = inNode.Value;
            }
            else if (_neurons.TryGetValue(synapse.SourceId, out var srcNeuron))
            {
                initial = srcNeuron.GetPotential();
            }
            synapse.PreviousSourceValue = initial;

            bool mustResort = _synapses.Count > 0 && synapse.Id < _synapses[^1].Id;
            _synapses.Add(synapse);
            if (mustResort)
            {
                _synapses.Sort((a, b) => a.Id.CompareTo(b.Id));
            }

            if (_neurons.TryGetValue(synapse.SourceId, out var sourceNeuron))
            {
                InsertOwnedSynapseSorted(sourceNeuron.OwnedSynapses, synapse);
            }
            else if (_neurons.TryGetValue(synapse.TargetId, out var targetNeuron))
            {
                InsertOwnedSynapseSorted(targetNeuron.OwnedSynapses, synapse);
            }
            
            _topologicallySortedNeurons = null;
            
            return synapse;
        }

        #endregion
    }
}
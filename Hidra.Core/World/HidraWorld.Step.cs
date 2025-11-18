// Hidra.Core/World/HidraWorld.Step.cs
using Hidra.Core.Brain;
using Hidra.Core.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Hidra.Core
{
    public partial class HidraWorld
    {
        [JsonIgnore] 
        private List<Neuron>? _topologicallySortedNeurons = null;
        [JsonIgnore] 
        private Dictionary<ulong, List<Synapse>>? _incomingSynapseCache = null;
        [JsonIgnore]
        private Dictionary<ulong, List<Synapse>>? _inputSynapseCache = null;

        // Intra-tick event lists for deterministic processing
        [JsonIgnore] private readonly List<Event> _currentTickPulses = new();
        [JsonIgnore] private readonly List<Event> _currentTickOtherEvents = new();
        [JsonIgnore] private readonly List<Event> _nextTickEvents = new();

        /// <summary>
        /// Gets a value indicating whether the simulation has been stopped due to a fatal error.
        /// </summary>
        [JsonIgnore]
        public bool IsHalted { get; private set; } = false;

        /// <summary>
        /// Advances the simulation by a single time step (tick). This is a thread-safe wrapper
        /// around the main simulation logic.
        /// </summary>
        public void Step()
        {
            if (IsHalted)
            {
                Log("SIM_CORE", LogLevel.Warning, $"Attempted to step a world that has been halted due to a fatal error on tick {CurrentTick}.");
                return;
            }

            lock (_worldApiLock)
            {
                StepInternal();
            }
        }
        
        /// <summary>
        /// Orchestrates the execution of a single simulation tick through a series of distinct, fault-tolerant phases.
        /// If any phase fails, the simulation is halted to prevent data corruption.
        /// </summary>
        private void StepInternal()
        {
            Log("SIM_CORE", LogLevel.Debug, $"--- Tick {CurrentTick} Start ---");

            if (!ExecutePhase(Phase0_InitializeTick, "Initialization")) return;
            if (!ExecutePhase(Phase1_PassiveUpdates, "Passive Updates")) return;
            if (!ExecutePhase(Phase2_ProcessInputs, "Input Processing")) return;
            if (!ExecutePhase(Phase3_EvaluateNeurons, "Neuron Evaluation")) return;
            if (!ExecutePhase(Phase4_ProcessIntraTickEvents, "Event Processing")) return;
            if (!ExecutePhase(Phase5_ProcessDeactivations, "Deactivation")) return;
            if (!ExecutePhase(Phase6_QueueNewEvents, "Queue New Events")) return;
            if (!ExecutePhase(Phase7_ArchiveAndAdvance, "Archive & Advance")) return;

            Log("SIM_CORE", LogLevel.Debug, $"--- Tick {CurrentTick-1} End ---");
        }

        /// <summary>
        /// A supervisory wrapper that executes a simulation phase, catching and logging any exceptions.
        /// If an exception occurs, the simulation is halted.
        /// </summary>
        /// <param name="phaseAction">The delegate for the phase to execute.</param>
        /// <param name="phaseName">The name of the phase for logging purposes.</param>
        /// <returns>True if the phase completed successfully, false otherwise.</returns>
        private bool ExecutePhase(Action phaseAction, string phaseName)
        {
            try
            {
                phaseAction();
                return true;
            }
            catch (Exception ex)
            {
                IsHalted = true;
                Log("SIM_FATAL", LogLevel.Error, $"--- SIMULATION HALTED ON TICK {CurrentTick} DURING PHASE: {phaseName} ---");
                Log("SIM_FATAL", LogLevel.Error, $"Fatal exception: {ex.Message}\nStack Trace:\n{ex.StackTrace}");
                return false;
            }
        }

        #region Simulation Phases

        /// <summary>
        /// Phase 0: Prepares the world state for the current tick's calculations.
        /// </summary>
        private void Phase0_InitializeTick()
        {
            EnsureCachesUpToDate();
            _currentTickPulses.Clear();
            _currentTickOtherEvents.Clear();
            _nextTickEvents.Clear();
            _eventQueue.ProcessAndPartionDueEvents(CurrentTick, _currentTickPulses, _currentTickOtherEvents);
        }

        /// <summary>
        /// Phase 1: Applies passive, time-based changes to all world entities (decay, aging, etc.).
        /// </summary>
        private void Phase1_PassiveUpdates()
        {
            foreach (var neuron in _neurons.Values)
            {
                if (!neuron.IsActive) continue;
                neuron.LocalVariables[(int)LVarIndex.SomaPotential] *= (1.0f - neuron.LocalVariables[(int)LVarIndex.DecayRate]);
                neuron.LocalVariables[(int)LVarIndex.FiringRate] *= Config.FiringRateMAWeight;
                neuron.LocalVariables[(int)LVarIndex.Age]++;
                neuron.LocalVariables[(int)LVarIndex.Health] -= Config.MetabolicTaxPerTick;
                if (neuron.LocalVariables[(int)LVarIndex.Health] <= 0f) { _neuronsToDeactivate.Add(neuron); continue; }
                if (neuron.LocalVariables[(int)LVarIndex.RefractoryTimeLeft] > 0f) { neuron.LocalVariables[(int)LVarIndex.RefractoryTimeLeft]--; }
            }
            foreach (var syn in _synapses)
            {
                if (!syn.IsActive) continue;
                syn.FatigueLevel = Math.Max(0f, syn.FatigueLevel - syn.FatigueRecoveryRate);
            }
        }

        /// <summary>
        /// Phase 2: Processes external inputs, generating PotentialPulse events for the current tick.
        /// </summary>
        private void Phase2_ProcessInputs()
        {
            if (_inputSynapseCache == null) return;
            
            foreach (var inputNode in _inputNodes.Values.OrderBy(n => n.Id))
            {
                if (!_inputSynapseCache.TryGetValue(inputNode.Id, out var outgoing)) continue;
                foreach (var syn in outgoing)
                {
                    var target = _neurons.GetValueOrDefault(syn.TargetId);
                    var ctx = new ConditionContext(this, syn, null, inputNode, target, inputNode.Value);
                    bool conditionMet = syn.Condition?.Evaluate(ctx) ?? (inputNode.Value > 0f);
                    if (!conditionMet) { syn.PreviousSourceValue = inputNode.Value; continue; }

                    var evtIdPulse = (ulong)Interlocked.Increment(ref _nextEventId);
                    float pulseValue = inputNode.Value * syn.Weight;

                    ulong executionTick = syn.SignalType switch
                    {
                        SignalType.Delayed => CurrentTick + (ulong)Math.Max(0, syn.Parameter),
                        _ => CurrentTick
                    };

                    var pulseEvt = new Event 
                    { 
                        Id = evtIdPulse, 
                        ExecutionTick = executionTick,
                        Type = EventType.PotentialPulse, 
                        TargetId = syn.TargetId, 
                        Payload = new EventPayload(PulseValue: pulseValue) 
                    };
                    
                    if (executionTick == CurrentTick)
                    {
                        _currentTickPulses.Add(pulseEvt);
                    }
                    else
                    {
                        _nextTickEvents.Add(pulseEvt);
                    }
                    
                    if (target != null)
                    {
                        var lv = target.LocalVariables;
                        var pos = target.Position;
                        Log("INPUT_PULSE", LogLevel.Info,
                            $"{{\"tick\":{CurrentTick},\"inputNodeId\":{inputNode.Id},\"inputValue\":{inputNode.Value}," +
                            $"\"synapseId\":{syn.Id},\"synapseWeight\":{syn.Weight},\"signalType\":\"{syn.SignalType}\"," +
                            $"\"targetNeuronId\":{target.Id},\"targetPre\":{{\"pos\":{{\"x\":{pos.X},\"y\":{pos.Y},\"z\":{pos.Z}}}," +
                            $"\"soma\":{lv[(int)LVarIndex.SomaPotential]},\"dend\":{lv[(int)LVarIndex.DendriticPotential]},\"thr\":{lv[(int)LVarIndex.FiringThreshold]},\"athr\":{lv[(int)LVarIndex.AdaptiveThreshold]},\"rate\":{lv[(int)LVarIndex.FiringRate]}," +
                            $"\"health\":{lv[(int)LVarIndex.Health]},\"age\":{lv[(int)LVarIndex.Age]},\"refrLeft\":{lv[(int)LVarIndex.RefractoryTimeLeft]}}}," +
                            $"\"pulseEvent\":{{\"id\":{evtIdPulse},\"type\":\"PotentialPulse\",\"execTick\":{executionTick},\"targetId\":{syn.TargetId},\"payload\":{{\"PulseValue\":{pulseValue}}}}}}}");
                    }

                    syn.PreviousSourceValue = inputNode.Value;
                }
            }
        }

        /// <summary>
        /// Phase 3: Accumulates pulses and evaluates neuron firing thresholds, generating Activate events for the current tick.
        /// </summary>
        private void Phase3_EvaluateNeurons()
        {
            var accumulated = new Dictionary<ulong, float>();

            foreach (var p in _currentTickPulses)
            {
                if (!p.Payload.PulseValue.HasValue) continue;
                float val = p.Payload.PulseValue.Value;

                if (_neurons.ContainsKey(p.TargetId))
                {
                    float cur = accumulated.GetValueOrDefault(p.TargetId, 0f) + val;
                    accumulated[p.TargetId] = cur;
                    continue;
                }

                if (_outputNodes.TryGetValue(p.TargetId, out var outNode))
                {
                    outNode.Value = val;
                    Log("OUTPUT_PULSE", LogLevel.Info,
                        $"{{\"tick\":{CurrentTick},\"outputNodeId\":{outNode.Id},\"value\":{outNode.Value},\"eventId\":{p.Id}}}");
                    continue;
                }
            }

            foreach (var neuron in _topologicallySortedNeurons!)
            {
                if (!neuron.IsActive) continue;
                float dend = CalculateDendriticBaseline(neuron);
                neuron.LocalVariables[(int)LVarIndex.DendriticPotential] = dend;
                float incoming = accumulated.GetValueOrDefault(neuron.Id, 0f);
                neuron.LocalVariables[(int)LVarIndex.SomaPotential] += incoming;

                if (neuron.LocalVariables[(int)LVarIndex.RefractoryTimeLeft] <= 0f)
                {
                    float thr = neuron.LocalVariables[(int)LVarIndex.FiringThreshold] + neuron.LocalVariables[(int)LVarIndex.AdaptiveThreshold];
                    float total = neuron.LocalVariables[(int)LVarIndex.SomaPotential] + dend;
                    if (total >= thr)
                    {
                        var evtIdActivate = (ulong)Interlocked.Increment(ref _nextEventId);
                        var activateEvt = new Event { Id = evtIdActivate, ExecutionTick = CurrentTick, Type = EventType.Activate, TargetId = neuron.Id, Payload = new EventPayload(ActivationPotential: total) };
                        _currentTickOtherEvents.Add(activateEvt);
                        Log("EVENT_GEN", LogLevel.Debug, $"Neuron {neuron.Id} threshold met ({total} >= {thr}); queued Activate event {evtIdActivate} for tick {CurrentTick}.");
                    }
                }
            }
        }

        /// <summary>
        /// Phase 4: Processes all non-pulse events scheduled for this tick, such as neuron activations.
        /// This phase generates events for FUTURE ticks.
        /// </summary>
        private void Phase4_ProcessIntraTickEvents()
        {
            _currentTickOtherEvents.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var e in _currentTickOtherEvents)
                ProcessEvent(e);
        }

        /// <summary>
        /// Phase 5: Deactivates neurons that have "died" during this tick and queues any resulting events.
        /// </summary>
        private void Phase5_ProcessDeactivations()
        {
            if (_neuronsToDeactivate.Count > 0)
            {
                foreach (var n in _neuronsToDeactivate)
                {
                    if (!n.IsActive) continue;
                    QueueApoptosisEventsFor(n);
                    n.IsActive = false;
                }
                _neuronsToDeactivate.Clear();
                _topologicallySortedNeurons = null; _incomingSynapseCache = null; _inputSynapseCache = null;
            }
        }

        /// <summary>
        /// Phase 6: Takes all newly generated future events from the buffer and adds them to the main event queue.
        /// </summary>
        private void Phase6_QueueNewEvents()
        {
            foreach (var evt in _nextTickEvents)
            {
                _eventQueue.Push(evt);
            }
        }

        /// <summary>
        /// Phase 7: Archives all events processed in this tick, collects metrics, and increments the tick counter.
        /// </summary>
        private void Phase7_ArchiveAndAdvance()
        {
            if (Config.MetricsEnabled &&
                _metricsRing != null &&
                _neurons.Count > 0 &&
                CurrentTick % (ulong)Config.MetricsCollectionInterval == 0)
            {
                var snapshot = BuildWorldSnapshot(includeSynapses: Config.MetricsIncludeSynapses);
                _metricsRing[_metricsHead] = snapshot;
                _metricsHead = (_metricsHead + 1) % Config.MetricsRingCapacity;
                if (_metricsHead == 0 && Config.MetricsRingCapacity > 0)
                {
                    _metricsWrapped = true;
                }
            }

            ulong completedTick = CurrentTick;  // The tick that just finished executing
            
            lock (_eventHistoryLock)
            {
                if (!_eventHistory.TryGetValue(completedTick, out var bucket))
                {
                    bucket = new List<Event>(_currentTickPulses.Count + _currentTickOtherEvents.Count);
                    _eventHistory[completedTick] = bucket;  // Use completedTick instead of CurrentTick
                }
                bucket.AddRange(_currentTickPulses);
                bucket.AddRange(_currentTickOtherEvents);
            }
            
            CurrentTick++;  // Advance to next tick AFTER archiving
        }

        #endregion
        
        /// <summary>
        /// Handles the logic for a single event based on its type.
        /// </summary>
        private void ProcessEvent(Event e)
        {
            switch (e.Type)
            {
                case EventType.ExecuteGene:
                    if (e.Payload.GeneId.HasValue) 
                        ExecuteGene(e.Payload.GeneId.Value, _neurons.GetValueOrDefault(e.TargetId), GetInitialContextForGene(e.Payload.GeneId.Value));
                    break;
                    
                case EventType.ExecuteGeneFromBrain:
                    if (e.Payload.GeneId.HasValue) 
                        ExecuteGene(e.Payload.GeneId.Value, _neurons.GetValueOrDefault(e.TargetId), GetInitialContextForGene(e.Payload.GeneId.Value));
                    break;
                    
                case EventType.Activate:
                    if (_neurons.TryGetValue(e.TargetId, out var neuron) && neuron.IsActive && e.Payload.ActivationPotential.HasValue)
                        ProcessNeuronActivation(neuron, e.Payload.ActivationPotential.Value);
                    break;
            }
        }
        
        /// <summary>
        /// Handles the full sequence of actions when a neuron fires, from brain evaluation to synaptic propagation.
        /// </summary>
        private void ProcessNeuronActivation(Neuron neuron, float activationPotential)
        {
            var logBuilder = new System.Text.StringBuilder();
            logBuilder.AppendLine($"--- NEURON ACTIVATION START ---");
            logBuilder.AppendLine($" Tick: {CurrentTick}, Neuron ID: {neuron.Id}");
            logBuilder.AppendLine(" Neuron State at Activation:");
            logBuilder.AppendLine($"  - IsActive: {neuron.IsActive}");
            logBuilder.AppendLine($"  - Position: {{ X:{neuron.Position.X}, Y:{neuron.Position.Y}, Z:{neuron.Position.Z} }}");
            logBuilder.AppendLine($"  - Soma Potential (pre-reset): {neuron.LocalVariables[(int)LVarIndex.SomaPotential]}");
            logBuilder.AppendLine($"  - Dendritic Potential: {neuron.LocalVariables[(int)LVarIndex.DendriticPotential]}");
            logBuilder.AppendLine($"  - Health: {neuron.LocalVariables[(int)LVarIndex.Health]}");
            logBuilder.AppendLine($"  - Age: {neuron.LocalVariables[(int)LVarIndex.Age]}");
            logBuilder.AppendLine($"  - Firing Rate: {neuron.LocalVariables[(int)LVarIndex.FiringRate]}");
            logBuilder.AppendLine($"  - Refractory Time Left (pre-reset): {neuron.LocalVariables[(int)LVarIndex.RefractoryTimeLeft]}");
            logBuilder.AppendLine(" Activation Inputs:");
            logBuilder.AppendLine($"  - Activation Potential (from threshold check): {activationPotential}");
            float brainOutputValue = 0f;
            var brainInputs = new float[neuron.Brain.InputMap.Count];
            logBuilder.AppendLine("  - Brain Inputs Evaluation:");
            if (brainInputs.Length == 0)
            {
                logBuilder.AppendLine("    - No inputs mapped to the brain.");
            }
            for (int i = 0; i < brainInputs.Length; i++)
            {
                var mapping = neuron.Brain.InputMap[i];
                brainInputs[i] = GetValueForBrainInput(mapping, neuron, activationPotential);
                logBuilder.AppendLine($"    - Input[{i}]: Source={mapping.SourceType}, Index={mapping.SourceIndex} -> Value={brainInputs[i]}");
            }
            logBuilder.AppendLine(" Brain Evaluation:");
            logBuilder.AppendLine($"  - Brain Type: {neuron.Brain.GetType().Name}");
            neuron.Brain.Evaluate(brainInputs, _logAction);
            logBuilder.AppendLine($"  - Initial Brain Output Value: {brainOutputValue}");
            if (neuron.Brain.OutputMap.Count == 0)
            {
                logBuilder.AppendLine("    - No outputs mapped from the brain. Output value remains the activation potential.");
            }
            foreach (var output in neuron.Brain.OutputMap)
            {
                logBuilder.AppendLine($"  - Processing Brain Output: Action={output.ActionType}, Value={output.Value}");
                switch (output.ActionType)
                {
                    case OutputActionType.SetOutputValue:
                        brainOutputValue = output.Value;
                        logBuilder.AppendLine($"    - Outcome: Brain output value was SET to {brainOutputValue}.");
                        break;

                    case OutputActionType.ExecuteGene:
                        uint internalGeneId = (uint)Math.Abs(output.Value) + Config.SystemGeneCount;
                        var execEvtId = (ulong)Interlocked.Increment(ref _nextEventId);
                        var execEvt = new Event
                        {
                            Id = execEvtId,
                            ExecutionTick = CurrentTick + 1,
                            Type = EventType.ExecuteGeneFromBrain,
                            TargetId = neuron.Id,
                            Payload = new EventPayload { GeneId = internalGeneId }
                        };
                        _nextTickEvents.Add(execEvt);
                        logBuilder.AppendLine($"    - Outcome: Queued ExecuteGeneFromBrain event for GeneID {internalGeneId} on Tick {CurrentTick + 1}.");
                        break;
                }
            }
            logBuilder.AppendLine($"  - Final Brain Output Value for Synapses: {brainOutputValue}");
            logBuilder.AppendLine(" Outgoing Synapse Propagation:");
            if (neuron.OwnedSynapses.Count == 0)
            {
                logBuilder.AppendLine("  - No outgoing synapses from this neuron.");
            }
            foreach (var synapse in neuron.OwnedSynapses.Where(s => s.SourceId == neuron.Id))
            {
                logBuilder.AppendLine($"  - Evaluating Synapse ID: {synapse.Id} (Target: {synapse.TargetId})");
                logBuilder.AppendLine($"    - Config: IsActive={synapse.IsActive}, Type={synapse.SignalType}, Weight={synapse.Weight}, Param={synapse.Parameter}, Fatigue={synapse.FatigueLevel}, Condition exists={synapse.Condition != null}");
                if (!synapse.IsActive)
                {
                    logBuilder.AppendLine("    - Outcome: Synapse is INACTIVE. No pulse generated.");
                    continue;
                }
                var targetNeuron = _neurons.GetValueOrDefault(synapse.TargetId);
                var context = new ConditionContext(this, synapse, neuron, null, targetNeuron, brainOutputValue);
                bool conditionMet = synapse.Condition?.Evaluate(context) ?? true;
                if (!conditionMet)
                {
                    logBuilder.AppendLine($"    - Outcome: Condition check FAILED. No pulse generated.");
                    synapse.PreviousSourceValue = brainOutputValue;
                    continue;
                }
                logBuilder.AppendLine("    - Condition check PASSED.");
                if (synapse.SignalType == SignalType.Persistent)
                {
                    logBuilder.AppendLine("    - Outcome: SignalType is Persistent. This type does not generate pulse events on activation.");
                }
                else
                {
                    float transmittedValue = CalculateTransmittedValue(brainOutputValue, synapse, true);
                    var pulseEventId = (ulong)Interlocked.Increment(ref _nextEventId);
                    ulong executionTick = synapse.SignalType switch
                    {
                        SignalType.Immediate => CurrentTick + 1,
                        SignalType.Delayed   => CurrentTick + 1 + (ulong)Math.Max(0, synapse.Parameter),
                        _                    => CurrentTick + 1
                    };
                    var newEvent = new Event
                    {
                        Id = pulseEventId,
                        Type = EventType.PotentialPulse,
                        TargetId = synapse.TargetId,
                        ExecutionTick = executionTick,
                        Payload = new EventPayload { PulseValue = transmittedValue }
                    };
                    _nextTickEvents.Add(newEvent);
                    logBuilder.AppendLine($"    - Outcome: SUCCESS. Queued PotentialPulse event for Tick {executionTick}.");
                    logBuilder.AppendLine($"      - Event ID: {newEvent.Id}, Target ID: {newEvent.TargetId}, Transmitted Value: {transmittedValue}");
                }
                synapse.PreviousSourceValue = brainOutputValue;
            }
            neuron.LocalVariables[(int)LVarIndex.SomaPotential] = 0;
            neuron.LocalVariables[(int)LVarIndex.RefractoryTimeLeft] = neuron.LocalVariables[(int)LVarIndex.RefractoryPeriod];
            neuron.LocalVariables[(int)LVarIndex.FiringRate] += (1.0f - Config.FiringRateMAWeight);
            logBuilder.AppendLine(" Post-Activation State Update:");
            logBuilder.AppendLine($"  - SomaPotential reset to: {neuron.LocalVariables[(int)LVarIndex.SomaPotential]}");
            logBuilder.AppendLine($"  - RefractoryTimeLeft set to: {neuron.LocalVariables[(int)LVarIndex.RefractoryTimeLeft]}");
            logBuilder.AppendLine($"  - FiringRate updated to: {neuron.LocalVariables[(int)LVarIndex.FiringRate]}");
            logBuilder.AppendLine("--- NEURON ACTIVATION END ---");
            Log("SIM_CORE_ACTIVATION", LogLevel.Info, logBuilder.ToString());
        }

        /// <summary>
        /// Calculates the baseline dendritic potential for a neuron from its active, persistent incoming synapses.
        /// </summary>
        private float CalculateDendriticBaseline(Neuron neuron)
        {
            float dendriticSum = 0f;
            if (_incomingSynapseCache == null || !_incomingSynapseCache.TryGetValue(neuron.Id, out var incoming))
                return 0f;
            foreach (var syn in incoming)
            {
                if (!syn.IsActive || syn.SignalType != SignalType.Persistent) continue;
                float sourceValue = 0f;
                if (_inputNodes.TryGetValue(syn.SourceId, out var inNode))
                {
                    sourceValue = inNode.Value;
                }
                else if (_neurons.TryGetValue(syn.SourceId, out var srcNeuron) && srcNeuron.IsActive)
                {
                    sourceValue = srcNeuron.LocalVariables[(int)LVarIndex.DendriticPotential]
                                + srcNeuron.LocalVariables[(int)LVarIndex.SomaPotential];
                }
                dendriticSum += sourceValue * syn.Weight;
            }
            return dendriticSum;
        }

        /// <summary>
        /// Queues the Apoptosis gene to be executed on all downstream neurons connected via a deceased neuron's outgoing synapses.
        /// </summary>
        private void QueueApoptosisEventsFor(Neuron deceasedNeuron)
        {
            foreach (var synapse in deceasedNeuron.OwnedSynapses.Where(s => s.SourceId == deceasedNeuron.Id && _neurons.ContainsKey(s.TargetId)))
            {
                var eventId = (ulong)Interlocked.Increment(ref _nextEventId);
                var newEvent = new Event { 
                    Id = eventId, 
                    Type = EventType.ExecuteGene, 
                    TargetId = synapse.TargetId, 
                    ExecutionTick = CurrentTick + 1, 
                    Payload = new EventPayload { GeneId = SYS_GENE_APOPTOSIS }
                };
                _nextTickEvents.Add(newEvent);
            }
        }

        /// <summary>
        /// Retrieves the appropriate value from the world or a neuron for a brain's input request.
        /// </summary>
        private float GetValueForBrainInput(BrainInput brainInput, Neuron neuron, float activationPotential)
        {
            const int LVAR_COUNT = 256; // Assuming this constant is available or defined elsewhere
            switch (brainInput.SourceType)
            {
                case InputSourceType.ActivationPotential:
                    return activationPotential;
                case InputSourceType.CurrentPotential:
                    return neuron.GetPotential();
                case InputSourceType.Health:
                    return neuron.LocalVariables[(int)LVarIndex.Health];
                case InputSourceType.Age:
                    return neuron.LocalVariables[(int)LVarIndex.Age];
                case InputSourceType.FiringRate:
                    return neuron.LocalVariables[(int)LVarIndex.FiringRate];
                case InputSourceType.LocalVariable:
                    return (brainInput.SourceIndex >= 0 && brainInput.SourceIndex < LVAR_COUNT) ? neuron.LocalVariables[brainInput.SourceIndex] : 0f;
                case InputSourceType.GlobalHormone:
                    return (brainInput.SourceIndex >= 0 && brainInput.SourceIndex < GlobalHormones.Length) ? GlobalHormones[brainInput.SourceIndex] : 0f;
                case InputSourceType.ConstantOne:
                    return 1.0f;
                case InputSourceType.ConstantZero:
                    return 0.0f;

                // --- START OF NEW IMPLEMENTATION ---
                case InputSourceType.SynapseValue:
                {
                    // The cache contains all synapses where the target is this neuron.
                    if (_incomingSynapseCache != null && _incomingSynapseCache.TryGetValue(neuron.Id, out var incomingSynapses))
                    {
                        // Ensure deterministic order by sorting by Synapse ID before indexing.
                        incomingSynapses.Sort((a, b) => a.Id.CompareTo(b.Id));
                        
                        int synapseIndex = brainInput.SourceIndex;
                        if (synapseIndex >= 0 && synapseIndex < incomingSynapses.Count)
                        {
                            // Read the value from the PREVIOUS tick to ensure deterministic, order-independent evaluation.
                            return incomingSynapses[synapseIndex].PreviousSourceValue;
                        }
                    }
                    // If the index is invalid or the neuron has no incoming synapses, return 0.
                    return 0f;
                }
                // --- END OF NEW IMPLEMENTATION ---

                default:
                    return 0f;
            }
        }
        
        /// <summary>
        /// Calculates the final value transmitted by a synapse after applying weight and fatigue.
        /// </summary>
        private float CalculateTransmittedValue(float inputValue, Synapse synapse, bool applyFatigue)
        {
            float effectiveWeight = synapse.Weight * (1.0f - synapse.FatigueLevel);
            float value = inputValue * effectiveWeight;
            if (applyFatigue)
            {
                synapse.FatigueLevel = Math.Min(1.0f, synapse.FatigueLevel + Math.Abs(value) * synapse.FatigueRate);
            }
            return value;
        }

        /// <summary>
        /// Ensures that runtime caches are built before they are accessed.
        /// </summary>
        private void EnsureCachesUpToDate()
        {
            if (_topologicallySortedNeurons == null)
            {
                RebuildCaches();
            }
        }

        /// <summary>
        /// Rebuilds all runtime caches, including neuron sort order and synapse lookups.
        /// </summary>
        private void RebuildCaches()
        {
            _topologicallySortedNeurons = TopologicallySortNeurons();
            
            _incomingSynapseCache = new Dictionary<ulong, List<Synapse>>();
            _inputSynapseCache = new Dictionary<ulong, List<Synapse>>();

            foreach (var synapse in _synapses)
            {
                if (_neurons.ContainsKey(synapse.TargetId))
                {
                    if (!_incomingSynapseCache.ContainsKey(synapse.TargetId))
                        _incomingSynapseCache[synapse.TargetId] = new List<Synapse>();
                    _incomingSynapseCache[synapse.TargetId].Add(synapse);
                }

                if (_inputNodes.ContainsKey(synapse.SourceId))
                {
                     if (!_inputSynapseCache.ContainsKey(synapse.SourceId))
                        _inputSynapseCache[synapse.SourceId] = new List<Synapse>();
                    _inputSynapseCache[synapse.SourceId].Add(synapse);
                }
            }
        }

        /// <summary>
        /// Performs a topological sort of neurons based on their shortest path distance from an input node,
        /// which helps process signals in a more natural data-flow order. Handles graph cycles.
        /// </summary>
        private List<Neuron> TopologicallySortNeurons()
        {
            Log("SIM_CORE", LogLevel.Info, "Recalculating neuron topological sort based on shortest path from input nodes...");
            if (_neurons.Count == 0) return new List<Neuron>();

            var adj = _neurons.ToDictionary(kvp => kvp.Key, kvp => new List<ulong>());
            var inDegree = _neurons.ToDictionary(kvp => kvp.Key, kvp => 0);
            foreach (var s in _synapses.Where(s => _neurons.ContainsKey(s.SourceId) && _neurons.ContainsKey(s.TargetId)))
            {
                adj[s.SourceId].Add(s.TargetId);
                inDegree[s.TargetId]++;
            }

            var distances = _neurons.ToDictionary(kvp => kvp.Key, kvp => int.MaxValue);
            var queue = new Queue<ulong>();
            foreach (var s in _synapses.Where(s => _inputNodes.ContainsKey(s.SourceId) && _neurons.ContainsKey(s.TargetId)))
            {
                if (distances[s.TargetId] == int.MaxValue)
                {
                    distances[s.TargetId] = 0;
                    queue.Enqueue(s.TargetId);
                }
            }
            
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                foreach (var v in adj[u])
                {
                    if (distances[v] > distances[u] + 1)
                    {
                        distances[v] = distances[u] + 1;
                        queue.Enqueue(v);
                    }
                }
            }

            var pq = new PriorityQueue<ulong, (int distance, ulong id)>();
            foreach (var neuron in _neurons.Values)
            {
                if (inDegree[neuron.Id] == 0)
                {
                    pq.Enqueue(neuron.Id, (distances[neuron.Id], neuron.Id));
                }
            }

            var sortedList = new List<Neuron>();
            while (pq.Count > 0)
            {
                var uId = pq.Dequeue();
                sortedList.Add(_neurons[uId]);

                foreach (var vId in adj[uId])
                {
                    inDegree[vId]--;
                    if (inDegree[vId] == 0)
                    {
                        pq.Enqueue(vId, (distances[vId], vId));
                    }
                }
            }

            if (sortedList.Count < _neurons.Count)
            {
                Log("SIM_CORE", LogLevel.Warning, "Cycle detected in neuron graph. Appending cyclic neurons to processing order.");
                var cyclicNeurons = _neurons.Values.Where(n => inDegree[n.Id] > 0).ToList();
                cyclicNeurons.Sort((a, b) => {
                    int distCompare = distances[a.Id].CompareTo(distances[b.Id]);
                    return distCompare != 0 ? distCompare : a.Id.CompareTo(b.Id);
                });
                sortedList.AddRange(cyclicNeurons);
            }

            return sortedList;
        }
    }
}
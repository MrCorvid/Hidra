// Hidra.Core/World/HidraWorld.Step.cs
using Hidra.Core.Brain;
using Hidra.Core.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Hidra.Core
{
    /// <summary>
    /// This partial class contains the core simulation loop logic for the HidraWorld.
    /// </summary>
    public partial class HidraWorld
    {
        /// <summary>
        /// Advances the simulation by a single time step (tick). This is the main engine loop.
        /// </summary>
        public void Step()
        {
            CurrentTick++;
            Logger.Log("SIM_CORE", LogLevel.Debug, $"--- Tick {CurrentTick} Start ---");

            // PHASE 1: Take snapshots of all node states from the end of the previous tick.
            var inputSnapshots = _inputNodes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value);
            var neuronSnapshots = _neurons.Values.Where(n => n.IsActive)
                                          .ToDictionary(n => n.Id, n => n.LocalVariables[(int)LVarIndex.SomaPotential] + n.LocalVariables[(int)LVarIndex.DendriticPotential]);

            // PHASE 2: Apply decay, recovery, and aging. Dendritic potential is reset to 0 here.
            foreach (var neuron in _neurons.Values)
            {
                if (!neuron.IsActive) continue;
                
                neuron.LocalVariables[(int)LVarIndex.SomaPotential] *= neuron.LocalVariables[(int)LVarIndex.DecayRate];
                neuron.LocalVariables[(int)LVarIndex.DendriticPotential] = 0f;
                
                neuron.LocalVariables[(int)LVarIndex.Age]++;
                neuron.LocalVariables[(int)LVarIndex.Health] -= Config.MetabolicTaxPerTick;
                if (neuron.LocalVariables[(int)LVarIndex.Health] <= 0) { neuron.IsActive = false; continue; }

                if (neuron.LocalVariables[(int)LVarIndex.RefractoryTimeLeft] > 0) neuron.LocalVariables[(int)LVarIndex.RefractoryTimeLeft]--;
                neuron.LocalVariables[(int)LVarIndex.AdaptiveThreshold] *= (1.0f - neuron.LocalVariables[(int)LVarIndex.ThresholdRecoveryRate]);
                neuron.LocalVariables[(int)LVarIndex.FiringRate] *= Config.FiringRateMAWeight;
            }
            foreach (var synapse in _synapses)
            {
                if(synapse.IsActive) synapse.FatigueLevel *= (1.0f - synapse.FatigueRecoveryRate);
            }

            // --- PHASE 3: Single-Pass Universal Continuous Signal Processing ---
            var dendriticAccumulator = new Dictionary<ulong, KahanAccumulator>();

            // In this single pass, we calculate the effect of all continuous signals based on the snapshots.
            foreach (var synapse in _synapses.Where(s => s.IsActive && s.SignalType != SignalType.Delayed))
            {
                // Determine the source value from the appropriate snapshot.
                float sourceValue = 0f;
                if (_inputNodes.ContainsKey(synapse.SourceId))
                {
                    inputSnapshots.TryGetValue(synapse.SourceId, out sourceValue);
                }
                else if (_neurons.ContainsKey(synapse.SourceId))
                {
                    neuronSnapshots.TryGetValue(synapse.SourceId, out sourceValue);
                }

                // Evaluate condition, if any.
                _neurons.TryGetValue(synapse.TargetId, out var targetNeuron);
                var context = new ConditionContext(this, synapse, _neurons.GetValueOrDefault(synapse.SourceId), _inputNodes.GetValueOrDefault(synapse.SourceId), targetNeuron, sourceValue);
                if (synapse.Condition != null && !synapse.Condition.Evaluate(context)) continue;

                float transmittedValue = 0f;
                switch (synapse.SignalType)
                {
                    case SignalType.Immediate:
                        transmittedValue = CalculateTransmittedValue(sourceValue, synapse, true);
                        break;
                    case SignalType.Persistent:
                        if (synapse.IsPersistentValueSet) transmittedValue = synapse.PersistentValue;
                        break;
                    case SignalType.Transient:
                        if (synapse.TransientTriggerTick == CurrentTick) transmittedValue = CalculateTransmittedValue(1.0f, synapse, true);
                        break;
                }
                
                if (transmittedValue == 0f) continue;
                
                // Accumulate the effects for all targets.
                if (targetNeuron != null && targetNeuron.IsActive)
                {
                    if (!dendriticAccumulator.ContainsKey(targetNeuron.Id)) dendriticAccumulator[targetNeuron.Id] = new KahanAccumulator();
                    dendriticAccumulator[targetNeuron.Id].Add(transmittedValue);
                }
                else if (_outputNodes.TryGetValue(synapse.TargetId, out var outputNode))
                {
                    float smoothing = Math.Clamp(synapse.Parameter, 0.0f, 1.0f);
                    outputNode.Value = (1f - smoothing) * outputNode.Value + smoothing * transmittedValue;
                }
            }

            // PHASE 3 (continued): Apply the final accumulated dendritic potentials.
            foreach (var kvp in dendriticAccumulator)
            {
                if (_neurons.TryGetValue(kvp.Key, out var neuron) && neuron.IsActive)
                {
                    neuron.LocalVariables[(int)LVarIndex.DendriticPotential] = kvp.Value.Sum;
                }
            }
            
            // PHASE 4 & 5: Process event-based triggers (pulsed inputs and neuron firing).
            // These use the fresh input snapshot and the now-calculated total potential for this tick.
            foreach (var synapse in _synapses.Where(s => s.IsActive && _inputNodes.ContainsKey(s.SourceId)))
            {
                if (inputSnapshots.TryGetValue(synapse.SourceId, out var inputValue) && inputValue >= synapse.Parameter)
                {
                    float pulsePayload = CalculateTransmittedValue(1.0f, synapse, true);
                    if (synapse.SignalType == SignalType.Delayed)
                    {
                        _eventQueue.Push(new Event { Id = (ulong)Interlocked.Increment(ref _nextEventId), Type = EventType.PotentialPulse, TargetId = synapse.TargetId, ExecutionTick = CurrentTick + 1, Payload = pulsePayload });
                    }
                    else if (synapse.SignalType == SignalType.Transient)
                    {
                        synapse.TransientTriggerTick = CurrentTick + 1;
                    }
                }
            }
            
            foreach (var neuron in _neurons.Values)
            {
                if (!neuron.IsActive || neuron.LocalVariables[(int)LVarIndex.RefractoryTimeLeft] > 0) continue;
                float totalPotential = neuron.LocalVariables[(int)LVarIndex.DendriticPotential] + neuron.LocalVariables[(int)LVarIndex.SomaPotential];
                float effectiveThreshold = neuron.LocalVariables[(int)LVarIndex.FiringThreshold] + neuron.LocalVariables[(int)LVarIndex.AdaptiveThreshold];

                if (totalPotential >= effectiveThreshold)
                {
                    _eventQueue.Push(new Event { Id = (ulong)Interlocked.Increment(ref _nextEventId), ExecutionTick = CurrentTick + 1, Type = EventType.Activate, TargetId = neuron.Id, Payload = totalPotential });
                    
                    neuron.LocalVariables[(int)LVarIndex.SomaPotential] = 0.0f;
                    neuron.LocalVariables[(int)LVarIndex.RefractoryTimeLeft] = neuron.LocalVariables[(int)LVarIndex.RefractoryPeriod];
                    neuron.LocalVariables[(int)LVarIndex.AdaptiveThreshold] += neuron.LocalVariables[(int)LVarIndex.ThresholdAdaptationFactor];
                    neuron.LocalVariables[(int)LVarIndex.FiringRate] += (1.0f - Config.FiringRateMAWeight);
                }
            }
            
            // FINAL PHASES: Process events and update synapse temporal state.
            RebuildSpatialHash();
            _eventQueue.ProcessDueEvents(CurrentTick, ProcessEvent);

            foreach (var synapse in _synapses.Where(s => s.IsActive))
            {
                neuronSnapshots.TryGetValue(synapse.SourceId, out var currentSourceValue);
                synapse.PreviousSourceValue = currentSourceValue;
            }
        }

        /// <summary>
        /// Dispatches a single event from the event queue to the appropriate handler logic.
        /// </summary>
        private void ProcessEvent(Event e)
        {
            switch (e.Type)
            {
                case EventType.ExecuteGene:
                    _neurons.TryGetValue(e.TargetId, out Neuron? geneTarget);
                    if (e.Payload is uint geneId) { ExecuteGene(geneId, geneTarget, GetInitialContextForGene(geneId)); }
                    break;
                    
                case EventType.Activate:
                    if (_neurons.TryGetValue(e.TargetId, out Neuron? activationTarget) && activationTarget.IsActive)
                    {
                        if (e.Payload is float activationPotential) { ProcessNeuronActivation(activationTarget, activationPotential); }
                    }
                    break;

                case EventType.PotentialPulse:
                    if (e.Payload is not float potential) break;
                    
                    // A PotentialPulse can target either a neuron or an output node.
                    if (_neurons.TryGetValue(e.TargetId, out Neuron? pulseTargetNeuron) && pulseTargetNeuron.IsActive)
                    {
                        pulseTargetNeuron.LocalVariables[(int)LVarIndex.SomaPotential] += potential;
                    }
                    else if (_outputNodes.TryGetValue(e.TargetId, out OutputNode? pulseTargetOutput))
                    {
                        // Note: A delayed signal to an output node has no smoothing parameter available
                        // in this simplified event model, so it's a direct addition. For smoothed
                        // output, an Immediate signal is preferred.
                        pulseTargetOutput.Value += potential;
                    }
                    break;
            }
        }

        /// <summary>
        /// Handles the logic for a neuron's activation, including evaluating its internal brain
        /// and triggering its outgoing event-based synapses.
        /// </summary>
        private void ProcessNeuronActivation(Neuron neuron, float activationPotential)
        {
            // The default output value is the raw activation potential.
            float brainOutputValue = activationPotential;
            
            if (neuron.Brain != null)
            {
                // Step 1: Evaluate the brain to determine actions and the primary output value.
                var brainInputs = new float[neuron.Brain.InputMap.Count];
                for (int i = 0; i < brainInputs.Length; i++)
                {
                    brainInputs[i] = GetValueForBrainInput(neuron.Brain.InputMap[i], neuron, activationPotential);
                }
                neuron.Brain.Evaluate(brainInputs);

                var moveVector = Vector3.Zero;
                bool hasMoveOutput = false;

                foreach (var output in neuron.Brain.OutputMap)
                {
                    if (output.Value == 0 && output.ActionType != OutputActionType.SetOutputValue) continue;

                    switch (output.ActionType)
                    {
                        case OutputActionType.Move:
                            // This part remains illustrative; a robust implementation would use a clearer mapping.
                            moveVector.X = output.Value; 
                            hasMoveOutput = true;
                            break;
                        case OutputActionType.ExecuteGene:
                            var eventId = (ulong)Interlocked.Increment(ref _nextEventId);
                            _eventQueue.Push(new Event { Id = eventId, Type = EventType.ExecuteGene, TargetId = neuron.Id, ExecutionTick = CurrentTick + 1, Payload = (uint)Math.Abs(output.Value) });
                            break;
                        case OutputActionType.SetOutputValue:
                            brainOutputValue = output.Value;
                            break;
                    }
                }
                if (hasMoveOutput) { neuron.Position += moveVector; }
            }
            
            // Step 2: Trigger the neuron's outgoing event-based synapses using the final brain output.
            foreach (var synapse in neuron.OwnedSynapses.Where(s => s.SourceId == neuron.Id && s.IsActive))
            {
                float transmittedValue = CalculateTransmittedValue(brainOutputValue, synapse, true);

                switch (synapse.SignalType)
                {
                    case SignalType.Delayed:
                        var eventId = (ulong)Interlocked.Increment(ref _nextEventId);
                        _eventQueue.Push(new Event { Id = eventId, Type = EventType.PotentialPulse, TargetId = synapse.TargetId, ExecutionTick = CurrentTick + 1, Payload = transmittedValue });
                        break;
                    case SignalType.Persistent:
                        synapse.PersistentValue = transmittedValue;
                        synapse.IsPersistentValueSet = true;
                        break;
                    case SignalType.Transient:
                        synapse.TransientTriggerTick = CurrentTick + 1;
                        break;
                }
            }
        }

        /// <summary>
        /// Gets the appropriate value for a brain input based on its source type.
        /// This works with any IBrain implementation.
        /// </summary>
        private float GetValueForBrainInput(BrainInput brainInput, Neuron neuron, float activationPotential)
        {
            return brainInput.SourceType switch
            {
                InputSourceType.ActivationPotential => activationPotential,
                InputSourceType.CurrentPotential => neuron.LocalVariables[(int)LVarIndex.DendriticPotential] + neuron.LocalVariables[(int)LVarIndex.SomaPotential],
                InputSourceType.Health => neuron.LocalVariables[(int)LVarIndex.Health],
                InputSourceType.Age => neuron.LocalVariables[(int)LVarIndex.Age],
                InputSourceType.FiringRate => neuron.LocalVariables[(int)LVarIndex.FiringRate],
                InputSourceType.LocalVariable => (brainInput.SourceIndex >= 0 && brainInput.SourceIndex < neuron.LocalVariables.Length) ? neuron.LocalVariables[brainInput.SourceIndex] : 0f,
                InputSourceType.GlobalHormone => (brainInput.SourceIndex >= 0 && brainInput.SourceIndex < GlobalHormones.Length) ? GlobalHormones[brainInput.SourceIndex] : 0f,
                InputSourceType.ConstantOne => 1.0f,
                InputSourceType.ConstantZero => 0.0f,
                _ => 0f,
            };
        }
        
        /// <summary>
        /// Calculates the transmitted value across a synapse, applying its weight.
        /// The application of fatigue is conditional.
        /// </summary>
        private float CalculateTransmittedValue(float inputValue, Synapse synapse, bool applyFatigue)
        {
            float effectiveWeight = synapse.Weight * (1.0f - synapse.FatigueLevel);
            float value = inputValue * effectiveWeight;
            
            if (applyFatigue)
            {
                synapse.FatigueLevel += Math.Abs(value) * synapse.FatigueRate;
                if (synapse.FatigueLevel > 1.0f) synapse.FatigueLevel = 1.0f;
            }
            
            return value;
        }
    }
}
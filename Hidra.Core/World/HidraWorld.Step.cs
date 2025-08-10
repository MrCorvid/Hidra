// Hidra.Core/World/HidraWorld.Step.cs
namespace Hidra.Core;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Hidra.Core.Brain;
using Hidra.Core.Logging;

/// <summary>
/// This partial class contains the core simulation loop logic for the HidraWorld.
/// </summary>
public partial class HidraWorld
{
    /// <summary>
    /// Advances the simulation by a single time step (tick).
    /// </summary>
    /// <remarks>
    /// This is the main engine loop, executed in several distinct, deterministic phases:
    /// 1.  **Snapshot:** Capture the state of all neuron and input node outputs from the previous tick.
    /// 2.  **Decay &amp; Recovery:** Apply passive processes like potential decay, health tax, and adaptive threshold recovery.
    /// 3.  **Signal Processing:** Calculate and apply all continuous signals (Immediate, Persistent, Transient) using the snapshots.
    /// 4.  **Firing &amp; Event Queuing:** Neurons check their total potential against their threshold and queue `Activate` events for the next tick if they fire.
    /// 5.  **Event Processing:** Process all events scheduled for the current tick from the event queue.
    /// 6.  **Cleanup:** Deactivate neurons and remove synapses that were marked for removal during the tick.
    /// </remarks>
    public void Step()
    {
        CurrentTick++;
        Logger.Log("SIM_CORE", LogLevel.Debug, $"--- Tick {CurrentTick} Start ---");

        // PHASE 1: Take snapshots of all node states from the end of the previous tick.
        var inputSnapshots = _inputNodes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value);
        var neuronSnapshots = _neurons.Values.Where(n => n.IsActive)
                                    .ToDictionary(n => n.Id, n =>
                                        n.LocalVariables[(int)LVarIndex.SomaPotential] +
                                        n.LocalVariables[(int)LVarIndex.DendriticPotential]);

        // PHASE 2: Apply decay, recovery, and aging.
        foreach (var neuron in _neurons.Values)
        {
            if (!neuron.IsActive) continue;

            neuron.LocalVariables[(int)LVarIndex.SomaPotential] *= neuron.LocalVariables[(int)LVarIndex.DecayRate];
            neuron.LocalVariables[(int)LVarIndex.DendriticPotential] = 0f;
            neuron.LocalVariables[(int)LVarIndex.Age]++;
            neuron.LocalVariables[(int)LVarIndex.Health] -= Config.MetabolicTaxPerTick;

            if (neuron.LocalVariables[(int)LVarIndex.Health] <= 0)
            {
                MarkNeuronForDeactivation(neuron);
                continue;
            }

            if (neuron.LocalVariables[(int)LVarIndex.RefractoryTimeLeft] > 0)
                neuron.LocalVariables[(int)LVarIndex.RefractoryTimeLeft]--;

            neuron.LocalVariables[(int)LVarIndex.AdaptiveThreshold] *= (1.0f - neuron.LocalVariables[(int)LVarIndex.ThresholdRecoveryRate]);
            neuron.LocalVariables[(int)LVarIndex.FiringRate] *= Config.FiringRateMAWeight;
        }
        foreach (var synapse in _synapses)
        {
            if (synapse.IsActive)
                synapse.FatigueLevel *= (1.0f - synapse.FatigueRecoveryRate);
        }

        // PHASE 3: Single-Pass Universal Signal Processing.
        _scratchDendritic.Clear();

        foreach (var synapse in _synapses)
        {
            if (!synapse.IsActive)
            {
                _synapsesToRemove.Add(synapse);
                continue;
            }

            float sourceValue = 0f;
            if (_inputNodes.ContainsKey(synapse.SourceId))
                inputSnapshots.TryGetValue(synapse.SourceId, out sourceValue);
            else if (_neurons.ContainsKey(synapse.SourceId))
                neuronSnapshots.TryGetValue(synapse.SourceId, out sourceValue);

            _neurons.TryGetValue(synapse.TargetId, out var targetNeuron);
            var context = new ConditionContext(this, synapse, _neurons.GetValueOrDefault(synapse.SourceId), _inputNodes.GetValueOrDefault(synapse.SourceId), targetNeuron, sourceValue);
            bool passes = synapse.Condition == null || synapse.Condition.Evaluate(context);

            // Invariant: PreviousSourceValue must be updated here for correct temporal (t-1 -> t) comparisons on the next tick.
            synapse.PreviousSourceValue = sourceValue;

            if (synapse.SignalType == SignalType.Delayed || !passes) continue;

            float transmittedValue = 0f;
            switch (synapse.SignalType)
            {
                case SignalType.Immediate:
                    transmittedValue = CalculateTransmittedValue(sourceValue, synapse, true);
                    break;
                case SignalType.Persistent when synapse.IsPersistentValueSet:
                    transmittedValue = synapse.PersistentValue;
                    break;
                case SignalType.Transient when synapse.TransientTriggerTick == CurrentTick:
                    transmittedValue = CalculateTransmittedValue(1.0f, synapse, true);
                    break;
            }

            if (transmittedValue == 0f) continue;

            if (targetNeuron is { IsActive: true })
            {
                if (!_scratchDendritic.TryGetValue(targetNeuron.Id, out var acc))
                {
                    acc = new KahanAccumulator();
                    _scratchDendritic[targetNeuron.Id] = acc;
                }
                acc.Add(transmittedValue);
            }
            else if (_outputNodes.TryGetValue(synapse.TargetId, out var outputNode))
            {
                float smoothing = Math.Clamp(synapse.Parameter, 0.0f, 1.0f);
                outputNode.Value = (1f - smoothing) * outputNode.Value + smoothing * transmittedValue;
            }
        }

        foreach (var (neuronId, accumulator) in _scratchDendritic)
        {
            if (_neurons.TryGetValue(neuronId, out var neuron) && neuron.IsActive)
                neuron.LocalVariables[(int)LVarIndex.DendriticPotential] = accumulator.Sum;
        }

        // PHASE 4 & 5: Process event-based triggers (pulsed inputs and neuron firing).
        for (var i = 0; i < _synapses.Count; i++)
        {
            var synapse = _synapses[i];
            if (!synapse.IsActive || !_inputNodes.ContainsKey(synapse.SourceId)) continue;

            if (inputSnapshots.TryGetValue(synapse.SourceId, out var inputValue) && inputValue >= synapse.Parameter)
            {
                float pulsePayload = CalculateTransmittedValue(1.0f, synapse, true);
                var eventId = (ulong)Interlocked.Increment(ref _nextEventId);
                if (synapse.SignalType == SignalType.Delayed)
                {
                    _eventQueue.Push(new Event { Id = eventId, Type = EventType.PotentialPulse, TargetId = synapse.TargetId, ExecutionTick = CurrentTick + 1, Payload = pulsePayload });
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

        _eventQueue.ProcessDueEvents(CurrentTick, ProcessEvent);

        // FINAL PHASE: Cleanup and Deactivation
        bool anythingChanged = false;

        if (_synapsesToRemove.Count > 0)
        {
            anythingChanged = true;
            Logger.Log("SIM_CORE", LogLevel.Debug, $"Removing {_synapsesToRemove.Count} inactive synapses.");
            var synapsesToRemoveSet = new HashSet<Synapse>(_synapsesToRemove);
            _synapses.RemoveAll(s => synapsesToRemoveSet.Contains(s));
            foreach (var neuron in _neurons.Values)
                neuron.OwnedSynapses.RemoveAll(s => synapsesToRemoveSet.Contains(s));
            _synapsesToRemove.Clear();
        }

        if (_neuronsToDeactivate.Count > 0)
        {
            anythingChanged = true;
            foreach (var neuronToDeactivate in _neuronsToDeactivate)
            {
                if (neuronToDeactivate.IsActive)
                {
                    neuronToDeactivate.IsActive = false;
                    QueueApoptosisEventsFor(neuronToDeactivate);
                    Logger.Log("SIM_CORE", LogLevel.Debug, $"Neuron {neuronToDeactivate.Id} deactivated.");
                }
            }
            _neuronsToDeactivate.Clear();
        }

        if (anythingChanged)
        {
            RebuildSpatialHash();
        }
        Logger.Log("SIM_CORE", LogLevel.Debug, $"--- Tick {CurrentTick} End ---");
    }

    private void ProcessEvent(Event e)
    {
        lock (_eventHistoryLock)
        {
            if (!_eventHistory.ContainsKey(CurrentTick))
                _eventHistory[CurrentTick] = new List<Event>();
            _eventHistory[CurrentTick].Add(e);
        }

        switch (e.Type)
        {
            case EventType.ExecuteGene: // System-queued events (e.g., Gestation, Apoptosis)
                if (e.Payload is uint geneId)
                {
                    _neurons.TryGetValue(e.TargetId, out var geneTarget);
                    ExecuteGene(geneId, geneTarget, GetInitialContextForGene(geneId));
                }
                break;

            case EventType.ExecuteGeneFromBrain: // Neuron brain-initiated events
                if (e.Payload is uint internalGeneId && _neurons.TryGetValue(e.TargetId, out var geneTarget))
                {
                    ExecuteGene(internalGeneId, geneTarget, ExecutionContext.General);
                }
                break;

            case EventType.Activate:
                if (e.Payload is float activationPotential && _neurons.TryGetValue(e.TargetId, out var activationTarget) && activationTarget.IsActive)
                    ProcessNeuronActivation(activationTarget, activationPotential);
                break;

            case EventType.PotentialPulse:
                if (e.Payload is not float potential) break;
                
                if (_neurons.TryGetValue(e.TargetId, out var pulseTargetNeuron) && pulseTargetNeuron.IsActive)
                    pulseTargetNeuron.LocalVariables[(int)LVarIndex.SomaPotential] += potential;
                else if (_outputNodes.TryGetValue(e.TargetId, out var pulseTargetOutput))
                    pulseTargetOutput.Value += potential;
                break;
        }
    }

    /// <summary>
    /// Queues the Apoptosis gene to be executed on all downstream neurons connected via a deceased neuron's outgoing synapses.
    /// </summary>
    /// <param name="deceasedNeuron">The neuron that has just become inactive.</param>
    private void QueueApoptosisEventsFor(Neuron deceasedNeuron)
    {
        foreach (var synapse in deceasedNeuron.OwnedSynapses.Where(s => s.SourceId == deceasedNeuron.Id))
        {
            if (_neurons.ContainsKey(synapse.TargetId))
            {
                var eventId = (ulong)Interlocked.Increment(ref _nextEventId);
                _eventQueue.Push(new Event { Id = eventId, Type = EventType.ExecuteGene, TargetId = synapse.TargetId, ExecutionTick = CurrentTick + 1, Payload = SYS_GENE_APOPTOSIS });
            }
        }
    }

    private void ProcessNeuronActivation(Neuron neuron, float activationPotential)
    {
        float brainOutputValue = activationPotential;
        
        if (neuron.Brain != null)
        {
            var brainInputs = new float[neuron.Brain.InputMap.Count];
            for (var i = 0; i < brainInputs.Length; i++)
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
                        moveVector.X = output.Value; 
                        hasMoveOutput = true;
                        break;
                    case OutputActionType.ExecuteGene:
                        uint userGeneIndex = (uint)Math.Abs(output.Value);
                        uint internalGeneId = userGeneIndex + Config.SystemGeneCount;
                        var eventId = (ulong)Interlocked.Increment(ref _nextEventId);
                        _eventQueue.Push(new Event { Id = eventId, Type = EventType.ExecuteGeneFromBrain, TargetId = neuron.Id, ExecutionTick = CurrentTick + 1, Payload = internalGeneId });
                        break;
                    case OutputActionType.SetOutputValue:
                        brainOutputValue = output.Value;
                        break;
                }
            }
            if (hasMoveOutput) { neuron.Position += moveVector; }
        }
        
        foreach (var synapse in neuron.OwnedSynapses.Where(s => s.SourceId == neuron.Id && s.IsActive))
        {
            float transmittedValue = CalculateTransmittedValue(brainOutputValue, synapse, true);
            var eventId = (ulong)Interlocked.Increment(ref _nextEventId);

            switch (synapse.SignalType)
            {
                case SignalType.Delayed:
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

    private float GetValueForBrainInput(BrainInput brainInput, Neuron neuron, float activationPotential) =>
        brainInput.SourceType switch
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
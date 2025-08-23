// Hidra.Core/World/WorldDataTypes.cs
using Hidra.Core.Brain;
using Hidra.Core.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hidra.Core
{
    #region Brain Interface and Helpers

    /// <summary>
    /// Represents a request for a specific piece of information from the world,
    /// which a neuron's brain uses as an input for its calculations.
    /// </summary>
    public class BrainInput
    {
        /// <summary>
        /// The type of data source to read from (e.g., Health, Age, a global hormone).
        /// </summary>
        public InputSourceType SourceType { get; set; }
        
        /// <summary>
        /// An optional index used by certain source types, such as specifying which
        /// LocalVariable or GlobalHormone to read.
        /// </summary>
        public int SourceIndex { get; set; }
    }

    /// <summary>
    /// Represents a command or value produced by a neuron's brain after its evaluation.
    /// </summary>
    public class BrainOutput
    {
        /// <summary>
        /// The type of action the world should perform (e.g., Move, ExecuteGene).
        /// </summary>
        public OutputActionType ActionType { get; set; }
        
        /// <summary>
        /// The value associated with the action (e.g., the magnitude of movement, the ID of the gene to execute).
        /// </summary>
        public float Value { get; set; }
    }

    /// <summary>
    /// Defines the contract for a neuron's "brain," which encapsulates its core decision-making logic.
    /// This interface allows for different brain implementations (e.g., Neural Networks, State Machines)
    /// to be used interchangeably within the simulation.
    /// </summary>
    public interface IBrain
    {
        /// <summary>
        /// Gets a read-only map of the inputs the brain requires. The world uses this contract to
        /// prepare the correct input array for the <see cref="Evaluate"/> method.
        /// </summary>
        IReadOnlyList<BrainInput> InputMap { get; }

        /// <summary>
        /// Gets a read-only map of the outputs the brain produces. The world reads from this
        /// collection after evaluation to execute the brain's commands.
        /// </summary>
        IReadOnlyList<BrainOutput> OutputMap { get; }

        /// <summary>
        /// Gets a value indicating whether this brain's structure or parameters can be
        /// modified by genetic or learning mechanisms during the simulation.
        /// </summary>
        bool CanLearn { get; }
        
        /// <summary>
        /// Evaluates the brain's internal logic using a set of inputs provided by the world.
        /// The results are stored in the <see cref="OutputMap"/>.
        /// </summary>
        /// <param name="inputs">An array of input values, ordered to match the <see cref="InputMap"/>.</param>
        /// <param name="logAction">An optional delegate for routing log messages from the brain's execution.</param>
        void Evaluate(float[] inputs, Action<string, LogLevel, string>? logAction = null);

        /// <summary>
        /// Applies a mutation to the brain's internal structure or parameters, typically
        /// as part of an evolutionary algorithm.
        /// </summary>
        /// <param name="rate">A value indicating the magnitude or probability of the mutation.</param>
        void Mutate(float rate);

        /// <summary>
        /// Resets any internal state of the brain. This is crucial for brains that have
        /// memory or sequential logic (e.g., recurrent neural networks).
        /// </summary>
        void Reset();
        
        /// <summary>
        /// Provides the brain with a pseudo-random number generator to ensure deterministic behavior.
        /// </summary>
        /// <param name="prng">The PRNG instance to use for any internal stochastic processes.</param>
        void SetPrng(IPrng prng);

        /// <summary>
        /// Performs any necessary post-deserialization initialization.
        /// </summary>
        void InitializeFromLoad();
    }
    
    /// <summary>
    /// A default, non-functional brain that provides pass-through behavior for neurons
    /// that do not have a specific brain implementation. This avoids null checks.
    /// </summary>
    public class DummyBrain : IBrain
    {
        private readonly List<BrainInput> _inputMap;
        private readonly List<BrainOutput> _outputMap;

        public IReadOnlyList<BrainInput> InputMap => _inputMap;
        public IReadOnlyList<BrainOutput> OutputMap => _outputMap;
        public bool CanLearn => false;

        /// <summary>
        /// Initializes a new instance of the <see cref="DummyBrain"/> class.
        /// It configures one input to receive activation potential and one output to set a signal value.
        /// </summary>
        public DummyBrain()
        {
            _inputMap = new List<BrainInput> 
            { 
                new BrainInput { SourceType = InputSourceType.ActivationPotential } 
            };
            
            _outputMap = new List<BrainOutput> 
            { 
                new BrainOutput { ActionType = OutputActionType.SetOutputValue, Value = 0f } 
            };
        }
        
        /// <summary>
        /// Implements pass-through evaluation: takes the first input (assumed to be the activation potential)
        /// and writes it directly to the first output's value.
        /// </summary>
        public void Evaluate(float[] inputs, Action<string, LogLevel, string>? logAction = null) 
        {
            if (inputs != null && inputs.Length > 0)
            {
                _outputMap[0].Value = inputs[0];
            }
        }
        
        public void Mutate(float rate) { }
        public void Reset() { }
        public void SetPrng(IPrng prng) { }
        public void InitializeFromLoad() { }
    }

    #endregion
    
    #region Synapse Condition System

    /// <summary>
    /// Provides the necessary world state to a synapse's condition, allowing it to
    /// evaluate whether it should transmit a signal.
    /// </summary>
    public class ConditionContext
    {
        public HidraWorld World { get; }
        public Synapse Synapse { get; }
        public Neuron? SourceNeuron { get; }
        public InputNode? SourceInputNode { get; }
        public Neuron? TargetNeuron { get; }
        
        /// <summary>
        /// The potential value the source is attempting to transmit before the condition is evaluated.
        /// </summary>
        public float SourceValue { get; }

        public ConditionContext(HidraWorld world, Synapse synapse, Neuron? sourceNeuron, InputNode? sourceInputNode, Neuron? targetNeuron, float sourceValue)
        {
            World = world;
            Synapse = synapse;
            SourceNeuron = sourceNeuron;
            SourceInputNode = sourceInputNode;
            TargetNeuron = targetNeuron;
            SourceValue = sourceValue;
        }
    }

    /// <summary>
    /// Defines the contract for conditional logic that can be attached to a synapse,
    /// allowing for complex, state-dependent signal transmission.
    /// </summary>
    public interface ICondition
    {
        /// <summary>
        /// Evaluates the condition based on the provided world state.
        /// </summary>
        /// <param name="context">An object containing all relevant state for the evaluation.</param>
        /// <returns>True if the condition is met and the synapse can transmit; otherwise, false.</returns>
        bool Evaluate(ConditionContext context);
    }

    #endregion

    #region Core Simulation Data Structures

    /// <summary>
    /// Represents a single neuron, the fundamental computational unit in the HidraWorld.
    /// A neuron integrates incoming signals and, upon firing, executes its internal brain logic.
    /// </summary>
    public class Neuron
    {
        public ulong Id { get; set; }
        public bool IsActive { get; set; }
        public Vector3 Position { get; set; }
        public bool IsLearningEnabled { get; set; }
        public float[] LocalVariables { get; set; } = Array.Empty<float>();
        public IBrain Brain { get; set; } = new DummyBrain();
        public List<Synapse> OwnedSynapses { get; } = new();

        /// <summary>
        /// Calculates the neuron's total current potential by summing its dendritic and somatic potentials.
        /// </summary>
        /// <returns>The total integrated potential of the neuron.</returns>
        public float GetPotential()
        {
            return LocalVariables[(int)LVarIndex.DendriticPotential] + LocalVariables[(int)LVarIndex.SomaPotential];
        }
    }

    /// <summary>
    /// Represents a directed connection between two entities (e.g., Neuron to Neuron, InputNode to Neuron).
    /// It governs how signals are transmitted, including their strength, timing, and any conditional logic.
    /// </summary>
    public class Synapse
    {
        public ulong Id { get; set; }
        public bool IsActive { get; set; }
        public ulong SourceId { get; set; }
        public ulong TargetId { get; set; }
        public SignalType SignalType { get; set; } = SignalType.Delayed;
        public float Weight { get; set; } = 1.0f;
        public float Parameter { get; set; } = 1.0f;
        public float PersistentValue { get; set; }
        public bool IsPersistentValueSet { get; set; }
        public ulong TransientTriggerTick { get; set; }
        public float FatigueLevel { get; set; } = 0f;
        public float FatigueRate { get; set; } = 0.01f;
        public float FatigueRecoveryRate { get; set; } = 0.005f;
        public ICondition? Condition { get; set; }
        
        /// <summary>
        /// Stores the source's output value from the previous simulation tick.
        /// This is essential for temporal conditions that need to detect changes over time.
        /// </summary>
        [JsonProperty]
        public float PreviousSourceValue { get; set; }

        /// <summary>
        /// A counter used by temporal conditions to track how many consecutive ticks a
        /// state has been met (e.g., for a "Sustained" condition).
        /// </summary>
        public int SustainedCounter { get; set; }
    }

    /// <summary>
    /// Represents a global input source for the entire simulation (e.g., a sensor).
    /// </summary>
    public class InputNode
    {
        public ulong Id { get; set; }
        public float Value { get; set; }
    }

    /// <summary>
    /// Represents a global output sink for the entire simulation (e.g., a motor or actuator).
    /// </summary>
    public class OutputNode
    {
        public ulong Id { get; set; }
        public float Value { get; set; }
    }
    
    /// <summary>
    /// A strongly-typed container for data associated with a scheduled <see cref="Event"/>.
    /// </summary>
    /// <param name="GeneId">The ID of a gene to be executed.</param>
    /// <param name="PulseValue">The value of a potential pulse.</param>
    /// <param name="ActivationPotential">The activation potential for a brain evaluation.</param>
    public record EventPayload(
        uint? GeneId = null, 
        float? PulseValue = null, 
        float? ActivationPotential = null
    );
    
    /// <summary>
    /// Represents a scheduled action to be executed at a specific future simulation tick.
    /// </summary>
    public class Event
    {
        public ulong ExecutionTick { get; set; }
        public ulong Id { get; set; }
        public EventType Type { get; set; }
        public ulong TargetId { get; set; }
        public EventPayload Payload { get; set; } = new EventPayload();
    }

    #endregion

    #region Metrics Snapshot Data Structures

    /// <summary>
    /// A snapshot of a neuron's state at a specific tick for metrics collection.
    /// </summary>
    public record NeuronSnapshot(
        ulong Id, bool IsActive, Vector3 Position, float[] LVars
    );
    
    /// <summary>
    /// A snapshot of a synapse's state at a specific tick for metrics collection.
    /// </summary>
    public record SynapseSnapshot(
        ulong Id, bool IsActive, ulong SourceId, ulong TargetId, SignalType SignalType, float Weight, float Parameter, float FatigueLevel
    );
    
    /// <summary>
    /// A snapshot of the simulation's I/O node values at a specific tick.
    /// </summary>
    public record IOSnapshot(
        Dictionary<ulong, float> Inputs, Dictionary<ulong, float> Outputs
    );
    
    /// <summary>
    /// A summary of key simulation metrics for a single tick.
    /// </summary>
    public record TickMetrics(
        ulong Tick,
        int NeuronCount, int ActiveNeuronCount,
        int SynapseCount, int ActiveSynapseCount,
        float MeanFiringRate, float MeanHealth,
        float MeanSomaPotential, float MeanDendriticPotential
    );
    
    /// <summary>
    /// A comprehensive snapshot of the entire world's state at a single tick.
    /// </summary>
    public record WorldSnapshot(
        ulong Tick,
        IReadOnlyList<NeuronSnapshot> Neurons,
        IReadOnlyList<SynapseSnapshot>? Synapses,
        IOSnapshot? IO,
        TickMetrics Summary
    );

    #endregion

    #region Enumerations

    /// <summary>
    /// Defines the indices for well-known values within a Neuron's `LocalVariables` array.
    /// </summary>
    public enum LVarIndex
    {
        // User Writable Area (Indices 0 to 238)
        /// <summary>The base potential a neuron must reach to fire. Writable by HGL.</summary>
        FiringThreshold = 0,
        /// <summary>The rate at which a neuron's potential decays each tick. Writable by HGL.</summary>
        DecayRate = 1,
        /// <summary>The number of ticks a neuron must wait after firing. Writable by HGL.</summary>
        RefractoryPeriod = 2,
        /// <summary>The factor by which the firing threshold increases after firing. Writable by HGL.</summary>
        ThresholdAdaptationFactor = 3,
        /// <summary>The rate at which the adaptive threshold component recovers. Writable by HGL.</summary>
        ThresholdRecoveryRate = 4,
        /// <summary>The number of instructions a gene can execute. Writable by HGL.</summary>
        GeneExecutionFuel = 5,

        // System Read-Only Area (Indices 239 onwards)
        /// <summary>The remaining ticks in the neuron's refractory period. Read-only.</summary>
        RefractoryTimeLeft = 239, 
        /// <summary>The exponential moving average of the neuron's firing rate. Read-only.</summary>
        FiringRate = 240, 
        /// <summary>The potential accumulated from incoming synapses. Read-only.</summary>
        DendriticPotential = 241,
        /// <summary>The neuron's main integrated potential. Read-only.</summary>
        SomaPotential = 242, 
        /// <summary>The neuron's health, affecting its survival. Read-only.</summary>
        Health = 243, 
        /// <summary>The number of ticks since the neuron was created. Read-only.</summary>
        Age = 244, 
        /// <summary>The adaptive component of the firing threshold. Read-only.</summary>
        AdaptiveThreshold = 245
    }

    /// <summary>
    /// Defines the types of events that can be scheduled in the simulation's event queue.
    /// </summary>
    public enum EventType
    {
        Activate,
        ExecuteGene,
        ExecuteGeneFromBrain,
        PotentialPulse
    }
    
    /// <summary>
    /// Defines the security context under which a gene is executed, controlling its level of privilege.
    /// </summary>
    public enum ExecutionContext
    {
        System,
        Protected,
        General
    }

    /// <summary>
    /// Defines the behavior of a synapse's signal transmission over time.
    /// </summary>
    public enum SignalType
    {
        Immediate = 0,
        Delayed = 1,
        Persistent = 2,
        Transient = 3
    }
    
    /// <summary>
    /// Defines properties of a synapse that can be targeted for modification by genes.
    /// </summary>
    public enum SynapseProperty
    {
        Weight = 0,
        Parameter = 1,
        SignalType = 2,
        Condition = 3
    }

    /// <summary>
    /// Specifies the type of entity a synapse can target.
    /// </summary>
    public enum SynapseTargetType
    {
        Neuron = 0,
        OutputNode = 1,
        InputNode = 2
    }
    
    /// <summary>
    /// Defines actions a neuron's brain can command the world to perform.
    /// </summary>
    public enum OutputActionType
    {
        SetOutputValue,
        ExecuteGene,
        Move
    }

    /// <summary>
    /// Defines the sources of input that can be requested by a neuron's brain.
    /// </summary>
    public enum InputSourceType
    {
        ActivationPotential,
        CurrentPotential,
        Health,
        Age,
        LocalVariable,
        GlobalHormone,
        ConstantOne,
        ConstantZero,
        FiringRate
    }

    #endregion
}
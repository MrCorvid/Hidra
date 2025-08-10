// Hidra.Core/World/WorldDataTypes.cs
namespace Hidra.Core;

using System;
using System.Collections.Generic;
using System.Numerics;
using Hidra.Core.Brain;

/// <summary>
/// Represents a request for a specific piece of information from the world,
/// which a neuron's brain uses as an input for its calculations.
/// </summary>
public class BrainInput
{
    /// <summary>
    /// Gets or sets the type of data source to read from (e.g., Health, Age, a global hormone).
    /// </summary>
    public InputSourceType SourceType { get; set; }
    
    /// <summary>
    /// Gets or sets an optional index used by certain source types, such as specifying which
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
    /// Gets or sets the type of action the world should perform (e.g., Move, ExecuteGene).
    /// </summary>
    public OutputActionType ActionType { get; set; }
    
    /// <summary>
    /// Gets or sets the value associated with the action (e.g., the magnitude of movement, the ID of the gene to execute).
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
    void Evaluate(float[] inputs);

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
}

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

    /// <summary>
    /// Initializes a new instance of the <see cref="ConditionContext"/> class with all
    /// relevant state for a condition evaluation.
    /// </summary>
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
    /// <returns><see langword="true"/> if the condition is met and the synapse can transmit; otherwise, <see langword="false"/>.</returns>
    bool Evaluate(ConditionContext context);
}

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
    
    /// <summary>
    /// A dedicated block of 256 floating-point values that serves as the neuron's local memory.
    /// Standardized values (like Health, Age, FiringThreshold) are accessed via the private
    /// `LVarIndex` enum within `HidraWorld` to ensure consistency.
    /// </summary>
    public float[] LocalVariables { get; set; } = Array.Empty<float>();

    /// <summary>
    /// The internal "brain" that dictates the neuron's behavior when it activates (fires).
    /// This is an extensible point, allowing for various <see cref="IBrain"/> implementations.
    /// </summary>
    public IBrain? Brain { get; set; } = new NeuralNetworkBrain();

    /// <summary>
    /// A list of all synapses that have this neuron as either a source or a target.
    /// This provides an efficient way to find all connections for a given neuron.
    /// </summary>
    /// <remarks>
    /// This collection is not thread-safe. To prevent race conditions, modifications
    /// should be performed exclusively through the thread-safe methods in `HidraWorld`.
    /// </remarks>
    public List<Synapse> OwnedSynapses { get; } = new();

    /// <summary>
    /// Calculates the neuron's total current potential by summing its dendritic and somatic potentials.
    /// </summary>
    /// <returns>The total integrated potential of the neuron.</returns>
    /// <remarks>
    /// These constant indices reflect the private `LVarIndex` enum in `HidraWorld`,
    /// which centralizes the memory layout of a neuron's `LocalVariables` array. This tight
    /// coupling is a deliberate design choice to keep neuron data compact and performant.
    /// </remarks>
    public float GetPotential()
    {
        const int DendriticPotentialIndex = 241;
        const int SomaPotentialIndex = 242;
        return LocalVariables[DendriticPotentialIndex] + LocalVariables[SomaPotentialIndex];
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
    /// <summary>Gets or sets a multi-purpose parameter, often used for signal-specific settings like smoothing factors.</summary>
    public float Parameter { get; set; } = 1.0f;
    public float PersistentValue { get; set; }
    public bool IsPersistentValueSet { get; set; }
    public ulong TransientTriggerTick { get; set; }
    public float FatigueLevel { get; set; } = 0f;
    public float FatigueRate { get; set; } = 0.01f;
    public float FatigueRecoveryRate { get; set; } = 0.005f;

    /// <summary>
    /// An optional condition that must be met for this synapse to transmit its signal.
    /// If this property is null, the synapse is considered unconditional.
    /// </summary>
    public ICondition? Condition { get; set; }

    /// <summary>
    /// Stores the source's output value from the previous simulation tick.
    /// This is essential for temporal conditions that need to detect changes over time (e.g., rising/falling edge detection).
    /// </summary>
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
/// Represents a scheduled action to be executed at a specific future simulation tick.
/// </summary>
public class Event
{
    public ulong ExecutionTick { get; set; }
    public ulong Id { get; set; }
    public EventType Type { get; set; }
    public ulong TargetId { get; set; }
    public object? Payload { get; set; }
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
    /// <summary>The gene has full privileges and can modify core simulation state.</summary>
    System,
    /// <summary>The gene has restricted privileges for important but non-critical operations.</summary>
    Protected,
    /// <summary>The gene has standard, limited privileges for general-purpose tasks.</summary>
    General
}

/// <summary>
/// Defines the behavior of a synapse's signal transmission over time.
/// </summary>
public enum SignalType
{
    /// <summary>Transmits a value continuously based on the source's potential within the same tick.</summary>
    Immediate = 0,
    /// <summary>Creates a discrete potential pulse event for the target on the next tick when the source fires.</summary>
    Delayed = 1,
    /// <summary>Transmits a value when the source fires and holds that value until a new value is sent.</summary>
    Persistent = 2,
    /// <summary>Transmits a single, one-tick pulse on the next tick when the source fires.</summary>
    Transient = 3
}

/// <summary>
/// Defines properties of a synapse that can be targeted for modification by genes.
/// </summary>
public enum SynapseProperty
{
    Weight = 0,
    /// <summary>A generic parameter, replacing older, more specific properties like Threshold.</summary>
    Parameter = 1,
    SignalType = 2,
    /// <summary>A reference to an ICondition object, allowing genes to attach conditional logic.</summary>
    Condition = 3
}

/// <summary>
/// Defines actions a neuron's brain can command the world to perform.
/// </summary>
public enum OutputActionType
{
    Move,
    ExecuteGene,
    SetOutputValue
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
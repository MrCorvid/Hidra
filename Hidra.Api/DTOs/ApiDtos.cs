// Hidra.API/DTOs/ApiDtos.cs
using Hidra.Core;
using Hidra.Core.Brain;
using System.Collections.Generic;
using System;
using System.ComponentModel.DataAnnotations;

namespace Hidra.API.DTOs
{
    // === Simulation Lifecycle DTOs ===

    public class CreateExperimentRequestDto
    {
        public string Name { get; set; } = "unnamed-experiment";
        public HidraConfig Config { get; set; } = new();
        public required string HGLGenome { get; set; }
        public IOConfigDto IOConfig { get; set; } = new();
        public long? Seed { get; set; }
    }
    
    public class RestoreExperimentRequestDto
    {
        public string Name { get; set; } = "restored-experiment";
        public required string SnapshotJson { get; set; }
        public required string HGLGenome { get; set; }
        public required HidraConfig Config { get; set; }
        public required IOConfigDto IOConfig { get; set; }
    }

    public class CloneExperimentRequestDto
    {
        /// <summary>
        /// The name for the new, cloned experiment.
        /// </summary>
        public string Name { get; set; } = "cloned-experiment";

        /// <summary>
        /// The tick at which to clone the experiment. 
        /// History up to this tick is kept; history after is discarded.
        /// The world state will start exactly at this tick.
        /// </summary>
        public ulong Tick { get; set; }
    }
    
    public class CreateRunRequestDto
    {
        public required string Type { get; set; }
        public required RunParametersDto Parameters { get; set; }
        public Dictionary<ulong, float>? StagedInputs { get; set; }
        public Dictionary<int, float>? StagedHormones { get; set; }
    }

    public class RunParametersDto
    {
        public int? Ticks { get; set; }
        public int? MaxTicks { get; set; }
        public PredicateDto? Predicate { get; set; }
    }

    public class PredicateDto
    {
        public required string Type { get; set; }
        public ulong? OutputId { get; set; }
        public float? Value { get; set; }
    }

    public class SaveRequestDto
    {
        public string ExperimentName { get; set; } = "api_save";
    }

    // === I/O DTOs ===

    public class IOConfigDto
    {
        public List<ulong> InputNodeIds { get; set; } = new();
        public List<ulong> OutputNodeIds { get; set; } = new();
    }
    
    public class AddIoNodeRequestDto
    {
        public ulong Id { get; set; }
        public float InitialValue { get; set; } = 0f;
    }
    
    // === Graph Editing DTOs ===
    public class AtomicStepRequestDto
    {
        public Dictionary<ulong, float> Inputs { get; set; } = new();
        public List<ulong> OutputIdsToRead { get; set; } = new();
    }

    public class AtomicStepResponseDto
    {
        public ulong NewTick { get; set; }
        public IReadOnlyList<Event> EventsProcessed { get; set; } = Array.Empty<Event>();
        public Dictionary<ulong, float> OutputValues { get; set; } = new();
    }

    public class PatchLVarRequestDto
    {
        public Dictionary<int, float> LocalVariables { get; set; } = new();
    }
    
    public class CreateNeuronRequestDto
    {
        public required Vector3f Position { get; set; }
    }
    
    public class MitosisRequestDto
    {
        public required Vector3f Offset { get; set; }
    }
    
    public class CreateSynapseRequestDto
    {
        public ulong SourceId { get; set; }
        public ulong TargetId { get; set; }
        public SignalType SignalType { get; set; }
        public float Weight { get; set; }
        public float Parameter { get; set; }
    }
    
    public class ModifySynapseRequestDto
    {
        public float? Weight { get; set; }
        public float? Parameter { get; set; }
        public SignalType? SignalType { get; set; }
        public SynapseConditionDto? Condition { get; set; }
    }
    
    public class SynapseConditionDto
    {
        public required string Type { get; set; }
        public ConditionTarget? Target { get; set; }
        public int? Index { get; set; }
        public float? Value { get; set; }
        public ComparisonOperator? Op { get; set; }
        public TemporalOperator? TemporalOperator { get; set; }
        public float? Threshold { get; set; }
        public int? Duration { get; set; }
    }
    
    // === Brain Manipulation DTOs ===
    
    public class ConstructBrainRequestDto
    {
        public required string Type { get; set; }
        public int? NumInputs { get; set; }
        public int? NumOutputs { get; set; }
        public int? NumHiddenLayers { get; set; }
        public int? NodesPerLayer { get; set; }
        public int? NumCompetitors { get; set; }
    }

    public class SetBrainTypeRequestDto
    {
        public required string Type { get; set; }
        public LogicGateType? GateType { get; set; }
        public FlipFlopType? FlipFlop { get; set; }
        public float Threshold { get; set; } = 0.5f;
    }

    public class AddBrainNodeRequestDto
    {
        public NNNodeType NodeType { get; set; }
        public float Bias { get; set; }
    }
    
    public class AddBrainConnectionRequestDto
    {
        public int FromNodeId { get; set; }
        public int ToNodeId { get; set; }
        public float Weight { get; set; }
    }
    
    public class ConfigureBrainNodeRequestDto
    {
        public InputSourceType? InputSource { get; set; }
        public int? SourceIndex { get; set; }
        public OutputActionType? ActionType { get; set; }
        public ActivationFunctionType? ActivationFunction { get; set; }
        public float? Bias { get; set; }
    }
    
    public record Vector3f(float X, float Y, float Z);

    // === HGL Specification DTOs ===
    public class HglApiFunctionDto
    {
        public required string Name { get; set; }
        public required byte Opcode { get; set; }
        public required List<string> Parameters { get; set; }
    }

    public class HglSpecificationDto
    {
        public Dictionary<string, byte> Instructions { get; set; } = new();
        public Dictionary<string, byte> Operators { get; set; } = new();
        public List<HglApiFunctionDto> ApiFunctions { get; set; } = new();
        public Dictionary<int, string> SystemVariables { get; set; } = new();
    }

    // === Visualization & History DTOs ===

    /// <summary>
    /// Represents a single frame of a simulation replay, containing the
    /// world snapshot and all events processed at that tick.
    /// </summary>
    public class ReplayFrameDto
    {
        public ulong Tick { get; set; }
        public required VisualizationSnapshotDto Snapshot { get; set; }
        public required IReadOnlyList<Event> Events { get; set; }
    }

    /// <summary>
    /// A comprehensive snapshot of the world's state, designed for visualization and replay.
    /// It contains both the static structure (neurons, synapses, brains) and the dynamic state (values, health, etc.).
    /// </summary>
    public class VisualizationSnapshotDto
    {
        public required string ExperimentId { get; set; }
        public ulong CurrentTick { get; set; }

        // --- Static Structure ---
        public List<ulong> InputNodeIds { get; set; } = new();
        public List<ulong> OutputNodeIds { get; set; } = new();
        public List<VisualizationNeuronDto> Neurons { get; set; } = new();
        public List<VisualizationSynapseDto> Synapses { get; set; } = new();

        // --- Dynamic State ---
        public Dictionary<ulong, float> InputNodeValues { get; set; } = new();
        public Dictionary<ulong, float> OutputNodeValues { get; set; } = new();
        public Dictionary<ulong, VisualizationNeuronStateDto> NeuronStates { get; set; } = new();
        public Dictionary<ulong, VisualizationSynapseStateDto> SynapseStates { get; set; } = new();
    }

    /// <summary>
    /// Represents the static (structural) information of a neuron.
    /// </summary>
    public class VisualizationNeuronDto
    {
        public ulong Id { get; set; }
        public required Vector3f Position { get; set; }
        public required VisualizationBrainDto Brain { get; set; }
    }

    /// <summary>
    /// Represents the static (structural) information of a synapse.
    /// </summary>
    public class VisualizationSynapseDto
    {
        public ulong Id { get; set; }
        public ulong SourceId { get; set; }
        public ulong TargetId { get; set; }
        public SignalType SignalType { get; set; }
        public float Weight { get; set; }
        public float Parameter { get; set; }
        public float FatigueRate { get; set; }
        public float FatigueRecoveryRate { get; set; }
    }

    /// <summary>
    /// A polymorphic container for different brain types.
    /// </summary>
    public class VisualizationBrainDto
    {
        public required string Type { get; set; }
        public required object Data { get; set; }
    }

    /// <summary>
    /// Represents the internal structure of a NeuralNetworkBrain.
    /// </summary>
    public class NNBrainDataDto
    {
        public List<NNNodeDataDto> Nodes { get; set; } = new();
        public List<NNConnectionDataDto> Connections { get; set; } = new();
    }

    public class NNNodeDataDto
    {
        public int Id { get; set; }
        public NNNodeType NodeType { get; set; }
        public float Bias { get; set; }
        public ActivationFunctionType ActivationFunction { get; set; }
        public InputSourceType InputSource { get; set; }
        public int SourceIndex { get; set; }
        public OutputActionType ActionType { get; set; }
    }

    public class NNConnectionDataDto
    {
        public int FromNodeId { get; set; }
        public int ToNodeId { get; set; }
        public float Weight { get; set; }
    }

    /// <summary>
    /// Represents the configuration of a LogicGateBrain.
    /// </summary>
    public class LGBrainDataDto
    {
        public int GateType { get; set; } 
        public FlipFlopType? FlipFlop { get; set; }
        public float Threshold { get; set; }
    }

    /// <summary>
    /// Represents the dynamic (per-tick) state of a neuron.
    /// </summary>
    public class VisualizationNeuronStateDto
    {
        public bool IsActive { get; set; }
        public Dictionary<string, float> LocalVariables { get; set; } = new();
        public Dictionary<int, float>? BrainNodeValues { get; set; } // For visualizing NN node activations
    }

    /// <summary>
    /// Represents the dynamic (per-tick) state of a synapse.
    /// </summary>
    public class VisualizationSynapseStateDto
    {
        public bool IsActive { get; set; }
        public float FatigueLevel { get; set; }
    }

    /// <summary>
    /// A container used for persistence that bundles the world state 
    /// with the events processed during the tick that produced this state.
    /// </summary>
    public class PersistedTick
    {
        /// <summary>
        /// The raw JSON string of the HidraWorld's internal state.
        /// We store the raw string because the internal state (Private Fields) differs 
        /// from the public DTOs, and we need the internal state for server restarts.
        /// </summary>
        public required string WorldStateJson { get; set; }
        
        public required List<Event> Events { get; set; }
    }
}
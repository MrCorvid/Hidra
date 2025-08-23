// In Hidra.API/DTOs/ApiDtos.cs
using Hidra.Core;
using Hidra.Core.Brain;
using System.Collections.Generic;
using System;
using System.ComponentModel.DataAnnotations; // Added for 'required' keyword support in some frameworks

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

    // === HGL Specification DTOs (Unchanged) ===
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
}
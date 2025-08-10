// Hidra.Core/Genome/HGLOpcodes.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

/// <summary>
/// Defines the complete set of opcodes for the Hidra Genesis Language (HGL) bytecode.
/// </summary>
/// <remarks>
/// This static class serves as the single source of truth for the HGL virtual machine.
/// It uses a static constructor to dynamically assign byte values to each opcode based on the
/// order in the `MasterInstructionOrder` list. This design allows developers to add or reorder
/// instructions simply by editing the master list, with all opcode values and lookups
/// adjusting automatically at startup.
/// </remarks>
public static class HGLOpcodes
{
    public static readonly byte NOP, PUSH_BYTE, DUP, POP;
    public static readonly byte StoreLVar, LoadLVar, GetSelfId, GetPosition;
    public static readonly byte StoreGVar, LoadGVar;
    public static readonly byte CreateNeuron, Apoptosis, Mitosis, CallGene, SetSystemTarget;
    public static readonly byte GetNeighborCount, GetNearestNeighborId, GetNearestNeighborPosition;
    
    public static readonly byte AddSynapse, ModifySynapse;
    public static readonly byte SetSynapseSimpleProperty, SetSynapseCondition;

    public static readonly byte SetBrainType, ConfigureLogicGate;
    public static readonly byte ClearBrain, AddBrainNode, AddBrainConnection, RemoveBrainNode, RemoveBrainConnection;
    public static readonly byte ConfigureOutputNode, SetBrainInputSource, SetNodeActivationFunction;
    public static readonly byte SetBrainConnectionWeight, SetBrainNodeProperty;
    
    public static readonly byte SetRefractoryPeriod, SetThresholdAdaptation, GetFiringRate;

    public static readonly byte CreateBrain_SimpleFeedForward, CreateBrain_Competitive;
    public static readonly byte CreateBrain_SparseFeedForward, CreateBrain_Autoencoder, CreateBrain_RandomizedFeedForward;
    public static readonly byte ADD, SUB, MUL, DIV, MOD, EQ, NEQ, GT, LT, GTE, LTE;
    public static readonly byte JZ, JMP, JNZ, JNE;

    public static readonly int InstructionCount;
    public static readonly IReadOnlyDictionary<string, byte> OpcodeLookup;
    public static readonly IReadOnlyDictionary<string, byte> OperatorLookup;
    public static readonly IReadOnlyList<string> MasterInstructionOrder;
    public static readonly byte ApiOpcodeStart;
    public static readonly byte OperatorOpcodeStart;

    /// <summary>
    /// Initializes the HGLOpcodes class by dynamically assigning values and building lookup tables.
    /// </summary>
    static HGLOpcodes()
    {
        // This master list is the single source of truth. The order here defines the final opcode values.
        MasterInstructionOrder = new List<string>
        {
            // -- Core Execution --
            "NOP", "PUSH_BYTE", "DUP", "POP",
            // -- Core API --
            "API_StoreLVar", "API_LoadLVar", "API_GetSelfId", "API_GetPosition", "API_StoreGVar", "API_LoadGVar",
            "API_CreateNeuron", "API_Apoptosis", "API_Mitosis", "API_CallGene", "API_SetSystemTarget", "API_GetNeighborCount",
            "API_GetNearestNeighborId", "API_GetNearestNeighborPosition",
            
            "API_AddSynapse", "API_ModifySynapse", 
            "API_SetSynapseSimpleProperty", "API_SetSynapseCondition",
            
            "API_SetBrainType", "API_ConfigureLogicGate",

            "API_ClearBrain", "API_AddBrainNode", "API_AddBrainConnection",
            "API_RemoveBrainNode", "API_RemoveBrainConnection", "API_ConfigureOutputNode", "API_SetBrainInputSource",
            "API_SetNodeActivationFunction", "API_SetBrainConnectionWeight", "API_SetBrainNodeProperty",
            
            "API_SetRefractoryPeriod", "API_SetThresholdAdaptation", "API_GetFiringRate",
            
            "API_CreateBrain_SimpleFeedForward", "API_CreateBrain_Competitive", "API_CreateBrain_SparseFeedForward",
            "API_CreateBrain_Autoencoder", "API_CreateBrain_RandomizedFeedForward",
            // -- Operators --
            "ADD", "SUB", "MUL", "DIV", "MOD", "EQ", "NEQ", "GT", "LT", "GTE", "LTE",
            // -- Control Flow --
            "JZ", "JMP", "JNZ", "JNE"
        };
        
        InstructionCount = MasterInstructionOrder.Count;
        var tempOpcodeMap = new Dictionary<string, byte>(InstructionCount);

        for (byte i = 0; i < InstructionCount; i++)
        {
            var name = MasterInstructionOrder[i];
            tempOpcodeMap[name] = i;

            var fieldName = name.StartsWith("API_", StringComparison.Ordinal) ? name.Substring(4) : name;
            var fieldInfo = typeof(HGLOpcodes).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);

            if (fieldInfo == null)
            {
                throw new InvalidOperationException($"HGL Spec Mismatch: Instruction '{name}' is defined in MasterInstructionOrder but no corresponding public static readonly field named '{fieldName}' was found in HGLOpcodes.cs.");
            }
            
            fieldInfo.SetValue(null, i);
        }

        OpcodeLookup = new ReadOnlyDictionary<string, byte>(tempOpcodeMap);
        
        ApiOpcodeStart = OpcodeLookup["API_StoreLVar"]; 
        OperatorOpcodeStart = OpcodeLookup["ADD"];
        
        OperatorLookup = new ReadOnlyDictionary<string, byte>(new Dictionary<string, byte>
        {
            { "+", ADD }, { "-", SUB }, { "*", MUL }, { "/", DIV }, { "%", MOD },
            { "==", EQ }, { "!=", NEQ }, { ">", GT }, { "<", LT }, { ">=", GTE }, { "<=", LTE }
        });
    }
}
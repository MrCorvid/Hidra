// Hidra.Core/Genome/HGLOpcodes.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

/// <summary>
/// Defines the complete set of opcodes for the Hidra Genesis Language (HGL) bytecode.
/// </summary>
public static class HGLOpcodes
{
    #region Opcode Constants (Dynamically Initialized)

    public static readonly byte NOP, PUSH_BYTE, DUP, POP;
    public static readonly byte StoreLVar, LoadLVar, GetSelfId, GetPosition;
    public static readonly byte StoreGVar, LoadGVar;
    public static readonly byte CreateNeuron, Apoptosis, Mitosis, CallGene, SetSystemTarget;
    public static readonly byte GetNeighborCount, GetNearestNeighborId, GetNearestNeighborPosition;
    
    public static readonly byte AddSynapse, ModifySynapse, ClearSynapseCondition;
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

    #endregion

    #region Lookups and Metadata

    public static readonly int InstructionCount;
    public static readonly IReadOnlyDictionary<string, byte> OpcodeLookup;
    public static readonly IReadOnlyDictionary<string, byte> OperatorLookup;
    public static readonly IReadOnlyList<string> MasterInstructionOrder;
    
    internal static readonly byte ApiOpcodeStart;
    internal static readonly byte OperatorOpcodeStart;

    #endregion

    /// <summary>
    /// Initializes the HGLOpcodes class by dynamically assigning values and building lookup tables.
    /// </summary>
    static HGLOpcodes()
    {
        MasterInstructionOrder = new List<string>
        {
            // -- Core Execution --
            "API_NOP", "API_PUSH_BYTE", "API_DUP", "API_POP",
            
            // -- Core API --
            "API_StoreLVar", "API_LoadLVar", "API_GetSelfId", "API_GetPosition", "API_StoreGVar", "API_LoadGVar",
            "API_CreateNeuron", "API_Apoptosis", "API_Mitosis", "API_CallGene", "API_SetSystemTarget", 
            
            // -- Sensory API --
            "API_GetNeighborCount", "API_GetNearestNeighborId", "API_GetNearestNeighborPosition",
            
            // -- Synapse API --
            "API_AddSynapse", "API_ModifySynapse", "API_ClearSynapseCondition", 
            "API_SetSynapseSimpleProperty", "API_SetSynapseCondition",
            
            // -- Brain API --
            "API_SetBrainType", "API_ConfigureLogicGate",
            "API_ClearBrain", "API_AddBrainNode", "API_AddBrainConnection",
            "API_RemoveBrainNode", "API_RemoveBrainConnection", "API_ConfigureOutputNode", "API_SetBrainInputSource",
            "API_SetNodeActivationFunction", "API_SetBrainConnectionWeight", "API_SetBrainNodeProperty",
            
            // -- Neuron State API --
            "API_SetRefractoryPeriod", "API_SetThresholdAdaptation", "API_GetFiringRate",
            
            // -- High-Level Brain Constructors --
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
            string name = MasterInstructionOrder[i];
            tempOpcodeMap[name] = i;

            string fieldName = name.StartsWith("API_") ? name.Substring(4) : name;
            var fieldInfo = typeof(HGLOpcodes).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);

            if (fieldInfo != null)
            {
                fieldInfo.SetValue(null, i);
                
                // Add backward compatibility: also map the field name (without API_) to the same opcode
                if (name.StartsWith("API_"))
                {
                    tempOpcodeMap[fieldName] = i;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[HGLOpcodes] Spec Mismatch: Instruction '{name}' has no public static field '{fieldName}'.");
            }
        }

        OpcodeLookup = new ReadOnlyDictionary<string, byte>(tempOpcodeMap);

        ApiOpcodeStart = OpcodeLookup["API_NOP"];
        OperatorOpcodeStart = OpcodeLookup["ADD"];

        OperatorLookup = new ReadOnlyDictionary<string, byte>(new Dictionary<string, byte>
        {
            { "+", ADD }, { "-", SUB }, { "*", MUL }, { "/", DIV }, { "%", MOD },
            { "==", EQ }, { "!=", NEQ }, { ">", GT }, { "<", LT }, { ">=", GTE }, { "<=", LTE }
        });
    }
}
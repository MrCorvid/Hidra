// Hidra.Core/Genome/Bridge/HidraSprakBridge.SynapseAPI.cs
using ProgrammingLanguageNr1;
using System;
using System.Linq;

namespace Hidra.Core
{
    public partial class HidraSprakBridge
    {
        #region Synapse API

        private enum ConditionType { LVar = 0, GVar = 1, Temporal = 2 }
        private static readonly int _conditionTypeCount = Enum.GetValues(typeof(ConditionType)).Length;
        private static readonly int _comparisonOperatorCount = Enum.GetValues(typeof(ComparisonOperator)).Length;
        private static readonly int _temporalOperatorCount = Enum.GetValues(typeof(TemporalOperator)).Length;
        private static readonly int _synapsePropertyCount = Enum.GetValues(typeof(SynapseProperty)).Length;

        [SprakAPI(
    "Creates a synapse. TargetType: 0=Neuron, 1=Output, 2=Input. " +
    "For Neuron/Output, source is the current neuron. For Input, source is the input and target is the current neuron. " +
    "A Neuron target_id of 0 means 'self'. A specific ID will be used if it exists, otherwise a modulus is applied to other neurons.",
    "target_type (0=Neuron, 1=Output, 2=Input)", "target_id", "signal_type", "weight", "parameter")]
        public float API_AddSynapse(float targetType, float targetId, float signalType, float weight, float parameter)
        {
            LogDbg("BRIDGE.SYN", $"API_AddSynapse(tt={targetType}, id={targetId}, sig={signalType}, w={weight}, p={parameter})");

            var self = GetTargetNeuron();
            if (self == null) 
            {
                LogWarn("BRIDGE.SYN", "No target neuron; cannot create synapse.");
                return 0f; 
            }

            if (!Enum.IsDefined(typeof(SynapseTargetType), (int)targetType) || !Enum.IsDefined(typeof(SignalType), (int)signalType))
            {
                LogWarn("BRIDGE.SYN", "Invalid SynapseTargetType or SignalType; no-op.");
                return 0f; 
            }
            
            var resolvedTargetType = (SynapseTargetType)targetType;
            var resolvedSignalType = (SignalType)signalType;
            ulong sourceId = 0, finalTargetId = 0;

            switch (resolvedTargetType)
            {
                case SynapseTargetType.Neuron:
                    sourceId = self.Id;
                    
                    if ((ulong)targetId == 0)
                    {
                        // Case 1: Target ID 0 is an explicit keyword for self-connection.
                        finalTargetId = self.Id;
                    }
                    else if (_world.GetNeuronById((ulong)targetId) != null)
                    {
                        // Case 2: The specified neuron ID exists. Use it directly.
                        finalTargetId = (ulong)targetId;
                    }
                    else
                    {
                        // Case 3 (Fallback): The ID doesn't exist. Apply modulus to OTHER neurons to prevent accidental self-loops.
                        var otherNeuronIds = _world.GetNeuronIdsSnapshot().Where(id => id != self.Id).ToList();
                        if (otherNeuronIds.Count == 0) 
                        {
                            LogWarn("BRIDGE.SYN", "No other neurons exist to target; falling back to self-connection as last resort.");
                            finalTargetId = self.Id; // Fallback to self only if no other options exist.
                        }
                        else
                        {
                            finalTargetId = otherNeuronIds[(int)(Math.Abs((long)targetId) % otherNeuronIds.Count)];
                        }
                    }
                    break;

                case SynapseTargetType.OutputNode:
                    sourceId = self.Id;
                    var outputIds = _world.GetOutputIdsSnapshot();
                    if (outputIds.Count == 0) 
                    {
                        LogWarn("BRIDGE.SYN", "No outputs exist; cannot create neuron->output synapse.");
                        return 0f; 
                    }

                    // Apply direct-addressing logic for outputs.
                    if (_world.GetOutputNodeById((ulong)targetId) != null)
                    {
                        finalTargetId = (ulong)targetId;
                    }
                    else
                    {
                        // Fallback to modulus if the specific ID doesn't exist.
                        finalTargetId = outputIds[(int)(Math.Abs((long)targetId) % outputIds.Count)];
                    }
                    break;

                case SynapseTargetType.InputNode:
                    finalTargetId = self.Id; // Target is always the current neuron for this type.
                    var inputIds = _world.GetInputIdsSnapshot();
                    if (inputIds.Count == 0) 
                    {
                        LogWarn("BRIDGE.SYN", "No inputs exist; cannot create input->neuron synapse.");
                        return 0f;
                    }

                    // Apply direct-addressing logic for selecting the source input node.
                    if (_world.GetInputNodeById((ulong)targetId) != null)
                    {
                        sourceId = (ulong)targetId;
                    }
                    else
                    {
                        // Fallback to modulus if the specific ID doesn't exist.
                        sourceId = inputIds[(int)(Math.Abs((long)targetId) % inputIds.Count)];
                    }
                    break;

                default:
                    LogWarn("BRIDGE.SYN", $"Unhandled target type {resolvedTargetType}; no-op.");
                    return 0f;
            }

            var newSynapse = _world.AddSynapse(sourceId, finalTargetId, resolvedSignalType, weight, parameter);
            return (float)(newSynapse?.Id ?? 0);
        }

        [SprakAPI("Modifies the core properties of a synapse (weight, parameter, signal type), leaving its condition unchanged.", "local_index", "new_weight", "new_param", "new_sig_type")]
        public void API_ModifySynapse(float localIndex, float newWeight, float newParameter, float newSignalType)
        {
            LogDbg("BRIDGE.SYN", $"API_ModifySynapse(idx={localIndex}, w={newWeight}, p={newParameter}, sig={newSignalType})");
            lock (_world)
            {
                var synapse = GetSynapseByLocalIndex("API_ModifySynapse", localIndex);
                if (synapse == null) return;

                synapse.Weight = newWeight;
                synapse.Parameter = newParameter;
                
                int typeValue = (int)newSignalType;
                if (Enum.IsDefined(typeof(SignalType), typeValue)) { synapse.SignalType = (SignalType)typeValue; }
            }
        }

        [SprakAPI("Removes any condition from a synapse, making it unconditional.", "local_index")]
        public void API_ClearSynapseCondition(float localIndex)
        {
            LogDbg("BRIDGE.SYN", $"API_ClearSynapseCondition(idx={localIndex})");
            lock (_world)
            {
                var synapse = GetSynapseByLocalIndex("API_ClearSynapseCondition", localIndex);
                if (synapse != null) { synapse.Condition = null; }
            }
        }
        
        [SprakAPI(
            "Sets a condition on a synapse. Type: 0=LVar, 1=GVar, 2=Temporal. For LVar, target is Source if available, else Target.",
            "local_index",
            "condition_type (0=LVar, 1=GVar, 2=Temporal)",
            "p1 (LVar:lvar_idx, GVar:gvar_idx, Temporal:temp_op)",
            "p2 (LVar:comp_op, GVar:comp_op, Temporal:threshold)",
            "p3 (LVar:value, GVar:value, Temporal:duration_ticks)")]
        public void API_SetSynapseCondition(float localIndex, float conditionType, float p1, float p2, float p3)
        {
            var resolvedType = (ConditionType)((int)Math.Abs(conditionType) % _conditionTypeCount);
            LogDbg("BRIDGE.SYN", $"API_SetSynapseCondition(idx={localIndex}, type={conditionType}->{resolvedType}, p1={p1}, p2={p2}, p3={p3})");

            lock (_world)
            {
                var synapse = GetSynapseByLocalIndex("API_SetSynapseCondition", localIndex);
                if (synapse == null) return;

                ICondition? newCondition = null;

                switch (resolvedType)
                {
                    case ConditionType.LVar:
                    {
                        var resolvedCompOp = (ComparisonOperator)((int)Math.Abs(p2) % _comparisonOperatorCount);
                        bool isSourceNeuron = _world.GetNeuronById(synapse.SourceId) != null;
                        ConditionTarget finalTarget = isSourceNeuron ? ConditionTarget.Source : ConditionTarget.Target;
                        
                        LogDbg("BRIDGE.SYN", $"LVar condition source is {(isSourceNeuron ? "Neuron" : "InputNode")}. Implicitly setting target to {finalTarget}. Operator {p2}->{resolvedCompOp}.");

                        newCondition = new LVarCondition
                        {
                            LVarIndex = (int)p1,
                            Operator = resolvedCompOp,
                            Value = p3,
                            Target = finalTarget
                        };
                        break;
                    }
                    case ConditionType.GVar:
                    {
                        var resolvedCompOp = (ComparisonOperator)((int)Math.Abs(p2) % _comparisonOperatorCount);
                        LogDbg("BRIDGE.SYN", $"GVar condition operator {p2}->{resolvedCompOp}.");
                        
                        newCondition = new GVarCondition
                        {
                            GVarIndex = (int)p1,
                            Operator = resolvedCompOp,
                            Value = p3
                        };
                        break;
                    }
                    case ConditionType.Temporal:
                    {
                        var resolvedTempOp = (TemporalOperator)((int)Math.Abs(p1) % _temporalOperatorCount);
                        LogDbg("BRIDGE.SYN", $"Temporal condition operator {p1}->{resolvedTempOp}.");

                        newCondition = new TemporalCondition
                        {
                            Operator = resolvedTempOp,
                            Threshold = p2,
                            Duration = (int)p3
                        };
                        break;
                    }
                }
                
                synapse.Condition = newCondition;
                LogDbg("BRIDGE.SYN", $"Synapse[{synapse.Id}] condition set to {resolvedType}.");
            }
        }
        
        [SprakAPI("Modifies a simple property of a synapse.", "local_index", "property_id (0=Weight, 1=Param, 2=SignalType)", "value")]
        public void API_SetSynapseSimpleProperty(float localIndex, float propertyId, float value)
        {
            LogDbg("BRIDGE.SYN", $"API_SetSynapseSimpleProperty(idx={localIndex}, propId={propertyId}, val={value})");
            var synapse = GetSynapseByLocalIndex("API_SetSynapseSimpleProperty", localIndex);
            if (synapse == null) return;
            
            var prop = (SynapseProperty)((int)Math.Abs(propertyId) % _synapsePropertyCount);

            switch (prop)
            {
                case SynapseProperty.Weight:
                    synapse.Weight = value;
                    break;
                case SynapseProperty.Parameter:
                    synapse.Parameter = value;
                    break;
                case SynapseProperty.SignalType:
                    var signalTypeCount = Enum.GetValues(typeof(SignalType)).Length;
                    var newType = (SignalType)((int)Math.Abs(value) % signalTypeCount);
                    synapse.SignalType = newType;
                    break;
                case SynapseProperty.Condition:
                    LogWarn("BRIDGE.SYN", "API_SetSynapseSimpleProperty cannot set Condition. Use API_SetSynapseCondition instead.");
                    break;
            }
        }

        /// <summary>Finds a synapse owned by the target neuron via its local index.</summary>
        private Synapse? GetSynapseByLocalIndex(string apiName, float localIndex)
        {
            var neuron = GetTargetNeuron();
            if (neuron == null || !neuron.OwnedSynapses.Any())
            {
                LogWarn("BRIDGE.SYN", $"{apiName}: No target neuron or it has no synapses.");
                return null;
            }

            int index = (int)Math.Abs(localIndex) % neuron.OwnedSynapses.Count;
            return neuron.OwnedSynapses[index];
        }

        #endregion
    }
}
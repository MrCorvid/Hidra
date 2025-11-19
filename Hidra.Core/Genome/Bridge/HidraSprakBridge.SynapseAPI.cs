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
            LogTrace("BRIDGE.SYN", $"API_AddSynapse(tt={targetType}, id={targetId}, sig={signalType}, w={weight}, p={parameter})");

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
            long longTargetId = (long)targetId; // Use long for modulus calculation

            switch (resolvedTargetType)
            {
                case SynapseTargetType.Neuron:
                    sourceId = self.Id;
                    
                    if (longTargetId == 0)
                    {
                        finalTargetId = self.Id;
                    }
                    else if (_world.GetNeuronById((ulong)longTargetId) != null)
                    {
                        finalTargetId = (ulong)longTargetId;
                    }
                    else
                    {
                        var otherNeuronIds = _world.GetNeuronIdsSnapshot().Where(id => id != self.Id).ToList();
                        if (otherNeuronIds.Count == 0) 
                        {
                            LogWarn("BRIDGE.SYN", "No other neurons exist to target; falling back to self-connection as last resort.");
                            finalTargetId = self.Id;
                        }
                        else
                        {
                            int count = otherNeuronIds.Count;
                            finalTargetId = otherNeuronIds[(int)((longTargetId % count + count) % count)];
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

                    if (_world.GetOutputNodeById((ulong)longTargetId) != null)
                    {
                        finalTargetId = (ulong)longTargetId;
                    }
                    else
                    {
                        int count = outputIds.Count;
                        finalTargetId = outputIds[(int)((longTargetId % count + count) % count)];
                    }
                    break;

                case SynapseTargetType.InputNode:
                    finalTargetId = self.Id;
                    var inputIds = _world.GetInputIdsSnapshot();
                    if (inputIds.Count == 0) 
                    {
                        LogWarn("BRIDGE.SYN", "No inputs exist; cannot create input->neuron synapse.");
                        return 0f; 
                    }

                    if (_world.GetInputNodeById((ulong)longTargetId) != null)
                    {
                        sourceId = (ulong)longTargetId;
                    }
                    else
                    {
                        int count = inputIds.Count;
                        sourceId = inputIds[(int)((longTargetId % count + count) % count)];
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
            LogTrace("BRIDGE.SYN", $"API_ModifySynapse(idx={localIndex}, w={newWeight}, p={newParameter}, sig={newSignalType})");
            lock (_world.SyncRoot)
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
            LogTrace("BRIDGE.SYN", $"API_ClearSynapseCondition(idx={localIndex})");
            lock (_world.SyncRoot)
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
            LogTrace("BRIDGE.SYN", $"API_SetSynapseCondition(idx={localIndex}, type={conditionType}, p1={p1}, p2={p2}, p3={p3})");
            
            ConditionType resolvedType;
            int intConditionType = (int)conditionType;

            // 1. Literal Enum Check
            if (Enum.IsDefined(typeof(ConditionType), intConditionType))
            {
                resolvedType = (ConditionType)intConditionType;
            }
            else // 2. Modulus Fallback
            {
                resolvedType = (ConditionType)(((intConditionType % _conditionTypeCount) + _conditionTypeCount) % _conditionTypeCount);
                LogTrace("BRIDGE.SYN", $"Literal condition_type {intConditionType} invalid; fell back to {resolvedType}.");
            }
            
            lock (_world.SyncRoot)
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
                        
                        newCondition = new LVarCondition
                        {
                            LVarIndex = (int)p1,
                            Operator = resolvedCompOp,
                            Value = p3,
                            Target = isSourceNeuron ? ConditionTarget.Source : ConditionTarget.Target
                        };
                        break;
                    }
                    case ConditionType.GVar:
                    {
                        var resolvedCompOp = (ComparisonOperator)((int)Math.Abs(p2) % _comparisonOperatorCount);
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
            }
        }
        
        [SprakAPI("Modifies a simple property of a synapse.", "local_index", "property_id (0=Weight, 1=Param, 2=SignalType)", "value")]
        public void API_SetSynapseSimpleProperty(float localIndex, float propertyId, float value)
        {
            LogTrace("BRIDGE.SYN", $"API_SetSynapseSimpleProperty(idx={localIndex}, propId={propertyId}, val={value})");
            var synapse = GetSynapseByLocalIndex("API_SetSynapseSimpleProperty", localIndex);
            if (synapse == null) return;
            
            SynapseProperty prop;
            int intPropertyId = (int)propertyId;

            // 1. Literal Enum Check
            if (Enum.IsDefined(typeof(SynapseProperty), intPropertyId))
            {
                prop = (SynapseProperty)intPropertyId;
            }
            else // 2. Modulus Fallback
            {
                prop = (SynapseProperty)(((intPropertyId % _synapsePropertyCount) + _synapsePropertyCount) % _synapsePropertyCount);
                LogTrace("BRIDGE.SYN", $"Literal property_id {intPropertyId} invalid; fell back to {prop}.");
            }

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
                    var newType = (SignalType)(((int)value % signalTypeCount + signalTypeCount) % signalTypeCount);
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

            int idx = (int)localIndex;
            int count = neuron.OwnedSynapses.Count;

            // 1. Literal ID (Index) Check
            if (idx >= 0 && idx < count)
            {
                LogTrace("BRIDGE.SYN", $"{apiName}: Found synapse via literal local index {idx}.");
                return neuron.OwnedSynapses[idx];
            }
            else // 2. Relative Modulus Fallback
            {
                // This robust modulus handles potential negative inputs correctly.
                int safeIndex = (idx % count + count) % count;
                LogTrace("BRIDGE.SYN", $"{apiName}: Literal index {idx} invalid. Used modulus fallback to get synapse at index {safeIndex}.");
                return neuron.OwnedSynapses[safeIndex];
            }
        }

        #endregion
    }
}
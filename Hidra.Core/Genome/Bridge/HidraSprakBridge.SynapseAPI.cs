// Hidra.Core/Genome/Bridge/HidraSprakBridge.SynapseAPI.cs
using ProgrammingLanguageNr1;
using System;

namespace Hidra.Core
{
    public partial class HidraSprakBridge
    {
        #region Synapse API

        [SprakAPI("Creates a synapse between two entities.", "source_id", "target_id", "signal_type (0=Imm, 1=Del, 2=Per, 3=Tra)", "weight", "parameter")]
        public float API_AddSynapse(float sourceId, float targetId, float signalType, float weight, float parameter)
        {
            var type = (SignalType)(int)signalType;
            if (!Enum.IsDefined(type)) return 0f;

            var synapse = _world.AddSynapse((ulong)sourceId, (ulong)targetId, type, weight, parameter);
            return (float)(synapse?.Id ?? 0);
        }

        [SprakAPI("Modifies the core properties of a synapse, optionally setting a simple LVar condition.", "local_index", "new_weight", "new_param", "new_sig_type", "cond_lvar_idx (-1 to disable)", "cond_op", "cond_val", "cond_target (0=Src, 1=Tgt)")]
        public void API_ModifySynapse(float localIndex, float newWeight, float newParameter, float newSignalType, float conditionLVarIndex, float conditionOp, float conditionValue, float conditionTarget)
        {
            var neuron = GetTargetNeuron();
            if (neuron == null || neuron.OwnedSynapses.Count == 0) return;

            int index = (int)Math.Abs(localIndex) % neuron.OwnedSynapses.Count;
            var synapse = neuron.OwnedSynapses[index];

            synapse.Weight = newWeight;
            synapse.Parameter = newParameter;
            var type = (SignalType)(int)newSignalType;
            if (Enum.IsDefined(type)) { synapse.SignalType = type; }

            if (conditionLVarIndex >= 0)
            {
                var op = (ComparisonOperator)(int)conditionOp;
                var target = (ConditionTarget)(int)conditionTarget;
                if (Enum.IsDefined(op) && Enum.IsDefined(target))
                {
                    synapse.Condition = new LVarCondition
                    {
                        Target = target,
                        LVarIndex = (int)conditionLVarIndex,
                        Operator = op,
                        Value = conditionValue
                    };
                }
            }
            else
            {
                synapse.Condition = null;
            }
        }

        [SprakAPI("Modifies a simple property of a synapse.", "local_index", "property_id (0=Weight, 1=Param, 2=SignalType)", "value")]
        public void API_SetSynapseSimpleProperty(float localIndex, float propertyId, float value)
        {
            var neuron = GetTargetNeuron();
            if (neuron == null || neuron.OwnedSynapses.Count == 0) return;

            int index = (int)Math.Abs(localIndex) % neuron.OwnedSynapses.Count;
            var synapse = neuron.OwnedSynapses[index];

            var prop = (SynapseProperty)(int)propertyId;
            switch (prop)
            {
                case SynapseProperty.Weight:
                    synapse.Weight = value;
                    break;
                case SynapseProperty.Parameter:
                    synapse.Parameter = value;
                    break;
                case SynapseProperty.SignalType:
                    var newType = (SignalType)(int)value;
                    if (Enum.IsDefined(newType)) { synapse.SignalType = newType; }
                    break;
            }
        }

        [SprakAPI("Sets or clears the condition for a synapse.", "local_index", "cond_type (0=Clear, 1=LVar...)", "p1", "p2", "p3", "value (for LVar/GVar)")]
        public void API_SetSynapseCondition(float localIndex, float conditionType, float p1, float p2, float p3, float value)
        {
            var neuron = GetTargetNeuron();
            if (neuron == null || neuron.OwnedSynapses.Count == 0) return;

            int index = (int)Math.Abs(localIndex) % neuron.OwnedSynapses.Count;
            var synapse = neuron.OwnedSynapses[index];

            switch ((int)conditionType)
            {
                case 0: // Clear condition
                    synapse.Condition = null;
                    break;
                case 1: // LVar Condition: p1=target, p2=lvar_idx, p3=op, value=val
                    var lvar_target = (ConditionTarget)(int)p1;
                    var lvar_op = (ComparisonOperator)(int)p3;
                    synapse.Condition = new LVarCondition { Target = lvar_target, LVarIndex = (int)p2, Operator = lvar_op, Value = value };
                    break;
                case 2: // GVar Condition: p1=gvar_idx, p2=op, value=val
                    var gvar_op = (ComparisonOperator)(int)p2;
                    synapse.Condition = new GVarCondition { GVarIndex = (int)p1, Operator = gvar_op, Value = value };
                    break;
                case 3: // Temporal Condition: p1=op, p2=threshold, p3=duration
                    var temporal_op = (TemporalOperator)(int)p1;
                    synapse.Condition = new TemporalCondition { Operator = temporal_op, Threshold = p2, Duration = (int)p3 };
                    break;
                case 4: // Relational Condition: p1=op
                    var relational_op = (ComparisonOperator)(int)p1;
                    synapse.Condition = new RelationalCondition { Operator = relational_op };
                    break;
            }
        }

        // The GetNextStackValue() helper method has been removed.

        #endregion
    }
}
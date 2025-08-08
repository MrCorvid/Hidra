// Hidra.Core/World/SynapseConditions.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hidra.Core
{
    #region Condition Enums

    /// <summary>
    /// Defines the type of comparison to be performed in a condition.
    /// </summary>
    public enum ComparisonOperator
    {
        GreaterThan,
        LessThan,
        EqualTo,
        NotEqualTo,
        GreaterThanOrEqualTo,
        LessThanOrEqualTo
    }

    /// <summary>
    /// Defines the type of temporal operation to be performed in a condition.
    /// </summary>
    public enum TemporalOperator
    {
        RisingEdge,  // Triggers when the source value crosses a threshold from below.
        FallingEdge, // Triggers when the source value crosses a threshold from above.
        Changed,     // Triggers when the source value changes by more than a threshold.
        Sustained    // Triggers when the source value remains above a threshold for N ticks.
    }

    /// <summary>
    /// Defines the target of a state-based condition (e.g., the source or target neuron).
    /// </summary>
    public enum ConditionTarget
    {
        Source,
        Target
    }

    #endregion

    /// <summary>
    /// A condition that checks a Local Variable (LVar) on the source or target neuron.
    /// </summary>
    public class LVarCondition : ICondition
    {
        public ConditionTarget Target { get; set; }
        public int LVarIndex { get; set; }
        public ComparisonOperator Operator { get; set; }
        public float Value { get; set; }

        public bool Evaluate(ConditionContext context)
        {
            var neuron = Target == ConditionTarget.Source ? context.SourceNeuron : context.TargetNeuron;
            if (neuron == null || LVarIndex < 0 || LVarIndex >= neuron.LocalVariables.Length) return false;

            return ConditionHelper.Compare(neuron.LocalVariables[LVarIndex], Value, Operator);
        }
    }
    
    /// <summary>
    /// A condition that checks a Global Variable (GVar / Hormone).
    /// </summary>
    public class GVarCondition : ICondition
    {
        public int GVarIndex { get; set; }
        public ComparisonOperator Operator { get; set; }
        public float Value { get; set; }

        public bool Evaluate(ConditionContext context)
        {
            if (GVarIndex < 0 || GVarIndex >= context.World.GlobalHormones.Length) return false;
            
            return ConditionHelper.Compare(context.World.GlobalHormones[GVarIndex], Value, Operator);
        }
    }
    
    /// <summary>
    /// A condition that compares a value from the source against a value from the target.
    /// </summary>
    public class RelationalCondition : ICondition
    {
        // For simplicity, this compares the incoming source value against the target neuron's total potential.
        public ComparisonOperator Operator { get; set; }

        public bool Evaluate(ConditionContext context)
        {
            if (context.TargetNeuron == null) return false;
            
            return ConditionHelper.Compare(context.SourceValue, context.TargetNeuron.GetPotential(), Operator);
        }
    }
    
    /// <summary>
    /// A condition that evaluates temporal patterns in the source's signal.
    /// </summary>
    public class TemporalCondition : ICondition
    {
        public TemporalOperator Operator { get; set; }
        public float Threshold { get; set; } // For edge detection and sustained level
        public int Duration { get; set; }    // For Sustained operator (in ticks)

        public bool Evaluate(ConditionContext context)
        {
            var synapse = context.Synapse;
            bool currentValueOverThreshold = context.SourceValue >= Threshold;
            bool previousValueOverThreshold = synapse.PreviousSourceValue >= Threshold;

            switch (Operator)
            {
                case TemporalOperator.RisingEdge:
                    return currentValueOverThreshold && !previousValueOverThreshold;
                
                case TemporalOperator.FallingEdge:
                    return !currentValueOverThreshold && previousValueOverThreshold;
                    
                case TemporalOperator.Changed:
                    return Math.Abs(context.SourceValue - synapse.PreviousSourceValue) > Threshold;

                case TemporalOperator.Sustained:
                    if (currentValueOverThreshold)
                    {
                        synapse.SustainedCounter++;
                    }
                    else
                    {
                        synapse.SustainedCounter = 0; // Reset counter if condition is broken
                    }
                    return synapse.SustainedCounter >= Duration;
            }
            return false;
        }
    }
    
    /// <summary>
    /// A condition that combines multiple other conditions using AND or OR logic.
    /// </summary>
    public class CompositeCondition : ICondition
    {
        public bool IsAndLogic { get; set; } // If false, use OR logic
        public List<ICondition> Conditions { get; set; } = new List<ICondition>();

        public bool Evaluate(ConditionContext context)
        {
            if (!Conditions.Any()) return true; // An empty composite condition is always true.

            if (IsAndLogic)
            {
                return Conditions.All(c => c.Evaluate(context));
            }
            else
            {
                return Conditions.Any(c => c.Evaluate(context));
            }
        }
    }

    /// <summary>
    /// Static helper to centralize comparison logic for all condition classes.
    /// </summary>
    internal static class ConditionHelper
    {
        internal static bool Compare(float a, float b, ComparisonOperator op)
        {
            const float epsilon = 1e-6f;
            return op switch
            {
                ComparisonOperator.GreaterThan => a > b,
                ComparisonOperator.LessThan => a < b,
                ComparisonOperator.EqualTo => Math.Abs(a - b) < epsilon,
                ComparisonOperator.NotEqualTo => Math.Abs(a - b) >= epsilon,
                ComparisonOperator.GreaterThanOrEqualTo => a >= b,
                ComparisonOperator.LessThanOrEqualTo => a <= b,
                _ => false,
            };
        }
    }
}
// Hidra.Core/Brain/LogicGateBrain.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hidra.Core.Brain
{
    #region Logic Gate Enums

    /// <summary>
    /// Defines the type of combinational (stateless) logic gate.
    /// </summary>
    public enum LogicGateType
    {
        // --- Basic Gates ---
        Buffer, // Passes input through
        NOT,
        AND,
        OR,
        NAND,
        NOR,
        XOR,
        XNOR,
    }
    
    /// <summary>
    /// Defines the type of sequential (stateful) flip-flop.
    /// </summary>
    public enum FlipFlopType
    {
        // --- Flip-Flops ---
        D_FlipFlop, // Data
        T_FlipFlop, // Toggle
        JK_FlipFlop,
    }

    #endregion

    /// <summary>
    /// A deterministic, computationally efficient brain type that simulates a single logic gate or flip-flop.
    /// </summary>
    public class LogicGateBrain : IBrain
    {
        // --- Configuration ---
        public LogicGateType GateType { get; set; } = LogicGateType.AND;
        public FlipFlopType? FlipFlop { get; set; } = null;
        public float Threshold { get; set; } = 0.5f;

        // --- Internal State for Sequential Logic ---
        private float _state = 0f;
        private float _previousClock = 0f;

        // --- IBrain Interface Implementation ---

        private readonly List<BrainInput> _inputMap = new();
        private readonly List<BrainOutput> _outputMap = new() { new BrainOutput { ActionType = OutputActionType.SetOutputValue, Value = 0f } };

        public IReadOnlyList<BrainInput> InputMap => _inputMap;
        public IReadOnlyList<BrainOutput> OutputMap => _outputMap;
        public bool CanLearn => false;

        public LogicGateBrain()
        {
            // By default, a logic brain takes a single input. This can be reconfigured.
            AddInput(InputSourceType.ActivationPotential, 0);
        }

        public void Evaluate(float[] inputs)
        {
            // Binarize inputs based on the threshold
            var b_inputs = inputs.Select(i => i >= Threshold).ToArray();
            bool output;

            if (FlipFlop.HasValue)
            {
                output = EvaluateFlipFlop(b_inputs);
            }
            else
            {
                output = EvaluateCombinational(b_inputs);
            }

            OutputMap[0].Value = output ? 1.0f : 0.0f;
        }

        private bool EvaluateCombinational(bool[] inputs)
        {
            if (inputs.Length == 0) return false;
            if (inputs.Length == 1)
            {
                return GateType switch
                {
                    LogicGateType.NOT => !inputs[0],
                    LogicGateType.NAND => !inputs[0],
                    LogicGateType.NOR => !inputs[0],
                    _ => inputs[0] // All others act as a buffer/pass-through for one input
                };
            }

            // Logic for 2 or more inputs
            return GateType switch
            {
                LogicGateType.AND => inputs.All(i => i),
                LogicGateType.OR => inputs.Any(i => i),
                LogicGateType.NAND => !inputs.All(i => i),
                LogicGateType.NOR => !inputs.Any(i => i),
                LogicGateType.XOR => inputs.Count(i => i) % 2 != 0,
                LogicGateType.XNOR => inputs.Count(i => i) % 2 == 0,
                _ => inputs[0] // Buffer/NOT for single input is handled above
            };
        }

        private bool EvaluateFlipFlop(bool[] inputs)
        {
            bool currentState = _state >= Threshold;
            // A rising edge is detected if the clock was low and is now high.
            bool clock = (inputs.Length > 0) ? inputs[0] : false;
            bool isRisingEdge = !(_previousClock >= Threshold) && clock;

            if (isRisingEdge && FlipFlop.HasValue) // <-- ADDED HasValue CHECK HERE
            {
                bool j = (inputs.Length > 1) ? inputs[1] : false;
                bool k = (inputs.Length > 2) ? inputs[2] : false;

                // Now it's safe to access FlipFlop.Value
                switch (FlipFlop.Value)
                {
                    case FlipFlopType.D_FlipFlop: // D is input[1]
                        currentState = j; 
                        break;
                    case FlipFlopType.T_FlipFlop: // T is input[1]
                        if (j) currentState = !currentState;
                        break;
                    case FlipFlopType.JK_FlipFlop: // J is input[1], K is input[2]
                        if (j && k) currentState = !currentState;
                        else if (j) currentState = true;
                        else if (k) currentState = false;
                        // if !j and !k, state holds.
                        break;
                }
            }

            _state = currentState ? 1.0f : 0.0f;
            _previousClock = clock ? 1.0f : 0.0f;

            return currentState;
        }

        public void Mutate(float rate)
        {
            // Logic gates are deterministic and generally shouldn't mutate their core function.
            // A mutation could potentially flip the gate type, but that is a larger structural change
            // best handled by the gene expression layer.
        }
        
        public void Reset()
        {
            _state = 0f;
            _previousClock = 0f;
        }
        
        // --- Configuration Methods for HGL API ---
        
        public void AddInput(InputSourceType sourceType, int sourceIndex)
        {
            _inputMap.Add(new BrainInput { SourceType = sourceType, SourceIndex = sourceIndex });
        }
        
        public void ClearInputs()
        {
            _inputMap.Clear();
        }
    }
}
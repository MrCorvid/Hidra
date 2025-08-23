// Hidra.Core/Brain/Brain.LogicGate.cs
using Hidra.Core.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hidra.Core.Brain
{
    #region Logic Gate Enums

    public enum LogicGateType
    {
        Buffer,
        NOT,
        AND,
        OR,
        NAND,
        NOR,
        XOR,
        XNOR,
    }
    
    public enum FlipFlopType
    {
        D_FlipFlop,
        T_FlipFlop,
        JK_FlipFlop,
    }

    #endregion

    /// <summary>
    /// A deterministic, computationally efficient brain type that simulates a single logic gate or flip-flop.
    /// </summary>
    public class LogicGateBrain : IBrain
    {
        public LogicGateType GateType { get; set; } = LogicGateType.AND;
        public FlipFlopType? FlipFlop { get; set; } = null;
        public float Threshold { get; set; } = 0.5f;

        private float _state = 0f;
        private float _previousClock = 0f;

        private readonly List<BrainInput> _inputMap = new();
        private readonly List<BrainOutput> _outputMap = new() { new BrainOutput { ActionType = OutputActionType.SetOutputValue, Value = 0f } };

        public IReadOnlyList<BrainInput> InputMap => _inputMap;
        public IReadOnlyList<BrainOutput> OutputMap => _outputMap;
        public bool CanLearn => false;

        public LogicGateBrain()
        {
            AddInput(InputSourceType.ActivationPotential, 0);
        }

        public void Evaluate(float[] inputs, Action<string, LogLevel, string>? logAction = null)
        {
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
                    _ => inputs[0]
                };
            }

            return GateType switch
            {
                LogicGateType.AND => inputs.All(i => i),
                LogicGateType.OR => inputs.Any(i => i),
                LogicGateType.NAND => !inputs.All(i => i),
                LogicGateType.NOR => !inputs.Any(i => i),
                LogicGateType.XOR => inputs.Count(i => i) % 2 != 0,
                LogicGateType.XNOR => inputs.Count(i => i) % 2 == 0,
                _ => inputs[0]
            };
        }

        private bool EvaluateFlipFlop(bool[] inputs)
        {
            bool currentState = _state >= Threshold;
            bool clock = (inputs.Length > 0) ? inputs[0] : false;
            bool isRisingEdge = !(_previousClock >= Threshold) && clock;

            if (isRisingEdge && FlipFlop.HasValue)
            {
                bool j = (inputs.Length > 1) ? inputs[1] : false;
                bool k = (inputs.Length > 2) ? inputs[2] : false;

                switch (FlipFlop.Value)
                {
                    case FlipFlopType.D_FlipFlop:
                        currentState = j; 
                        break;
                    case FlipFlopType.T_FlipFlop:
                        if (j) currentState = !currentState;
                        break;
                    case FlipFlopType.JK_FlipFlop:
                        if (j && k) currentState = !currentState;
                        else if (j) currentState = true;
                        else if (k) currentState = false;
                        break;
                }
            }

            _state = currentState ? 1.0f : 0.0f;
            _previousClock = clock ? 1.0f : 0.0f;

            return currentState;
        }

        public void Mutate(float rate)
        {
            // Logic gates are deterministic and do not mutate.
        }
        
        public void Reset()
        {
            _state = 0f;
            _previousClock = 0f;
        }
        
        public void SetPrng(IPrng prng)
        {
            // This brain type is deterministic and does not use a PRNG.
        }
        
        public void InitializeFromLoad()
        {
            // This brain has simple state that is correctly handled by the deserializer.
            // No special re-initialization logic is needed.
        }

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
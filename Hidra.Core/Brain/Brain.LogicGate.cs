// Hidra.Core/Brain/LogicGateBrain.cs
namespace Hidra.Core.Brain;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Defines the type of combinational (stateless) logic gate.
/// </summary>
public enum LogicGateType
{
    /// <summary>Passes input through without change.</summary>
    Buffer,
    /// <summary>Inverts the input.</summary>
    NOT,
    /// <summary>Outputs true only if all inputs are true.</summary>
    AND,
    /// <summary>Outputs true if any input is true.</summary>
    OR,
    /// <summary>The inverse of AND.</summary>
    NAND,
    /// <summary>The inverse of OR.</summary>
    NOR,
    /// <summary>Outputs true if an odd number of inputs are true.</summary>
    XOR,
    /// <summary>Outputs true if an even number of inputs are true.</summary>
    XNOR,
}

/// <summary>
/// Defines the type of sequential (stateful) flip-flop.
/// </summary>
public enum FlipFlopType
{
    /// <summary>Data flip-flop; outputs the value of the D input on the clock's rising edge.</summary>
    D_FlipFlop,
    /// <summary>Toggle flip-flop; inverts its state on the clock's rising edge if the T input is true.</summary>
    T_FlipFlop,
    /// <summary>JK flip-flop; a universal flip-flop with set, reset, hold, and toggle behaviors.</summary>
    JK_FlipFlop,
}

/// <summary>
/// A deterministic, computationally efficient brain type that simulates a single logic gate or flip-flop.
/// </summary>
public class LogicGateBrain : IBrain
{
    private readonly List<BrainInput> _inputMap = new();
    private readonly List<BrainOutput> _outputMap = new() { new BrainOutput { ActionType = OutputActionType.SetOutputValue, Value = 0f } };

    private float _state;
    private float _previousClock;

    /// <summary>Gets or sets the type of combinational gate to use when not configured as a flip-flop.</summary>
    public LogicGateType GateType { get; set; } = LogicGateType.AND;
    
    /// <summary>Gets or sets the type of sequential flip-flop to use. If null, a combinational gate is used.</summary>
    public FlipFlopType? FlipFlop { get; set; }
    
    /// <summary>Gets or sets the value at which continuous inputs are binarized to true.</summary>
    public float Threshold { get; set; } = 0.5f;

    /// <inheritdoc/>
    public IReadOnlyList<BrainInput> InputMap => _inputMap;
    /// <inheritdoc/>
    public IReadOnlyList<BrainOutput> OutputMap => _outputMap;
    /// <inheritdoc/>
    public bool CanLearn => false;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogicGateBrain"/> class.
    /// </summary>
    /// <remarks>
    /// By default, a logic brain takes a single input, mapped to the neuron's activation potential.
    /// This can be reconfigured via the <see cref="AddInput"/> and <see cref="ClearInputs"/> methods.
    /// </remarks>
    public LogicGateBrain()
    {
        AddInput(InputSourceType.ActivationPotential, 0);
    }

    /// <inheritdoc/>
    public void Evaluate(float[] inputs)
    {
        var binaryInputs = inputs.Select(i => i >= Threshold).ToArray();
        bool output = FlipFlop.HasValue
            ? EvaluateFlipFlop(binaryInputs)
            : EvaluateCombinational(binaryInputs);

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
                _ => inputs[0] // All others act as a buffer for one input
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
        bool clock = inputs.Length > 0 && inputs[0];
        bool isRisingEdge = !(_previousClock >= Threshold) && clock;

        if (isRisingEdge)
        {
            bool j = inputs.Length > 1 && inputs[1];
            bool k = inputs.Length > 2 && inputs[2];

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

    /// <inheritdoc/>
    /// <remarks>
    /// This method is a no-op. Logic gates are deterministic and their core function
    /// is not designed to be mutated. Structural changes should be handled at the gene level.
    /// </remarks>
    public void Mutate(float rate) { }
    
    /// <inheritdoc/>
    public void Reset()
    {
        _state = 0f;
        _previousClock = 0f;
    }
    
    /// <summary>
    /// Adds a new data source to the brain's input map.
    /// </summary>
    /// <param name="sourceType">The type of world or neuron data to use as an input.</param>
    /// <param name="sourceIndex">The index for source types that require it (e.g., LocalVariable).</param>
    public void AddInput(InputSourceType sourceType, int sourceIndex) => _inputMap.Add(new BrainInput { SourceType = sourceType, SourceIndex = sourceIndex });
    
    /// <summary>
    /// Removes all configured inputs from the brain.
    /// </summary>
    public void ClearInputs() => _inputMap.Clear();
}
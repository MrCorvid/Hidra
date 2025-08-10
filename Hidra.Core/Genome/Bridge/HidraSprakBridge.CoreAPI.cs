// Hidra.Core/Genome/Bridge/HidraSprakBridge.CoreAPI.cs
namespace Hidra.Core;

using System;
using System.Numerics;
using ProgrammingLanguageNr1;

public partial class HidraSprakBridge
{
    /// <summary>
    /// A constant defining the upper bound of local variables writable by user scripts.
    /// This prevents scripts from overwriting reserved system variables like Health, Age, etc.
    /// </summary>
    private const int USER_LVAR_WRITABLE_LIMIT = 240;

    [SprakAPI("Stores a value in a local variable of the target neuron.", "index", "value")]
    public void API_StoreLVar(float index, float value)
    {
        var target = GetTargetNeuron();
        if (target == null) return;

        int idx = (int)index;
        if (idx >= 0 && idx < USER_LVAR_WRITABLE_LIMIT && idx < target.LocalVariables.Length)
        {
            target.LocalVariables[idx] = value;
        }
    }

    [SprakAPI("Loads a value from a local variable of the target neuron.", "index")]
    public float API_LoadLVar(float index)
    {
        var target = GetTargetNeuron();
        if (target == null) return 0f;

        int idx = (int)index;
        if (idx >= 0 && idx < target.LocalVariables.Length)
        {
            return target.LocalVariables[idx];
        }
        return 0f;
    }

    [SprakAPI("Gets the unique identifier of the neuron executing the gene ('self').")]
    public float API_GetSelfId() => (float)(_self?.Id ?? 0);

    [SprakAPI("Gets a component of the target neuron's position.", "axis (0=X, 1=Y, 2=Z)")]
    public float API_GetPosition(float axis)
    {
        var target = GetTargetNeuron();
        if (target == null) return 0f;

        return ((int)axis) switch
        {
            0 => target.Position.X,
            1 => target.Position.Y,
            2 => target.Position.Z,
            _ => 0f,
        };
    }

    [SprakAPI("Stores a value in the world's global hormone array.", "index", "value")]
    public void API_StoreGVar(float index, float value)
    {
        int idx = (int)index;
        if (idx >= 0 && idx < _world.GlobalHormones.Length)
        {
            _world.GlobalHormones[idx] = value;
        }
    }

    [SprakAPI("Loads a value from the world's global hormone array.", "index")]
    public float API_LoadGVar(float index)
    {
        int idx = (int)index;
        if (idx >= 0 && idx < _world.GlobalHormones.Length)
        {
            return _world.GlobalHormones[idx];
        }
        return 0f;
    }

    [SprakAPI("Creates a new neuron at the specified position. Only callable in a System context.", "x", "y", "z")]
    public float API_CreateNeuron(float x, float y, float z)
    {
        if (_context != ExecutionContext.System)
        {
            return 0;
        }

        var newNeuron = _world.AddNeuron(new Vector3(x, y, z));
        _systemTargetNeuron = newNeuron;
        return (float)newNeuron.Id;
    }

    [SprakAPI("Creates a copy of the target neuron (mitosis), returning the new neuron's ID. Only callable in a Protected context.", "offset_x", "offset_y", "offset_z")]
    public float API_Mitosis(float offsetX, float offsetY, float offsetZ)
    {
        if (_context != ExecutionContext.Protected)
        {
            return 0;
        }

        var parentNeuron = GetTargetNeuron();
        if (parentNeuron == null)
        {
            return 0;
        }

        var childNeuron = _world.PerformMitosis(parentNeuron, new Vector3(offsetX, offsetY, offsetZ));
        
        return (float)(childNeuron?.Id ?? 0);
    }

    [SprakAPI("Marks the current neuron for self-destruction by deactivating it. Only callable in a General context.")]
    public void API_Apoptosis()
    {
        if (_context != ExecutionContext.General || _self == null)
        {
            return;
        }
        _world.MarkNeuronForDeactivation(_self);
    }

    [SprakAPI("Executes another gene, inheriting the current execution context and target.", "user_gene_index")]
    public void API_CallGene(float geneId)
    {
        if (geneId < 0) return;

        // Security: The user script provides a 0-based index for a user gene.
        // We add the offset from config to get the internal ID. This prevents calling system genes.
        uint systemGeneCount = _world.Config.SystemGeneCount;
        uint targetGeneId = (uint)geneId + systemGeneCount;

        // The called gene inherits the context of the calling gene.
        _world.ExecuteGene(targetGeneId, _self, _context);
    }

    [SprakAPI("Sets the active target neuron for subsequent API calls. Only effective in a System context.", "neuron_id")]
    public void API_SetSystemTarget(float neuronId)
    {
        if (_context == ExecutionContext.System)
        {
            _systemTargetNeuron = _world.GetNeuronById((ulong)neuronId);
        }
    }

    [SprakAPI("Sets the target neuron's refractory period in ticks.", "ticks")]
    public void API_SetRefractoryPeriod(float ticks)
    {
        var target = GetTargetNeuron();
        if (target != null)
        {
            // LVarIndex 2 is RefractoryPeriod
            target.LocalVariables[2] = Math.Max(0, ticks);
        }
    }

    [SprakAPI("Sets the target neuron's threshold adaptation parameters.", "adaptation_factor", "recovery_rate")]
    public void API_SetThresholdAdaptation(float adaptationFactor, float recoveryRate)
    {
        var target = GetTargetNeuron();
        if (target != null)
        {
            // LVarIndex 3 is ThresholdAdaptationFactor
            target.LocalVariables[3] = Math.Max(0, adaptationFactor);
            // LVarIndex 4 is ThresholdRecoveryRate
            target.LocalVariables[4] = Math.Clamp(recoveryRate, 0.0f, 1.0f);
        }
    }

    [SprakAPI("Gets the target neuron's current firing rate (a moving average).")]
    public float API_GetFiringRate()
    {
        var target = GetTargetNeuron();
        // LVarIndex 240 is FiringRate
        return target?.LocalVariables[240] ?? 0f;
    }
}
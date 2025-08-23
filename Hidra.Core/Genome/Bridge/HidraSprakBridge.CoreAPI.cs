// Hidra.Core/Genome/Bridge/HidraSprakBridge.CoreAPI.cs
using ProgrammingLanguageNr1;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hidra.Core
{
    public partial class HidraSprakBridge
    {
        #region Core API

        [SprakAPI("No-op placeholder for missing/unsupported API calls.")]
        public void API_NOP()
        {
            LogDbg("BRIDGE.CORE", "API_NOP()");
            // Intentionally empty: preserves interpreter state and guarantees no crash.
        }

        /// <summary>
        /// Upper bound of local variables writable by user scripts.
        /// </summary>
        private const int USER_LVAR_WRITABLE_LIMIT = (int)LVarIndex.RefractoryTimeLeft;

        [SprakAPI("Stores a value in a local variable of the target neuron.", "index", "value")]
        public void API_StoreLVar(float index, float value)
        {
            var target = GetTargetNeuron();
            LogDbg("BRIDGE.CORE", $"API_StoreLVar(index={index}, value={value}) -> target={target?.Id.ToString() ?? "null"}");
            if (target == null) return;

            try
            {
                int idx = (int)index;
                if (idx >= 0 && idx < USER_LVAR_WRITABLE_LIMIT && idx < target.LocalVariables.Length)
                {
                    target.LocalVariables[idx] = value;
                    LogDbg("BRIDGE.CORE", $"LVar[{idx}] set to {value}");
                }
                else
                {
                    LogWarn("BRIDGE.CORE", $"LVar index {idx} out of writable/bounds; ignored.");
                }
            }
            catch (Exception ex)
            {
                LogErr("BRIDGE.CORE", $"API_StoreLVar exception: {ex.Message}");
            }
        }

        [SprakAPI("Loads a value from a local variable of the target neuron.", "index")]
        public float API_LoadLVar(float index)
        {
            var target = GetTargetNeuron();
            LogDbg("BRIDGE.CORE", $"API_LoadLVar(index={index}) -> target={target?.Id.ToString() ?? "null"}");
            if (target == null) return 0f;

            try
            {
                int idx = (int)index;
                if (idx >= 0 && idx < target.LocalVariables.Length)
                {
                    var val = target.LocalVariables[idx];
                    LogDbg("BRIDGE.CORE", $"LVar[{idx}] = {val}");
                    return val;
                }
                LogWarn("BRIDGE.CORE", $"LVar index {idx} out of bounds; returning 0.");
                return 0f;
            }
            catch (Exception ex)
            {
                LogErr("BRIDGE.CORE", $"API_LoadLVar exception: {ex.Message}");
                return 0f;
            }
        }

        [SprakAPI("Gets the unique identifier of the neuron executing the gene ('self'). " +
                  "In System context, returns the current System target neuron if available.")]
        public float API_GetSelfId()
        {
            var who = _context == ExecutionContext.System ? _systemTargetNeuron : _self;
            var idf = (float)(who?.Id ?? 0);
            LogDbg("BRIDGE.CORE", $"API_GetSelfId() -> {idf}");
            return idf;
        }

        [SprakAPI("Gets a component of the target neuron's position.", "axis (0=X, 1=Y, 2=Z)")]
        public float API_GetPosition(float axis)
        {
            var target = GetTargetNeuron();
            LogDbg("BRIDGE.CORE", $"API_GetPosition(axis={axis}) -> target={target?.Id.ToString() ?? "null"}");
            if (target == null) return 0f;

            var result = ((int)axis) switch
            {
                0 => target.Position.X,
                1 => target.Position.Y,
                2 => target.Position.Z,
                _ => 0f,
            };
            LogDbg("BRIDGE.CORE", $"API_GetPosition -> {result}");
            return result;
        }

        [SprakAPI("Stores a value in the world's global hormone array.", "index", "value")]
        public void API_StoreGVar(float index, float value)
        {
            LogDbg("BRIDGE.CORE", $"API_StoreGVar(index={index}, value={value})");
            try
            {
                _world.SetGlobalHormones(new Dictionary<int, float> { { (int)index, value } });
            }
            catch (Exception ex)
            {
                LogErr("BRIDGE.CORE", $"API_StoreGVar exception: {ex.Message}");
            }
        }

        [SprakAPI("Loads a value from the world's global hormone array.", "index")]
        public float API_LoadGVar(float index)
        {
            LogDbg("BRIDGE.CORE", $"API_LoadGVar(index={index})");
            try
            {
                int idx = (int)index;
                var hormones = _world.GetGlobalHormones();
                if (idx >= 0 && idx < hormones.Count)
                {
                    var val = hormones[idx];
                    LogDbg("BRIDGE.CORE", $"GVar[{idx}] = {val}");
                    return val;
                }
                LogWarn("BRIDGE.CORE", $"GVar index {idx} out of bounds; returning 0.");
                return 0f;
            }
            catch (Exception ex)
            {
                LogErr("BRIDGE.CORE", $"API_LoadGVar exception: {ex.Message}");
                return 0f;
            }
        }

        [SprakAPI("Creates a new neuron at the specified position. Only callable in a System context.", "x", "y", "z")]
        public float API_CreateNeuron(float x, float y, float z)
        {
            LogDbg("BRIDGE.CORE", $"API_CreateNeuron(x={x}, y={y}, z={z})");
            if (_context != ExecutionContext.System)
            {
                LogWarn("BRIDGE.CORE", "API_CreateNeuron called outside System context; ignored.");
                return 0;
            }

            try
            {
                var newNeuron = _world.AddNeuron(new Vector3(x, y, z));
                _systemTargetNeuron = newNeuron;
                LogDbg("BRIDGE.CORE", $"Created neuron id={newNeuron.Id} at ({x},{y},{z}) and set as system target.");
                return (float)newNeuron.Id;
            }
            catch (Exception ex)
            {
                LogErr("BRIDGE.CORE", $"API_CreateNeuron exception: {ex.Message}");
                return 0;
            }
        }

        [SprakAPI("Creates a copy of the target neuron (mitosis), returning the new neuron's ID. Callable from System or General contexts.", "offset_x", "offset_y", "offset_z")]
        public float API_Mitosis(float offsetX, float offsetY, float offsetZ)
        {
            LogDbg("BRIDGE.CORE", $"API_Mitosis(dx={offsetX}, dy={offsetY}, dz={offsetZ})");
            if (_context == ExecutionContext.Protected)
            {
                LogWarn("BRIDGE.CORE", "API_Mitosis called from Protected context; ignored.");
                return 0;
            }

            var parentNeuron = GetTargetNeuron();
            if (parentNeuron == null)
            {
                LogWarn("BRIDGE.CORE", "API_Mitosis has no target neuron; ignored.");
                return 0;
            }

            try
            {
                var childNeuron = _world.PerformMitosis(parentNeuron, new Vector3(offsetX, offsetY, offsetZ));
                var idf = (float)(childNeuron?.Id ?? 0);
                LogDbg("BRIDGE.CORE", $"Mitosis created neuron id={idf}");
                return idf;
            }
            catch (Exception ex)
            {
                LogErr("BRIDGE.CORE", $"API_Mitosis exception: {ex.Message}");
                return 0;
            }
        }

        [SprakAPI("Marks the current neuron for self-destruction by deactivating it. Only callable in a General context.")]
        public void API_Apoptosis()
        {
            LogDbg("BRIDGE.CORE", "API_Apoptosis()");
            if (_context != ExecutionContext.General || _self == null)
            {
                LogWarn("BRIDGE.CORE", "API_Apoptosis ignored (wrong context or no self).");
                return;
            }
            _world.MarkNeuronForDeactivation(_self);
            LogDbg("BRIDGE.CORE", $"Neuron {_self.Id} marked for deactivation.");
        }

        [SprakAPI("Executes another gene, inheriting the current execution context and target.", "user_gene_index")]
        public void API_CallGene(float geneId)
        {
            LogDbg("BRIDGE.CORE", $"API_CallGene(geneIndex={geneId})");
            if (geneId < 0) return;
            
            uint systemGeneCount = _world.Config.SystemGeneCount;
            uint targetGeneId = (uint)geneId + systemGeneCount;

            _world.ExecuteGene(targetGeneId, _self, _context);
            LogDbg("BRIDGE.CORE", $"Requested ExecuteGene({targetGeneId})");
        }

        [SprakAPI("Sets the active target neuron for subsequent API calls. Only valid in System context. " +
                  "A specific ID will be used if it exists, otherwise a modulus is applied to all neurons.", "neuron_id")]
        public void API_SetSystemTarget(float neuronId)
        {
            LogDbg("BRIDGE.CORE", $"API_SetSystemTarget(id={neuronId})");
            if (_context != ExecutionContext.System)
            {
                LogWarn("BRIDGE.CORE", "API_SetSystemTarget called outside System context; ignored.");
                return;
            }

            var id = (ulong)neuronId;
            var target = _world.GetNeuronById(id);
            
            // If the specific target ID was not found, fall back to modulus logic.
            if (target == null)
            {
                var ids = _world.GetNeuronIdsSnapshot();
                if (ids.Any())
                {
                    // Use long for modulus to prevent overflow issues with large ulong IDs cast to int
                    var index = (int)(Math.Abs((long)id) % ids.Count);
                    target = _world.GetNeuronById(ids[index]);
                }
            }

            _systemTargetNeuron = target;
            LogDbg("BRIDGE.CORE", $"System target set to neuron={_systemTargetNeuron?.Id.ToString() ?? "null"}");
        }

        #endregion

        #region Implemented Core API Functions

        [SprakAPI("Sets the target neuron's refractory period in ticks.", "ticks")]
        public void API_SetRefractoryPeriod(float ticks)
        {
            LogDbg("BRIDGE.CORE", $"API_SetRefractoryPeriod(ticks={ticks})");
            var target = GetTargetNeuron();
            if (target != null)
            {
                target.LocalVariables[(int)LVarIndex.RefractoryPeriod] = Math.Max(0, ticks);
            }
        }

        [SprakAPI("Sets the target neuron's threshold adaptation parameters.", "adaptation_factor", "recovery_rate")]
        public void API_SetThresholdAdaptation(float adaptationFactor, float recoveryRate)
        {
            LogDbg("BRIDGE.CORE", $"API_SetThresholdAdaptation(factor={adaptationFactor}, rate={recoveryRate})");
            var target = GetTargetNeuron();
            if (target != null)
            {
                target.LocalVariables[(int)LVarIndex.ThresholdAdaptationFactor] = Math.Max(0, adaptationFactor);
                target.LocalVariables[(int)LVarIndex.ThresholdRecoveryRate] = Math.Clamp(recoveryRate, 0.0f, 1.0f);
            }
        }

        [SprakAPI("Gets the target neuron's current firing rate (a moving average).")]
        public float API_GetFiringRate()
        {
            LogDbg("BRIDGE.CORE", "API_GetFiringRate()");
            var target = GetTargetNeuron();
            if (target == null) return 0f;

            var rate = target.LocalVariables[(int)LVarIndex.FiringRate];
            LogDbg("BRIDGE.CORE", $" -> FiringRate = {rate}");
            return rate;
        }

        #endregion
    }
}
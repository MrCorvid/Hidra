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
            LogTrace("BRIDGE.CORE", "API_NOP()");
            // Intentionally empty: preserves interpreter state and guarantees no crash.
        }

        /// <summary>
        /// Upper bound of local variables writable by user scripts.
        /// </summary>
        private const int USER_LVAR_WRITABLE_LIMIT = (int)LVarIndex.RefractoryTimeLeft;

        [SprakAPI("Stores a value in a local variable of the target neuron.", "index", "value")]
        public void API_StoreLVar(float index, float value)
        {
            LogTrace("BRIDGE.CORE", $"API_StoreLVar(index={index}, value={value})");
            var target = GetTargetNeuron();
            if (target == null) return;

            try
            {
                int idx = (int)index;
                if (idx >= 0 && idx < USER_LVAR_WRITABLE_LIMIT && idx < target.LocalVariables.Length)
                {
                    target.LocalVariables[idx] = value;
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
            LogTrace("BRIDGE.CORE", $"API_LoadLVar(index={index})");
            var target = GetTargetNeuron();
            if (target == null) return 0f;

            try
            {
                int idx = (int)index;
                if (idx >= 0 && idx < target.LocalVariables.Length)
                {
                    return target.LocalVariables[idx];
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
            LogTrace("BRIDGE.CORE", "API_GetSelfId()");
            var who = _context == ExecutionContext.System ? _systemTargetNeuron : _self;
            return (float)(who?.Id ?? 0);
        }

        [SprakAPI("Gets a component of the target neuron's position.", "axis (0=X, 1=Y, 2=Z)")]
        public float API_GetPosition(float axis)
        {
            LogTrace("BRIDGE.CORE", $"API_GetPosition(axis={axis})");
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
            LogTrace("BRIDGE.CORE", $"API_StoreGVar(index={index}, value={value})");
            try
            {
                var hormones = _world.GetGlobalHormonesDirect(); // Get direct reference to the array
                int count = hormones.Length;
                int idx = (int)index;

                if (count == 0) {
                    LogWarn("BRIDGE.CORE", "Attempted to StoreGVar but no global variables are configured.");
                    return;
                }

                // 1. Literal ID (Index) Check
                if (idx >= 0 && idx < count)
                {
                    hormones[idx] = value;
                    LogTrace("BRIDGE.CORE", $"Stored in literal index {idx}.");
                }
                else // 2. Relative Modulus Fallback
                {
                    // This handles potential negative inputs robustly.
                    int safeIndex = (idx % count + count) % count;
                    hormones[safeIndex] = value;
                    LogTrace("BRIDGE.CORE", $"Literal index invalid. Used modulus fallback to store in index {safeIndex}.");
                }
            }
            catch (Exception ex)
            {
                LogErr("BRIDGE.CORE", $"API_StoreGVar exception: {ex.Message}");
            }
        }

        [SprakAPI("Loads a value from the world's global hormone array.", "index")]
        public float API_LoadGVar(float index)
        {
            LogTrace("BRIDGE.CORE", $"API_LoadGVar(index={index})");
            try
            {
                var hormones = _world.GetGlobalHormonesDirect();
                int count = hormones.Length;
                int idx = (int)index;

                if (count == 0) {
                    LogWarn("BRIDGE.CORE", "Attempted to LoadGVar but no global variables are configured.");
                    return 0f;
                }

                // 1. Literal ID (Index) Check
                if (idx >= 0 && idx < count)
                {
                    LogTrace("BRIDGE.CORE", $"Reading from literal index {idx}.");
                    return hormones[idx];
                }
                else // 2. Relative Modulus Fallback
                {
                    int safeIndex = (idx % count + count) % count;
                    LogTrace("BRIDGE.CORE", $"Literal index invalid. Used modulus fallback to read from index {safeIndex}.");
                    return hormones[safeIndex];
                }
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
            LogTrace("BRIDGE.CORE", $"API_CreateNeuron(x={x}, y={y}, z={z})");
            if (_context != ExecutionContext.System)
            {
                LogWarn("BRIDGE.CORE", "API_CreateNeuron called outside System context; ignored.");
                return 0;
            }

            try
            {
                var newNeuron = _world.AddNeuron(new Vector3(x, y, z));
                _systemTargetNeuron = newNeuron;
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
            LogTrace("BRIDGE.CORE", $"API_Mitosis(dx={offsetX}, dy={offsetY}, dz={offsetZ})");
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
                return (float)(childNeuron?.Id ?? 0);
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
            LogTrace("BRIDGE.CORE", "API_Apoptosis()");
            if (_context != ExecutionContext.General || _self == null)
            {
                LogWarn("BRIDGE.CORE", "API_Apoptosis ignored (wrong context or no self).");
                return;
            }
            _world.MarkNeuronForDeactivation(_self);
        }

        [SprakAPI("Executes another gene, inheriting the current execution context and target.", "user_gene_index")]
        public void API_CallGene(float geneId)
        {
            LogTrace("BRIDGE.CORE", $"API_CallGene(geneIndex={geneId})");
            int requestedGeneIndex = (int)geneId;

            // --- FIX ---
            // Use the newly created public method on HidraWorld to get the total gene count.
            int totalGeneCount = _world.GetGeneCount();
            uint systemGeneCount = _world.Config.SystemGeneCount;
            int userGeneCount = totalGeneCount - (int)systemGeneCount;

            if (userGeneCount <= 0)
            {
                LogWarn("BRIDGE.CORE", "API_CallGene ignored, no user genes available.");
                return;
            }

            uint finalGeneId;

            // 1. Literal ID (Index) Check
            if (requestedGeneIndex >= 0 && requestedGeneIndex < userGeneCount)
            {
                // Add the system gene offset to get the absolute gene ID
                finalGeneId = (uint)requestedGeneIndex + systemGeneCount;
                LogTrace("BRIDGE.CORE", $"Calling literal gene index {requestedGeneIndex} (absolute ID {finalGeneId}).");
            }
            else // 2. Relative Modulus Fallback
            {
                int safeIndex = (requestedGeneIndex % userGeneCount + userGeneCount) % userGeneCount;
                // Add the system gene offset to the safe index
                finalGeneId = (uint)safeIndex + systemGeneCount;
                LogTrace("BRIDGE.CORE", $"Literal gene index invalid. Used modulus fallback to call index {safeIndex} (absolute ID {finalGeneId}).");
            }
            
            _world.ExecuteGene(finalGeneId, _self, _context);
        }

        [SprakAPI("Sets the active target neuron for subsequent API calls. Only valid in System context. " +
                  "A specific ID will be used if it exists, otherwise a modulus is applied to all neurons.", "neuron_id")]
        public void API_SetSystemTarget(float neuronId)
        {
            LogTrace("BRIDGE.CORE", $"API_SetSystemTarget(id={neuronId})");
            if (_context != ExecutionContext.System)
            {
                LogWarn("BRIDGE.CORE", "API_SetSystemTarget called outside System context; ignored.");
                return;
            }

            var id = (ulong)neuronId;
            var target = _world.GetNeuronById(id);
            
            if (target == null)
            {
                var ids = _world.GetNeuronIdsSnapshot();
                if (ids.Any())
                {
                    var index = (int)(Math.Abs((long)id) % ids.Count);
                    target = _world.GetNeuronById(ids[index]);
                }
            }

            _systemTargetNeuron = target;
        }

        #endregion

        #region Implemented Core API Functions

        [SprakAPI("Sets the target neuron's refractory period in ticks.", "ticks")]
        public void API_SetRefractoryPeriod(float ticks)
        {
            LogTrace("BRIDGE.CORE", $"API_SetRefractoryPeriod(ticks={ticks})");
            var target = GetTargetNeuron();
            if (target != null)
            {
                target.LocalVariables[(int)LVarIndex.RefractoryPeriod] = Math.Max(0, ticks);
            }
        }

        [SprakAPI("Sets the target neuron's threshold adaptation parameters.", "adaptation_factor", "recovery_rate")]
        public void API_SetThresholdAdaptation(float adaptationFactor, float recoveryRate)
        {
            LogTrace("BRIDGE.CORE", $"API_SetThresholdAdaptation(factor={adaptationFactor}, rate={recoveryRate})");
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
            LogTrace("BRIDGE.CORE", "API_GetFiringRate()");
            var target = GetTargetNeuron();
            if (target == null) return 0f;

            return target.LocalVariables[(int)LVarIndex.FiringRate];
        }

        #endregion
    }
}
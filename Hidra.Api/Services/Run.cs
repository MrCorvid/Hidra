// Hidra.API/Services/Run.cs
using Hidra.Core;
using Hidra.API.DTOs;
using System;
using System.Threading;

namespace Hidra.API.Services
{
    public enum RunStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Aborted
    }

    public class Run
    {
        public string Id { get; }
        public RunStatus Status { get; private set; }
        public string CompletionReason { get; private set; } = "N/A";
        
        public CreateRunRequestDto Request { get; }
        public DateTime StartTime { get; private set; }
        public DateTime EndTime { get; private set; }
        public ulong StartTick { get; private set; }
        public ulong EndTick { get; private set; }

        private readonly Experiment _parentExperiment;

        // How many ticks to execute before momentarily releasing the lock
        private const int TICK_CHUNK_SIZE = 100; 

        public Run(Experiment parentExperiment, CreateRunRequestDto request)
        {
            Id = $"run_{Guid.NewGuid():N}";
            Status = RunStatus.Pending;
            Request = request;
            _parentExperiment = parentExperiment;
        }

        /// <summary>
        /// Executes the run logic. Now designed to run on a background Task.
        /// </summary>
        public void Execute()
        {
            // Initial Setup - Atomic check
            lock (_parentExperiment.GetLockObject())
            {
                if (_parentExperiment.State != SimulationState.Idle)
                {
                    Status = RunStatus.Failed;
                    CompletionReason = "Experiment was not idle at run start.";
                    return;
                }

                try
                {
                    Status = RunStatus.Running;
                    StartTime = DateTime.UtcNow;
                    StartTick = _parentExperiment.World.CurrentTick;

                    // Apply inputs/hormones once at start
                    if (Request.StagedInputs != null)
                        _parentExperiment.World.SetInputValues(Request.StagedInputs);
                    if (Request.StagedHormones != null)
                        _parentExperiment.World.SetGlobalHormones(Request.StagedHormones);
                }
                catch (Exception ex)
                {
                    Status = RunStatus.Failed;
                    CompletionReason = $"Initialization failed: {ex.Message}";
                    return;
                }
            }

            try
            {
                if (Request.Type.Equals("runFor", StringComparison.OrdinalIgnoreCase))
                {
                    ExecuteRunFor();
                }
                else if (Request.Type.Equals("runUntil", StringComparison.OrdinalIgnoreCase))
                {
                    ExecuteRunUntil();
                }
                else
                {
                    throw new NotSupportedException($"Run type '{Request.Type}' is not supported.");
                }
            }
            catch (Exception ex)
            {
                Status = RunStatus.Failed;
                CompletionReason = $"Execution error: {ex.Message}";
            }
            finally
            {
                // Finalize state
                lock (_parentExperiment.GetLockObject())
                {
                    EndTick = _parentExperiment.World.CurrentTick;
                    EndTime = DateTime.UtcNow;
                    
                    // Only mark completed if not already Failed/Aborted
                    if (Status == RunStatus.Running)
                    {
                        Status = RunStatus.Completed;
                    }
                }
                
                // Save final state
                try { _parentExperiment.SaveCurrentTickState(); } catch { /* Ignore IO errors on final save */ }
            }
        }

        private void ExecuteRunFor()
        {
            int ticksTotal = Request.Parameters.Ticks ?? throw new ArgumentNullException("ticks");
            int ticksExecuted = 0;

            while (ticksExecuted < ticksTotal)
            {
                // Execute a chunk
                int chunk = Math.Min(TICK_CHUNK_SIZE, ticksTotal - ticksExecuted);
                
                lock (_parentExperiment.GetLockObject())
                {
                    // Safety check: If user manually Stopped the experiment via API during the run
                    if (_parentExperiment.State != SimulationState.Idle && _parentExperiment.State != SimulationState.Running)
                    {
                        Status = RunStatus.Aborted;
                        CompletionReason = "Experiment state changed externally.";
                        return;
                    }

                    for (int i = 0; i < chunk; i++)
                    {
                        _parentExperiment.World.Step();
                    }
                }

                ticksExecuted += chunk;

                // Optional: Save state occasionally (e.g., every 10 chunks) to avoid data loss on crash
                if (ticksExecuted % (TICK_CHUNK_SIZE * 10) == 0)
                {
                     _parentExperiment.SaveCurrentTickState();
                }

                // Yield to allow other threads (API reads) to acquire the lock if needed
                Thread.Sleep(1);
            }
            CompletionReason = $"Completed {ticksTotal} ticks.";
        }
        
        private void ExecuteRunUntil()
        {
            var predicateDto = Request.Parameters.Predicate ?? throw new ArgumentNullException("predicate");
            var maxTicks = Request.Parameters.MaxTicks ?? throw new ArgumentNullException("maxTicks");
            
            Func<HidraWorld, bool> stopCondition = BuildPredicate(predicateDto);

            int ticksExecuted = 0;
            bool conditionMet = false;

            while (ticksExecuted < maxTicks && !conditionMet)
            {
                // Process one tick at a time or small chunks to check predicate
                lock (_parentExperiment.GetLockObject())
                {
                     if (_parentExperiment.State != SimulationState.Idle && _parentExperiment.State != SimulationState.Running)
                    {
                        Status = RunStatus.Aborted;
                        CompletionReason = "Experiment state changed externally.";
                        return;
                    }

                    _parentExperiment.World.Step();
                    
                    if (stopCondition(_parentExperiment.World))
                    {
                        conditionMet = true;
                    }
                }
                
                ticksExecuted++;
                
                // Yield every 100 ticks
                if (ticksExecuted % 100 == 0) Thread.Sleep(1);
            }

            CompletionReason = conditionMet ? "Predicate met." : "MaxTicks reached.";
        }

        private Func<HidraWorld, bool> BuildPredicate(PredicateDto predicateDto)
        {
            return predicateDto.Type.ToLowerInvariant() switch
            {
                "outputabove" => (world) =>
                {
                    if (!predicateDto.OutputId.HasValue || !predicateDto.Value.HasValue) return false;
                    var outputNode = world.GetOutputNodeById(predicateDto.OutputId.Value);
                    return outputNode != null && outputNode.Value > predicateDto.Value.Value;
                },
                _ => throw new ArgumentException($"Unknown predicate type: '{predicateDto.Type}'.")
            };
        }
    }
}
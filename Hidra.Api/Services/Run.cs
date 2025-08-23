using Hidra.Core;
using Hidra.API.DTOs;
using System;

namespace Hidra.API.Services
{
    public enum RunStatus
    {
        Pending,
        Running,
        Completed,
        Failed
    }

    /// <summary>
    /// Represents a single, auditable execution segment within an Experiment.
    /// It encapsulates the parameters, status, and outcome of a specific run command.
    /// </summary>
    public class Run
    {
        // --- Identity and Status ---
        public string Id { get; }
        public RunStatus Status { get; private set; }
        public string CompletionReason { get; private set; } = "N/A";
        
        // --- Auditing Information ---
        public CreateRunRequestDto Request { get; }
        public DateTime StartTime { get; private set; }
        public DateTime EndTime { get; private set; }
        public ulong StartTick { get; private set; }
        public ulong EndTick { get; private set; }

        private readonly Experiment _parentExperiment;

        public Run(Experiment parentExperiment, CreateRunRequestDto request)
        {
            Id = $"run_{Guid.NewGuid():N}";
            Status = RunStatus.Pending;
            Request = request;
            _parentExperiment = parentExperiment;
        }

        /// <summary>
        /// Executes the run. This is a synchronous, blocking call.
        /// </summary>
        public void Execute()
        {
            // Lock the parent experiment to ensure no other actions can interfere.
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
                    // --- PRE-RUN SETUP ---
                    Status = RunStatus.Running;
                    StartTime = DateTime.UtcNow;
                    StartTick = _parentExperiment.World.CurrentTick;

                    // Atomically apply staged inputs and hormones
                    if (Request.StagedInputs != null)
                        _parentExperiment.World.SetInputValues(Request.StagedInputs);
                    if (Request.StagedHormones != null)
                        _parentExperiment.World.SetGlobalHormones(Request.StagedHormones);
                    
                    // --- EXECUTION LOGIC ---
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
                catch(Exception ex)
                {
                    Status = RunStatus.Failed;
                    CompletionReason = $"An exception occurred during execution: {ex.Message}";
                }
                finally
                {
                    // --- POST-RUN CLEANUP ---
                    if (Status == RunStatus.Running) // If it didn't fail
                    {
                        Status = RunStatus.Completed;
                    }
                    EndTick = _parentExperiment.World.CurrentTick;
                    EndTime = DateTime.UtcNow;
                }
            }
        }

        private void ExecuteRunFor()
        {
            int ticksToRun = Request.Parameters.Ticks ?? 
                             throw new ArgumentNullException(nameof(Request.Parameters.Ticks), "runFor requires a 'ticks' parameter.");

            for (int i = 0; i < ticksToRun; i++)
            {
                _parentExperiment.World.Step();
            }
            CompletionReason = $"Completed {ticksToRun} ticks.";
        }
        
        private void ExecuteRunUntil()
        {
            var predicateDto = Request.Parameters.Predicate ?? 
                               throw new ArgumentNullException(nameof(Request.Parameters.Predicate), "runUntil requires a 'predicate' parameter.");
            
            var maxTicks = Request.Parameters.MaxTicks ?? 
                           throw new ArgumentNullException(nameof(Request.Parameters.MaxTicks), "runUntil requires a 'maxTicks' parameter.");

            Func<HidraWorld, bool> stopCondition = BuildPredicate(predicateDto);

            for (int i = 0; i < maxTicks; i++)
            {
                _parentExperiment.World.Step();
                if (stopCondition(_parentExperiment.World))
                {
                    CompletionReason = "Predicate met.";
                    return; // Exit successfully
                }
            }
            CompletionReason = "MaxTicks reached.";
        }

        private Func<HidraWorld, bool> BuildPredicate(PredicateDto predicateDto)
        {
            return predicateDto.Type.ToLowerInvariant() switch
            {
                "outputabove" => (world) =>
                {
                    if (!predicateDto.OutputId.HasValue || !predicateDto.Value.HasValue)
                        throw new ArgumentException("Predicate 'outputAbove' requires 'outputId' and 'value'.");
                    
                    var outputNode = world.GetOutputNodeById(predicateDto.OutputId.Value);
                    return outputNode != null && outputNode.Value > predicateDto.Value.Value;
                },
                _ => throw new ArgumentException($"Unknown predicate type: '{predicateDto.Type}'.")
            };
        }
    }
}
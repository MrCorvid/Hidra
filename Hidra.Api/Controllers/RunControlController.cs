using Microsoft.AspNetCore.Mvc;
using Hidra.API.Services;
using Hidra.API.DTOs;
using System.Linq;

namespace Hidra.API.Controllers
{
    /// <summary>
    /// Provides endpoints to control the execution of a specific experiment. This includes
    /// simple continuous control (start, pause, step) and audited, transactional runs.
    /// </summary>
    [ApiController]
    [Route("api/experiments/{expId}")]
    public class RunControlController : ControllerBase
    {
        private readonly ExperimentManager _manager;

        public RunControlController(ExperimentManager manager)
        {
            _manager = manager;
        }

        private Experiment? GetExperiment(string expId)
        {
            return _manager.GetExperiment(expId);
        }

        #region Continuous (Non-Audited) Lifecycle Control

        // ... (start, pause, resume, stop, step, atomicStep, save methods are unchanged) ...
        [HttpPost("start")]
        public IActionResult Start(string expId)
        {
            var exp = GetExperiment(expId);
            if (exp == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            exp.Start();
            return Ok(new { message = $"Continuous run for experiment '{expId}' started."});
        }
        
        [HttpPost("pause")]
        public IActionResult Pause(string expId)
        {
            var exp = GetExperiment(expId);
            if (exp == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            
            exp.Pause();
            return Ok(new { message = $"Continuous run for experiment '{expId}' paused." });
        }

        [HttpPost("resume")]
        public IActionResult Resume(string expId)
        {
            var exp = GetExperiment(expId);
            if (exp == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            exp.Resume();
            return Ok(new { message = $"Continuous run for experiment '{expId}' resumed." });
        }
        
        [HttpPost("stop")]
        public IActionResult Stop(string expId)
        {
            var exp = GetExperiment(expId);
            if (exp == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            exp.Stop();
            return Ok(new { message = $"Continuous run for experiment '{expId}' stopped." });
        }

        [HttpPost("step")]
        public IActionResult Step(string expId)
        {
            var exp = GetExperiment(expId);
            if (exp == null)
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            exp.Step();
            return Ok(new { message = $"Experiment '{expId}' advanced by one tick.", newTick = exp.World.CurrentTick });
        }
        
        [HttpPost("atomicStep")]
        public IActionResult AtomicStep(string expId, [FromBody] AtomicStepRequestDto body)
        {
            var exp = GetExperiment(expId);
            if (exp == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            AtomicStepResponseDto response;
            lock(exp.GetLockObject())
            {
                if (exp.State != SimulationState.Idle)
                {
                    return Conflict(new { error = "Conflict", message = "Cannot perform atomic step while a continuous run is active. Stop it first."});
                }
                
                exp.World.ApplyInputsAndStep(body.Inputs);
                
                response = new AtomicStepResponseDto
                {
                    NewTick = exp.World.CurrentTick,
                    EventsProcessed = exp.World.GetEventsForTick(exp.World.CurrentTick),
                    OutputValues = exp.World.GetOutputValues(body.OutputIdsToRead)
                };
            }
            
            return Ok(response);
        }

        [HttpPost("save")]
        public IActionResult SaveState(string expId, [FromBody] SaveRequestDto request)
        {
            var exp = GetExperiment(expId);
            if (exp == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            
            string jsonContent;
            string path;
            lock (exp.GetLockObject())
            {
                (path, jsonContent) = exp.World.SaveStateToJson(request.ExperimentName);
            }

            return Ok(new { message = $"World state for experiment '{expId}' saved successfully.", path, worldJson = jsonContent });
        }

        #endregion

        #region Audited Run Endpoints

        /// <summary>
        /// Creates and starts a new audited run for an experiment.
        /// </summary>
        [HttpPost("runs")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public IActionResult CreateRun(string expId, [FromBody] CreateRunRequestDto request)
        {
            var exp = GetExperiment(expId);
            if (exp == null)
            {
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            }

            if (exp.State != SimulationState.Idle)
            {
                return Conflict(new { error = "Conflict", message = "Cannot create a run while a continuous simulation is active. Stop it first." });
            }

            var run = exp.CreateAndExecuteRun(request);

            var response = new 
            {
                id = run.Id,
                status = run.Status.ToString(),
                startTick = run.StartTick,
                parameters = run.Request.Parameters
            };
            
            return AcceptedAtAction(nameof(GetRun), new { expId, runId = run.Id }, response);
        }

        /// <summary>
        /// Retrieves the status and results of a specific audited run.
        /// </summary>
        [HttpGet("runs/{runId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetRun(string expId, string runId)
        {
            var exp = GetExperiment(expId);
            if (exp == null)
            {
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            }

            var run = exp.RunHistory.FirstOrDefault(r => r.Id == runId);
            if (run == null)
            {
                return NotFound(new { error = "NotFound", message = $"Run '{runId}' not found in experiment '{expId}'." });
            }

            return Ok(new 
            {
                id = run.Id,
                status = run.Status.ToString(),
                startTick = run.StartTick,
                endTick = run.EndTick,
                durationTicks = (run.Status != RunStatus.Pending && run.Status != RunStatus.Running) ? (run.EndTick - run.StartTick) : 0,
                completionReason = run.CompletionReason,
                parameters = run.Request.Parameters
            });
        }

        #endregion
    }
}
// In Hidra.API/Controllers/ExperimentsController.cs
using Hidra.API.DTOs;
using Hidra.API.Services;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System;
using Hidra.Core;
using System.Numerics; // Required for Vector3.Zero

namespace Hidra.API.Controllers
{
    [ApiController]
    [Route("api/experiments")]
    public class ExperimentsController : ControllerBase
    {
        private readonly ExperimentManager _manager;

        public ExperimentsController(ExperimentManager manager)
        {
            _manager = manager;
        }

        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult CreateExperiment([FromBody] CreateExperimentRequestDto body)
        {
            var experiment = _manager.CreateExperiment(body);

            var response = new
            {
                id = experiment.Id,
                name = experiment.Name,
                state = experiment.State.ToString().ToLowerInvariant(),
                tick = experiment.World.CurrentTick,
                createdAt = DateTime.UtcNow
            };

            return CreatedAtAction(nameof(GetExperiment), new { expId = experiment.Id }, response);
        }
        

        [HttpPost("restore")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult RestoreExperiment([FromBody] RestoreExperimentRequestDto body)
        {
            HidraWorld restoredWorld;
            try
            {
                // CORRECT: Call the new overload, passing primitives from the DTO.
                // This properly decouples the Core from the API DTOs.
                restoredWorld = HidraWorld.LoadStateFromJson(
                    body.SnapshotJson, 
                    body.HGLGenome, 
                    body.Config, 
                    body.IOConfig.InputNodeIds, 
                    body.IOConfig.OutputNodeIds
                );
            }
            catch(Exception ex)
            {
                return BadRequest(new { error = "RestoreFailed", message = $"Failed to load world state from JSON: {ex.Message}"}); 
            }
            
            // This part remains the same, as CreateExperimentRequestDto is a valid DTO for the manager.
            var createRequest = new CreateExperimentRequestDto
            {
                Name = body.Name,
                Config = body.Config,
                HGLGenome = body.HGLGenome,
                IOConfig = body.IOConfig
            };

            var experiment = _manager.CreateExperimentFromWorld(createRequest, restoredWorld);

            var response = new
            {
                id = experiment.Id,
                name = experiment.Name,
                state = experiment.State.ToString().ToLowerInvariant(),
                tick = experiment.World.CurrentTick,
                createdAt = DateTime.UtcNow
            };
            
            return CreatedAtAction(nameof(GetExperiment), new { expId = experiment.Id }, response);
        }

        /// <summary>
        /// Lists all active experiments, with an optional filter by state.
        /// </summary>
        /// <param name="state">Optional filter to return only experiments in a specific state (e.g., "running", "paused").</param>
        /// <returns>A list of experiment summaries.</returns>
        [HttpGet]
        public IActionResult ListExperiments([FromQuery] string? state)
        {
            SimulationState? stateFilter = null;
            if (!string.IsNullOrEmpty(state) && Enum.TryParse<SimulationState>(state, ignoreCase: true, out var parsedState))
            {
                stateFilter = parsedState;
            }

            var experiments = _manager.ListExperiments(stateFilter)
                .Select(exp => new
                {
                    id = exp.Id,
                    name = exp.Name,
                    state = exp.State.ToString().ToLowerInvariant(),
                    tick = exp.World.CurrentTick
                });

            return Ok(experiments);
        }

        /// <summary>
        /// Gets detailed information for a single experiment by its ID.
        /// </summary>
        /// <param name="expId">The unique identifier of the experiment.</param>
        /// <returns>The detailed state of the experiment, or 404 if not found.</returns>
        [HttpGet("{expId}")]
        public IActionResult GetExperiment(string expId)
        {
            var exp = _manager.GetExperiment(expId);
            if (exp == null)
            {
                return NotFound(new { error = "NotFound", message = $"Experiment with ID '{expId}' not found." });
            }

            return Ok(new
            {
                id = exp.Id,
                name = exp.Name,
                state = exp.State.ToString().ToLowerInvariant(),
                tick = exp.World.CurrentTick,
                seed = exp.Seed,
                config = exp.World.Config
            });
        }

        /// <summary>
        /// Stops and deletes an experiment, freeing all associated resources.
        /// </summary>
        /// <param name="expId">The unique identifier of the experiment to delete.</param>
        /// <returns>A 204 No Content response on success, or 404 if not found.</returns>
        [HttpDelete("{expId}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult DeleteExperiment(string expId)
        {
            if (_manager.DeleteExperiment(expId))
            {
                return NoContent();
            }
            
            return NotFound(new { error = "NotFound", message = $"Experiment with ID '{expId}' not found." });
        }
    }
}
// Hidra.API/Controllers/ExperimentsController.cs
using Hidra.API.DTOs;
using Hidra.API.Services;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System;
using Hidra.Core;
using System.Collections.Generic;

namespace Hidra.API.Controllers
{
    /// <summary>
    /// DTO for listing experiments with hierarchical context.
    /// </summary>
    public class ExperimentListItemDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "Standalone"; // "Standalone", "EvolutionRun", "GenerationOrganism"
        public string Activity { get; set; } = "Manual";
        public int? Generation { get; set; }
        public float? Fitness { get; set; }
        public int ChildrenCount { get; set; }
        public string State { get; set; } = "Unknown";
        public ulong Tick { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// DTO for updating experiment details (Renaming).
    /// </summary>
    public class UpdateExperimentRequestDto
    {
        public string? Name { get; set; }
    }

    [ApiController]
    [Route("api/experiments")]
    public class ExperimentsController : ControllerBase
    {
        private readonly ExperimentManager _manager;
        private readonly ExperimentRegistryService _registry;

        public ExperimentsController(ExperimentManager manager, ExperimentRegistryService registry)
        {
            _manager = manager;
            _registry = registry;
        }

        /// <summary>
        /// Creates a new experiment from scratch using an HGL genome.
        /// Registers the experiment in the Master Registry as a Standalone item.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult CreateExperiment([FromBody] CreateExperimentRequestDto body)
        {
            try
            {
                var experiment = _manager.CreateExperiment(body);

                // Register in the persistent index
                _registry.RegisterExperiment(new ExperimentMetadata
                {
                    Id = experiment.Id,
                    Name = experiment.Name,
                    Type = ExperimentType.Standalone,
                    ActivityType = experiment.ActivityType,
                    CreatedAt = DateTime.UtcNow
                });

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
            catch (Exception ex)
            {
                return BadRequest(new { error = "CreationFailed", message = ex.Message });
            }
        }

        /// <summary>
        /// Restores an experiment from a JSON snapshot.
        /// </summary>
        [HttpPost("restore")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult RestoreExperiment([FromBody] RestoreExperimentRequestDto body)
        {
            HidraWorld restoredWorld;
            try
            {
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
            
            var createRequest = new CreateExperimentRequestDto
            {
                Name = body.Name,
                Config = body.Config,
                HGLGenome = body.HGLGenome,
                IOConfig = body.IOConfig
            };

            var experiment = _manager.CreateExperimentFromWorld(createRequest, restoredWorld);

            // Register restoration
            _registry.RegisterExperiment(new ExperimentMetadata
            {
                Id = experiment.Id,
                Name = experiment.Name,
                Type = ExperimentType.Standalone,
                ActivityType = experiment.ActivityType,
                CreatedAt = DateTime.UtcNow
            });

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
        /// Clones an existing experiment starting from a specific tick.
        /// </summary>
        [HttpPost("{expId}/clone")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult CloneExperiment(string expId, [FromBody] CloneExperimentRequestDto body)
        {
            try
            {
                var newExperiment = _manager.CloneExperiment(expId, body);

                // Register clone
                _registry.RegisterExperiment(new ExperimentMetadata
                {
                    Id = newExperiment.Id,
                    Name = newExperiment.Name,
                    Type = ExperimentType.Standalone,
                    ActivityType = newExperiment.ActivityType,
                    CreatedAt = DateTime.UtcNow
                });

                var response = new
                {
                    id = newExperiment.Id,
                    name = newExperiment.Name,
                    state = newExperiment.State.ToString().ToLowerInvariant(),
                    tick = newExperiment.World.CurrentTick,
                    createdAt = DateTime.UtcNow
                };

                return CreatedAtAction(nameof(GetExperiment), new { expId = newExperiment.Id }, response);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = "NotFound", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = "Conflict", message = ex.Message });
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return BadRequest(new { error = "BadRequest", message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "InternalServerError", message = $"Cloning failed: {ex.Message}" });
            }
        }

        /// <summary>
        /// Updates an experiment's metadata (e.g., Name).
        /// </summary>
        [HttpPatch("{expId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult UpdateExperiment(string expId, [FromBody] UpdateExperimentRequestDto body)
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return BadRequest(new { error = "InvalidName", message = "Name cannot be empty." });

            // 1. Update in Memory/Disk Manager (handles .db metadata and active instance)
            // Note: You must ensure ExperimentManager has the RenameExperiment method implemented.
            if (!_manager.RenameExperiment(expId, body.Name))
            {
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found in manager." });
            }

            // 2. Update in Master Registry (handles UI list view)
            // Note: You must ensure ExperimentRegistryService has the UpdateName method implemented.
            _registry.UpdateName(expId, body.Name);

            return Ok(new { message = "Experiment updated.", name = body.Name });
        }

        /// <summary>
        /// Lists experiments with optional hierarchical filtering.
        /// Uses the Master Registry for structure and ExperimentManager for live status.
        /// </summary>
        /// <param name="parentId">If provided, lists children of this group. If null, lists root items.</param>
        [HttpGet]
        public IActionResult ListExperiments([FromQuery] string? parentId = null)
        {
            IEnumerable<ExperimentMetadata> items;
            
            // 1. Query Registry for hierarchy
            var allRegistryItems = _registry.GetAll().ToList();

            if (string.IsNullOrEmpty(parentId))
            {
                // Root items (Standalone or Evolution Groups)
                items = allRegistryItems.Where(x => string.IsNullOrEmpty(x.ParentGroupId));
            }
            else
            {
                // Children (Organisms within a Run)
                items = allRegistryItems.Where(x => x.ParentGroupId == parentId);
            }

            // 2. Map to DTOs, enriching with live data if available
            var dtos = items.Select(meta =>
            {
                // Check if currently loaded in memory
                var liveExp = _manager.GetExperiment(meta.Id);
                
                return new ExperimentListItemDto
                {
                    Id = meta.Id,
                    Name = meta.Name,
                    Type = meta.Type.ToString(),
                    Activity = meta.ActivityType,
                    Generation = meta.GenerationIndex,
                    Fitness = meta.FitnessScore,
                    // Count how many children point to this ID
                    ChildrenCount = allRegistryItems.Count(c => c.ParentGroupId == meta.Id),
                    CreatedAt = meta.CreatedAt,
                    // Live status or default based on Type
                    State = liveExp?.State.ToString() ?? (meta.Type == ExperimentType.EvolutionRun ? "Archive" : "Unloaded"),
                    Tick = liveExp?.World.CurrentTick ?? 0
                };
            });

            return Ok(dtos);
        }

        /// <summary>
        /// Gets detailed information for a single experiment by its ID.
        /// Supports Lazy Loading via the ExperimentManager.
        /// </summary>
        [HttpGet("{expId}")]
        public IActionResult GetExperiment(string expId)
        {
            // ExperimentManager.GetExperiment handles both memory lookup and disk-based lazy loading.
            var exp = _manager.GetExperiment(expId);

            if (exp == null)
            {
                // If it wasn't found in memory OR on disk (as a .db file), it truly doesn't exist in a runnable state.
                return NotFound(new { error = "NotFound", message = $"Experiment with ID '{expId}' not found or could not be loaded." });
            }

            return Ok(new
            {
                id = exp.Id,
                name = exp.Name,
                state = exp.State.ToString().ToLowerInvariant(),
                activity = exp.ActivityType,
                tick = exp.World.CurrentTick,
                seed = exp.Seed,
                config = exp.World.Config
            });
        }

        /// <summary>
        /// Stops and deletes an experiment, freeing all associated resources.
        /// </summary>
        [HttpDelete("{expId}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult DeleteExperiment(string expId)
        {
            // 1. Stop/Unload from memory and delete simulation DB files
            bool deletedFromDisk = _manager.DeleteExperiment(expId);
            
            // 2. Remove from Master Registry
            // Note: If this is a group, ExperimentRegistryService.DeleteExperiment handles recursive deletion of metadata entries.
            _registry.DeleteExperiment(expId);

            if (deletedFromDisk)
            {
                return NoContent();
            }
            
            // If it wasn't in the manager but was in registry, check registry success
            var meta = _registry.Get(expId);
            
            // If meta is null, it means it was successfully deleted (or never existed)
            if (meta == null)
            {
                return NoContent(); 
            }

            return NotFound(new { error = "NotFound", message = $"Experiment with ID '{expId}' could not be deleted." });
        }
    }
}
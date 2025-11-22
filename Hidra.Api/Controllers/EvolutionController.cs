// Hidra.API/Controllers/EvolutionController.cs
using Hidra.API.Evolution;
using Hidra.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hidra.API.Controllers
{
    [ApiController]
    [Route("api/evolution")]
    public class EvolutionController : ControllerBase
    {
        private readonly EvolutionService _evoService;

        public EvolutionController(EvolutionService evoService)
        {
            _evoService = evoService;
        }

        /// <summary>
        /// Starts a new evolutionary run. If one is already running, returns 409 Conflict.
        /// </summary>
        [HttpPost("start")]
        public IActionResult Start([FromBody] EvolutionRunConfig config)
        {
            try
            {
                _evoService.StartRun(config);
                return Ok(new { message = "Evolution run started." });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = "AlreadyRunning", message = ex.Message });
            }
        }

        /// <summary>
        /// Stops the current run.
        /// </summary>
        [HttpPost("stop")]
        public IActionResult Stop()
        {
            _evoService.StopRun();
            return Ok(new { message = "Evolution run stopped." });
        }

        /// <summary>
        /// Gets the current status, including progress and the history of fitness stats.
        /// </summary>
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            return Ok(_evoService.GetStatus());
        }

        /// <summary>
        /// Exports the current run history as a CSV file.
        /// </summary>
        [HttpGet("export/csv")]
        public IActionResult ExportCsv()
        {
            var status = _evoService.GetStatus();
            if (status.History == null || status.History.Count == 0)
            {
                return BadRequest("No evolution history available to export.");
            }

            var csv = new System.Text.StringBuilder();
            // Header
            csv.AppendLine("Generation,MaxFitness,AvgFitness,MinFitness,BestGenomeBytes");

            // Rows
            foreach (var gen in status.History)
            {
                // Calculate approximate genome byte length (Hex length / 2)
                int byteLen = (gen.BestGenomeHex?.Length ?? 0) / 2;
                
                csv.AppendLine($"{gen.GenerationIndex},{gen.MaxFitness},{gen.AvgFitness},{gen.MinFitness},{byteLen}");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
            return File(bytes, "text/csv", $"hidra_export_{System.DateTime.UtcNow:yyyyMMddHHmm}.csv");
        }

        /// <summary>
        /// Creates a new standard Experiment from the best organism of a specific generation.
        /// This allows the user to load that organism into the visualizer.
        /// </summary>
        [HttpPost("load-generation/{genIndex}")]
        public IActionResult LoadGeneration(int genIndex)
        {
            try
            {
                string experimentId = _evoService.CreateExperimentFromGeneration(genIndex);
                return Ok(new 
                { 
                    message = "Experiment created from generation.", 
                    experimentId = experimentId 
                });
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { error = "NotFound", message = ex.Message });
            }
        }
    }
}
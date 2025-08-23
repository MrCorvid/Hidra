using Hidra.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hidra.API.Controllers
{
    /// <summary>
    /// Provides endpoints for retrieving performance and state metrics
    /// from a specific experiment's world.
    /// </summary>
    [ApiController]
    [Route("api/experiments/{expId}/metrics")]
    public class MetricsController : ControllerBase
    {
        private readonly ExperimentManager _manager;

        // The dependency is now on ExperimentManager.
        public MetricsController(ExperimentManager manager)
        {
            _manager = manager;
        }

        [HttpGet("latest")]
        public IActionResult GetLatestMetrics(string expId)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) 
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            // This read operation is thread-safe as per Hidra.Core implementation.
            var metrics = experiment.World.GetTickMetrics();
            return Ok(metrics);
        }

        [HttpGet("timeseries")]
        public IActionResult GetMetricsTimeseries(string expId, [FromQuery] int maxCount = 256)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) 
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            // This read operation is thread-safe.
            var history = experiment.World.GetRecentSnapshots(maxCount);
            return Ok(history);
        }
        
        [HttpGet("timeseries/csv")]
        public IActionResult GetMetricsTimeseriesAsCsv(string expId, [FromQuery] int maxCount = 4096)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) 
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            // This read operation is thread-safe.
            string csv = experiment.World.ExportRecentTickMetricsCsv(maxCount);
            return Content(csv, "text/csv", System.Text.Encoding.UTF8);
        }
    }
}
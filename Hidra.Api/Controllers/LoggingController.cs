// In Hidra.API/Controllers/LoggingController.cs
using Hidra.API.Services;
// Note: Hidra.Core.Logging is still used, but we will be more specific below.
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Hidra.API.Controllers
{
    /// <summary>
    /// Data Transfer Object for setting the log level.
    /// </summary>
    public class SetLogLevelRequestDto
    {
        /// <summary>
        /// The minimum log level to capture for the experiment.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        // FIX: Explicitly use the custom LogLevel enum to resolve ambiguity.
        public Hidra.Core.Logging.LogLevel MinimumLevel { get; set; }
    }

    /// <summary>
    /// Provides endpoints for controlling logging behavior for an experiment.
    /// </summary>
    [ApiController]
    [Route("api/experiments/{expId}/logging")]
    public class LoggingController : ControllerBase
    {
        private readonly ExperimentManager _manager;

        public LoggingController(ExperimentManager manager)
        {
            _manager = manager;
        }

        /// <summary>
        /// Sets the minimum log level for an experiment.
        /// </summary>
        /// <remarks>
        /// Setting the level to 'Trace' will enable instruction-level logging, which is very verbose.
        /// </remarks>
        /// <param name="expId">The ID of the experiment to configure.</param>
        /// <param name="request">A DTO containing the desired minimum log level.</param>
        [HttpPut("level")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult SetMinimumLogLevel(string expId, [FromBody] SetLogLevelRequestDto request)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null)
            {
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            }

            experiment.SetMinimumLogLevel(request.MinimumLevel);

            return Ok(new { message = $"Minimum log level for experiment '{expId}' set to {request.MinimumLevel}." });
        }
    }
}
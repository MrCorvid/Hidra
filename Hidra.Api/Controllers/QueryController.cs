// In Hidra.API/Controllers/QueryController.cs
using Microsoft.AspNetCore.Mvc;
using Hidra.API.Services;
using Hidra.Core;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Hidra.Core.Logging;
using System;
using Newtonsoft.Json;

namespace Hidra.API.Controllers
{
    /// <summary>
    /// Provides endpoints for querying the state of a specific experiment's world, including logs.
    /// </summary>
    [ApiController]
    [Route("api/experiments/{expId}/query")]
    public class QueryController : ControllerBase
    {
        private readonly ExperimentManager _manager;
        
        public QueryController(ExperimentManager manager)
        {
            _manager = manager;
        }

        private Experiment? GetExperiment(string expId)
        {
            return _manager.GetExperiment(expId);
        }

        [HttpGet("status")]
        public IActionResult GetStatus(string expId)
        {
            var experiment = GetExperiment(expId);
            if (experiment == null) 
            {
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            }
            
            var world = experiment.World;
            return Ok(new
            {
                ExperimentId = experiment.Id,
                ExperimentName = experiment.Name,
                State = experiment.State.ToString(),
                world.CurrentTick,
                NeuronCount = world.Neurons.Count,
                SynapseCount = world.Synapses.Count,
                InputNodeCount = world.InputNodes.Count,
                OutputNodeCount = world.OutputNodes.Count
            });
        }

        [HttpGet("neurons/{id}")]
        public IActionResult GetNeuronById(string expId, ulong id)
        {
            var world = GetExperiment(expId)?.World;
            if (world == null)
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            var neuron = world.GetNeuronById(id);
            if (neuron == null)
                return NotFound(new { error = "NotFound", message = $"Neuron with ID {id} not found in experiment '{expId}'." });

            return Ok(neuron);
        }

        [HttpGet("neurons")]
        public IActionResult GetNeurons(string expId, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
        {
            var world = GetExperiment(expId)?.World;
            if (world == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            var neurons = world.Neurons.Values
                                .Skip((page - 1) * pageSize)
                                .Take(pageSize)
                                .ToList();
            return Ok(neurons);
        }
        
        [HttpGet("neighbors")]
        public IActionResult GetNeighbors(string expId, [FromQuery] ulong centerId, [FromQuery] float radius)
        {
            var experiment = GetExperiment(expId);
            if (experiment == null) 
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            
            var world = experiment.World;
            var centerNeuron = world.GetNeuronById(centerId);
            if (centerNeuron == null)
                return NotFound(new { error = "NotFound", message = $"Center neuron with ID {centerId} not found." });
            
            lock (experiment.GetLockObject())
            {
                var neighbors = world.GetNeighbors(centerNeuron, radius).ToList();
                return Ok(neighbors);
            }
        }
        
        [HttpGet("synapses/{id}")]
        public IActionResult GetSynapseById(string expId, ulong id)
        {
            var world = GetExperiment(expId)?.World;
            if (world == null)
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            var syn = world.GetSynapseById(id);
            if (syn == null)
                return NotFound(new { error = "NotFound", message = $"Synapse with ID {id} not found in experiment '{expId}'." });

            return Ok(syn);
        }

        [HttpGet("synapses")]
        public IActionResult GetSynapses(string expId, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
        {
            var world = GetExperiment(expId)?.World;
            if (world == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            var synapses = world.Synapses
                                .Skip((page - 1) * pageSize)
                                .Take(pageSize);
            return Ok(synapses);
        }

        [HttpGet("events")]
        public IActionResult GetEventsForTick(string expId, [FromQuery] ulong tick)
        {
            var world = GetExperiment(expId)?.World;
            if (world == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            
            var events = world.GetEventsForTick(tick);
            return Ok(events);
        }

        [HttpGet("inputs")]
        public IActionResult GetInputNodes(string expId)
        {
            var world = GetExperiment(expId)?.World;
            if (world == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            
            return Ok(world.InputNodes.Values);
        }

        [HttpGet("outputs")]
        public IActionResult GetOutputNodes(string expId)
        {
            var world = GetExperiment(expId)?.World;
            if (world == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            return Ok(world.OutputNodes.Values);
        }

        [HttpGet("hormones")]
        public IActionResult GetGlobalHormones(string expId)
        {
            var world = GetExperiment(expId)?.World;
            if (world == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            
            var gvars = world.GetGlobalHormones();
            return Ok(gvars);
        }

        #region Log Endpoints

        [HttpGet("logs")]
        public IActionResult GetExperimentLogs(
            string expId,
            [FromQuery] string? level = null,
            [FromQuery] string? tag = null)
        {
            var experiment = GetExperiment(expId);
            if (experiment == null) 
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            IEnumerable<LogEntry> logs;
            lock (experiment.LogHistory)
            {
                logs = experiment.LogHistory.ToList();
            }

            if (!string.IsNullOrWhiteSpace(level) && Enum.TryParse<Hidra.Core.Logging.LogLevel>(level, true, out var minLevel))
            {
                logs = logs.Where(e => e.Level >= minLevel);
            }

            if (!string.IsNullOrWhiteSpace(tag))
            {
                logs = logs.Where(e => e.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));
            }

            return Ok(logs);
        }

        [HttpGet("logs/text")]
        public IActionResult GetExperimentLogsAsText(
            string expId,
            [FromQuery] string? level = null,
            [FromQuery] string? tag = null)
        {
            var experiment = GetExperiment(expId);
            if (experiment == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            
            IEnumerable<LogEntry> logs;
            lock (experiment.LogHistory) { logs = experiment.LogHistory.ToList(); }
            
            if (!string.IsNullOrWhiteSpace(level) && Enum.TryParse<Hidra.Core.Logging.LogLevel>(level, true, out var minLevel)) 
            { 
                logs = logs.Where(e => e.Level >= minLevel); 
            }
            if (!string.IsNullOrWhiteSpace(tag)) { logs = logs.Where(e => e.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)); }

            var sb = new StringBuilder();
            foreach (var entry in logs)
            {
                sb.AppendLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level,-7}] [{entry.Tag}] {entry.Message}");
            }

            return Content(sb.ToString(), "text/plain", Encoding.UTF8);
        }

        #endregion
    }
}
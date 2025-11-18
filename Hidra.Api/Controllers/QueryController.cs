// In Hidra.API/Controllers/QueryController.cs
using Microsoft.AspNetCore.Mvc;
using Hidra.API.Services;
using Hidra.Core;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Hidra.Core.Logging;
using System;
using Hidra.API.DTOs;
using Hidra.Core.Brain;
using System.IO;
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
        private readonly string _baseStoragePath;

        public QueryController(ExperimentManager manager)
        {
            _manager = manager;
            // Get the same root storage path the manager uses to find experiment data
            _baseStoragePath = Path.Combine(Directory.GetCurrentDirectory(), "_experiments");
        }

        private Experiment? GetExperiment(string expId)
        {
            return _manager.GetExperiment(expId);
        }

        /// <summary>
        /// Retrieves the complete history of an experiment by reading its persisted state from disk.
        /// Each frame includes a visualization snapshot and the events for that tick.
        /// </summary>
        [HttpGet("history")]
        [ProducesResponseType(typeof(List<ReplayFrameDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetFullHistory(string expId)
        {
            var experiment = GetExperiment(expId);
            if (experiment == null)
            {
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            }

            var experimentPath = Path.Combine(_baseStoragePath, expId);
            if (!Directory.Exists(experimentPath))
            {
                // This case can happen if an experiment is created but somehow fails before the first save.
                return Ok(new List<ReplayFrameDto>());
            }
            
            var history = new List<ReplayFrameDto>();

            // Find all tick files, sort them numerically by name to ensure correct chronological order.
            var tickFiles = Directory.GetFiles(experimentPath, "tick_*.json")
                                     .OrderBy(f => f)
                                     .ToList();

            foreach (var filePath in tickFiles)
            {
                try
                {
                    var json = System.IO.File.ReadAllText(filePath);
                    var worldState = JsonConvert.DeserializeObject<FullWorldState>(json);

                    if (worldState != null)
                    {
                        var frame = new ReplayFrameDto
                        {
                            Tick = worldState.CurrentTick,
                            // GetEventsForTick still reads from the live world's memory, which is correct
                            // as event history is kept in memory.
                            Events = experiment.World.GetEventsForTick(worldState.CurrentTick), 
                            Snapshot = MapWorldStateToVisualizationDto(worldState)
                        };
                        history.Add(frame);
                    }
                }
                catch (Exception ex)
                {
                    // Log an error if a specific file is corrupt, but continue processing others.
                    Console.WriteLine($"Error processing history file {filePath} for experiment {expId}: {ex.Message}");
                }
            }
            
            return Ok(history);
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

        [HttpGet("visualize")]
        public IActionResult GetVisualizationSnapshot(string expId)
        {
            var world = GetExperiment(expId)?.World;
            if (world == null)
            {
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            }

            var worldState = world.GetFullWorldState();
            var snapshotDto = MapWorldStateToVisualizationDto(worldState);
            return Ok(snapshotDto);
        }
        
        #region Private Controller Helpers for DTO Mapping

        private VisualizationSnapshotDto MapWorldStateToVisualizationDto(FullWorldState worldState)
        {
            return new VisualizationSnapshotDto
            {
                ExperimentId = worldState.ExperimentId,
                CurrentTick = worldState.CurrentTick,
                InputNodeIds = worldState.InputNodes.Select(n => n.Id).ToList(),
                OutputNodeIds = worldState.OutputNodes.Select(n => n.Id).ToList(),
                Neurons = worldState.Neurons.Select(n => new VisualizationNeuronDto
                {
                    Id = n.Id,
                    Position = new Vector3f(n.Position.X, n.Position.Y, n.Position.Z),
                    Brain = BuildVisualizationBrainDto(n.Brain)
                }).ToList(),
                Synapses = worldState.Synapses.Select(s => new VisualizationSynapseDto
                {
                    Id = s.Id,
                    SourceId = s.SourceId,
                    TargetId = s.TargetId,
                    SignalType = s.SignalType,
                    Weight = s.Weight,
                    Parameter = s.Parameter,
                    FatigueRate = s.FatigueRate,
                    FatigueRecoveryRate = s.FatigueRecoveryRate
                }).ToList(),
                InputNodeValues = worldState.InputNodes.ToDictionary(n => n.Id, n => n.Value),
                OutputNodeValues = worldState.OutputNodes.ToDictionary(n => n.Id, n => n.Value),
                NeuronStates = worldState.Neurons.ToDictionary(n => n.Id, n => new VisualizationNeuronStateDto
                {
                    IsActive = n.IsActive,
                    LocalVariables = Enum.GetValues(typeof(LVarIndex))
                                         .Cast<LVarIndex>()
                                         .ToDictionary(lvar => lvar.ToString(), lvar => n.LocalVariables[(int)lvar]),
                    BrainNodeValues = GetBrainNodeValues(n.Brain)
                }),
                SynapseStates = worldState.Synapses.ToDictionary(s => s.Id, s => new VisualizationSynapseStateDto
                {
                    IsActive = s.IsActive,
                    FatigueLevel = s.FatigueLevel
                })
            };
        }

        private VisualizationBrainDto BuildVisualizationBrainDto(IBrain brain)
        {
            switch (brain)
            {
                case NeuralNetworkBrain nnBrain:
                    var network = nnBrain.GetInternalNetwork();
                    return new VisualizationBrainDto
                    {
                        Type = "NeuralNetworkBrain",
                        Data = new NNBrainDataDto
                        {
                            Nodes = network.Nodes.Values.Select(n => new NNNodeDataDto
                            {
                                Id = n.Id,
                                NodeType = n.NodeType,
                                Bias = n.Bias,
                                ActivationFunction = n.ActivationFunction,
                                InputSource = n.InputSource,
                                SourceIndex = n.SourceIndex,
                                ActionType = n.ActionType
                            }).ToList(),
                            Connections = network.Connections.Select(c => new NNConnectionDataDto
                            {
                                FromNodeId = c.FromNodeId,
                                ToNodeId = c.ToNodeId,
                                Weight = c.Weight
                            }).ToList()
                        }
                    };
                    
                case LogicGateBrain lgBrain:
                    return new VisualizationBrainDto
                    {
                        Type = "LogicGateBrain",
                        Data = new LGBrainDataDto
                        {
                            GateType = (int)lgBrain.GateType, // Explicitly cast the enum to an int
                            FlipFlop = lgBrain.FlipFlop,
                            Threshold = lgBrain.Threshold
                        }
                    };

                default: // Includes DummyBrain
                    return new VisualizationBrainDto { Type = "DummyBrain", Data = new object() };
            }
        }

        private Dictionary<int, float>? GetBrainNodeValues(IBrain brain)
        {
            if (brain is NeuralNetworkBrain nnBrain)
            {
                var network = nnBrain.GetInternalNetwork();
                return network.Nodes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value);
            }
            return null;
        }
        #endregion

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
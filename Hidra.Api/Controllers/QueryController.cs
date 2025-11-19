// Hidra.API/Controllers/QueryController.cs
using Microsoft.AspNetCore.Mvc;
using Hidra.API.Services;
using Hidra.Core;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System;
using Hidra.API.DTOs;
using Hidra.Core.Brain;
using Newtonsoft.Json;

// Explicit alias to resolve ambiguity between Hidra.Core.Logging and Microsoft.Extensions.Logging
using LogLevel = Hidra.Core.Logging.LogLevel;

namespace Hidra.API.Controllers
{
    /// <summary>
    /// Provides endpoints for querying the state of a specific experiment's world, including logs,
    /// history replay, and specific entity details.
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

        private Experiment? GetExperiment(string expId) => _manager.GetExperiment(expId);

        /// <summary>
        /// Retrieves experiment logs as structured JSON objects from the SQLite database.
        /// </summary>
        [HttpGet("logs")]
        public IActionResult GetExperimentLogs(string expId, [FromQuery] string? level = null, [FromQuery] string? tag = null)
        {
            var experiment = GetExperiment(expId);
            if (experiment == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            // Retrieve from SQLite (limit 2000 for performance)
            var logs = experiment.Db.ReadLogs(2000); 

            if (!string.IsNullOrWhiteSpace(level) && Enum.TryParse<LogLevel>(level, true, out var minLevel))
            {
                logs = logs.Where(e => e.Level >= minLevel).ToList();
            }

            if (!string.IsNullOrWhiteSpace(tag))
            {
                logs = logs.Where(e => e.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return Ok(logs);
        }
        
        /// <summary>
        /// Retrieves experiment logs as a single plain text string from the SQLite database.
        /// </summary>
        [HttpGet("logs/text")]
        public IActionResult GetExperimentLogsAsText(string expId, [FromQuery] string? level = null, [FromQuery] string? tag = null)
        {
            var experiment = GetExperiment(expId);
            if (experiment == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            
            var logs = experiment.Db.ReadLogs(2000); 
            
            if (!string.IsNullOrWhiteSpace(level) && Enum.TryParse<LogLevel>(level, true, out var minLevel)) 
            { 
                logs = logs.Where(e => e.Level >= minLevel).ToList(); 
            }
            if (!string.IsNullOrWhiteSpace(tag)) 
            { 
                logs = logs.Where(e => e.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList(); 
            }

            var sb = new StringBuilder();
            foreach (var entry in logs)
            {
                sb.AppendLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level,-7}] [{entry.Tag}] {entry.Message}");
            }

            return Content(sb.ToString(), "text/plain", Encoding.UTF8);
        }

        /// <summary>
        /// Retrieves the complete history of the experiment by deserializing compressed snapshots from the database.
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

            // Retrieve the genome from metadata. This is critical for ensuring the temporary world
            // used for DTO mapping has the correct genes compiled.
            string experimentGenome = experiment.Db.GetMetadata("Genome") ?? "";

            var history = new List<ReplayFrameDto>();
            
            // 1. Load raw compressed JSON from SQLite
            var snapshots = experiment.Db.LoadAllSnapshots();

            // Settings for the outer wrapper (PersistedTick)
            var wrapperSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                PreserveReferencesHandling = PreserveReferencesHandling.Objects
            };

            foreach (var (tick, json) in snapshots)
            {
                try
                {
                    // 2. Deserialize the wrapper to get the raw internal world string and the events
                    var persistedTick = JsonConvert.DeserializeObject<PersistedTick>(json, wrapperSettings);
                    
                    if (persistedTick != null && !string.IsNullOrEmpty(persistedTick.WorldStateJson))
                    {
                        // 3. Rehydrate a temporary HidraWorld object from the internal JSON string.
                        // We pass the fetched experimentGenome here.
                        // We pass dummy config/IO here because we only need the object structure 
                        // to map it to the Visualization DTO.
                        var tempWorld = HidraWorld.LoadStateFromJson(
                            persistedTick.WorldStateJson, 
                            experimentGenome,   // Ensure genes are loaded
                            new HidraConfig(),  // Dummy config
                            new List<ulong>(),  // No IO mapping needed
                            new List<ulong>()   // No IO mapping needed
                        );

                        if (tempWorld != null)
                        {
                            // 4. Convert the domain object to the API DTO
                            var fullState = tempWorld.GetFullWorldState();
                            
                            var frame = new ReplayFrameDto
                            {
                                Tick = fullState.CurrentTick,
                                Events = persistedTick.Events,
                                Snapshot = MapWorldStateToVisualizationDto(fullState)
                            };
                            history.Add(frame);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] GetFullHistory: Failed to deserialize frame for tick {tick}. Error: {ex.Message}");
                }
            }

            return Ok(history);
        }

        /// <summary>
        /// Gets the current live status of the experiment.
        /// </summary>
        [HttpGet("status")]
        public IActionResult GetStatus(string expId)
        {
            var experiment = GetExperiment(expId);
            if (experiment == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            
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

        #region Entity Getters

        [HttpGet("neurons/{id}")]
        public IActionResult GetNeuronById(string expId, ulong id)
        {
            var world = GetExperiment(expId)?.World;
            if (world == null) return NotFound();
            var neuron = world.GetNeuronById(id);
            return neuron != null ? Ok(neuron) : NotFound();
        }
        
        [HttpGet("neurons")]
        public IActionResult GetNeurons(string expId, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
        {
            var world = GetExperiment(expId)?.World;
            if (world == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            var neurons = world.Neurons.Values.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Ok(neurons);
        }
        
        [HttpGet("neighbors")]
        public IActionResult GetNeighbors(string expId, [FromQuery] ulong centerId, [FromQuery] float radius)
        {
            var experiment = GetExperiment(expId);
            if (experiment == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            
            var world = experiment.World;
            var centerNeuron = world.GetNeuronById(centerId);
            if (centerNeuron == null) return NotFound(new { error = "NotFound", message = $"Center neuron with ID {centerId} not found." });
            
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
            if (world == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            var syn = world.GetSynapseById(id);
            return syn != null ? Ok(syn) : NotFound(new { error = "NotFound", message = $"Synapse with ID {id} not found." });
        }

        [HttpGet("synapses")]
        public IActionResult GetSynapses(string expId, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
        {
            var world = GetExperiment(expId)?.World;
            if (world == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            var synapses = world.Synapses.Skip((page - 1) * pageSize).Take(pageSize);
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
            return world != null ? Ok(world.InputNodes.Values) : NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
        }

        [HttpGet("outputs")]
        public IActionResult GetOutputNodes(string expId)
        {
            var world = GetExperiment(expId)?.World;
            return world != null ? Ok(world.OutputNodes.Values) : NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
        }

        [HttpGet("hormones")]
        public IActionResult GetGlobalHormones(string expId)
        {
            var world = GetExperiment(expId)?.World;
            return world != null ? Ok(world.GetGlobalHormones()) : NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
        }

        #endregion

        #region DTO Mapping Helpers

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
                            GateType = (int)lgBrain.GateType, 
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
    }
}
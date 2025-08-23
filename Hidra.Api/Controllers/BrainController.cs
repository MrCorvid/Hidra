// In Hidra.API/Controllers/BrainController.cs
using Hidra.API.DTOs;
using Hidra.API.Services;
using Hidra.Core.Brain;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using System.Text;

namespace Hidra.API.Controllers
{
    /// <summary>
    /// Provides endpoints for manipulating the internal "brain" of a single neuron
    /// within a specific experiment.
    /// </summary>
    [ApiController]
    [Route("api/experiments/{expId}/neurons/{neuronId}/brain")]
    public class BrainController : ControllerBase
    {
        private readonly ExperimentManager _manager;
        
        public BrainController(ExperimentManager manager)
        {
            _manager = manager;
        }

        private NeuralNetwork? GetNeuralNetwork(string expId, ulong neuronId)
        {
            var neuron = _manager.GetExperiment(expId)?.World.GetNeuronById(neuronId);
            return (neuron?.Brain as NeuralNetworkBrain)?.GetInternalNetwork();
        }

        [HttpPost("type")]
        public IActionResult SetBrainType(string expId, ulong neuronId, [FromBody] SetBrainTypeRequestDto body)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            lock (experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot modify brain while simulation is running." });
                
                var neuron = experiment.World.GetNeuronById(neuronId);
                if (neuron == null) 
                    return NotFound(new { error = "NotFound", message = $"Neuron with ID {neuronId} not found in experiment '{expId}'." });

                if (body.Type.Equals("NeuralNetwork", StringComparison.OrdinalIgnoreCase))
                {
                    neuron.Brain = new NeuralNetworkBrain();
                }
                else if (body.Type.Equals("LogicGate", StringComparison.OrdinalIgnoreCase))
                {
                    var lgBrain = new LogicGateBrain
                    {
                        GateType = body.GateType ?? LogicGateType.AND,
                        FlipFlop = body.FlipFlop,
                        Threshold = body.Threshold
                    };
                    neuron.Brain = lgBrain;
                }
                else
                {
                    return BadRequest("Invalid brain type specified. Must be 'NeuralNetwork' or 'LogicGate'.");
                }
            }
            return Ok(new { message = $"Brain for neuron {neuronId} in experiment '{expId}' set to {body.Type}."});
        }
        
        [HttpPost("construct")]
        public IActionResult ConstructBrain(string expId, ulong neuronId, [FromBody] ConstructBrainRequestDto body)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            
            var neuron = experiment.World.GetNeuronById(neuronId);
            if (neuron == null) return NotFound(new { error = "NotFound", message = $"Neuron with ID {neuronId} not found." });
            
            lock(experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot modify brain while simulation is running." });

                if (neuron.Brain is not NeuralNetworkBrain)
                {
                    neuron.Brain = new NeuralNetworkBrain();
                }
                var nn = (neuron.Brain as NeuralNetworkBrain)!.GetInternalNetwork();
                nn.Clear();

                var rng = new Random();
                
                switch(body.Type.ToLowerInvariant())
                {
                    case "simplefeedforward":
                    {
                        var numInputs = body.NumInputs ?? 2;
                        var numOutputs = body.NumOutputs ?? 1;
                        var numHiddenLayers = body.NumHiddenLayers ?? 1;
                        var nodesPerLayer = body.NodesPerLayer ?? 3;
                        var totalHiddenNodes = numHiddenLayers * nodesPerLayer;
                        
                        var inputIds = new List<int>();
                        var hiddenIds = new List<int>();
                        var outputIds = new List<int>();
                        int nextId = 0;

                        for (int i = 0; i < numInputs; i++) { inputIds.Add(nextId); nn.AddNode(new NNNode(nextId++, NNNodeType.Input)); }
                        for (int i = 0; i < totalHiddenNodes; i++) { hiddenIds.Add(nextId); nn.AddNode(new NNNode(nextId++, NNNodeType.Hidden) { Bias = (float)(rng.NextDouble() * 2.0 - 1.0) }); }
                        for (int i = 0; i < numOutputs; i++) { outputIds.Add(nextId); nn.AddNode(new NNNode(nextId++, NNNodeType.Output) { Bias = (float)(rng.NextDouble() * 2.0 - 1.0) }); }

                        if (hiddenIds.Any())
                        {
                            foreach (var iId in inputIds) foreach (var hId in hiddenIds) nn.AddConnection(new NNConnection(iId, hId, (float)(rng.NextDouble() * 2.0 - 1.0)));
                            foreach (var hId in hiddenIds) foreach (var oId in outputIds) nn.AddConnection(new NNConnection(hId, oId, (float)(rng.NextDouble() * 2.0 - 1.0)));
                        }
                        else
                        {
                            foreach (var iId in inputIds) foreach (var oId in outputIds) nn.AddConnection(new NNConnection(iId, oId, (float)(rng.NextDouble() * 2.0 - 1.0)));
                        }
                        break;
                    }
                    case "competitive":
                    {
                        var numInputs = body.NumInputs ?? 2;
                        var numCompetitors = body.NumCompetitors ?? 4;

                        var inputIds = new List<int>();
                        var competitiveIds = new List<int>();
                        int nextId = 0;

                        for (int i = 0; i < numInputs; i++) { inputIds.Add(nextId); nn.AddNode(new NNNode(nextId++, NNNodeType.Input)); }
                        for (int i = 0; i < numCompetitors; i++) { competitiveIds.Add(nextId); nn.AddNode(new NNNode(nextId++, NNNodeType.Hidden) { Bias = (float)(rng.NextDouble() * 2.0 - 1.0) }); }
                        
                        foreach (var iId in inputIds) foreach (var cId in competitiveIds) nn.AddConnection(new NNConnection(iId, cId, (float)rng.NextDouble()));
                        foreach (var fromId in competitiveIds) foreach (var toId in competitiveIds) if (fromId != toId) nn.AddConnection(new NNConnection(fromId, toId, -1.0f));
                        break;
                    }
                    default:
                        return BadRequest(new { error = "BadRequest", message = $"Unknown brain constructor type: '{body.Type}'" });
                }
            }
            return Ok(new { message = $"Brain for neuron {neuronId} constructed with type '{body.Type}'." });
        }

        [HttpPost("clear")]
        public IActionResult ClearBrain(string expId, ulong neuronId)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            lock (experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot modify brain while simulation is running." });

                var nn = GetNeuralNetwork(expId, neuronId);
                if (nn == null) return BadRequest("Target neuron does not have a NeuralNetworkBrain.");
                
                nn.Clear();
            }
            return Ok(new { message = "Brain cleared." });
        }

        [HttpPost("nodes")]
        public IActionResult AddNode(string expId, ulong neuronId, [FromBody] AddBrainNodeRequestDto body)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            NNNode? node;
            lock (experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot modify brain while simulation is running." });

                var nn = GetNeuralNetwork(expId, neuronId);
                if (nn == null) return BadRequest("Target neuron does not have a NeuralNetworkBrain.");
                
                var nodeId = nn.Nodes.Count > 0 ? nn.Nodes.Keys.Max() + 1 : 0;
                node = new NNNode(nodeId, body.NodeType) { Bias = body.Bias };
                nn.AddNode(node);
            }
            
            return Ok(node);
        }

        [HttpDelete("nodes/{nodeId}")]
        public IActionResult DeleteNode(string expId, ulong neuronId, int nodeId)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            
            lock (experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot modify brain while simulation is running." });

                var nn = GetNeuralNetwork(expId, neuronId);
                if (nn == null) return BadRequest("Target neuron does not have a NeuralNetworkBrain.");

                if (!nn.Nodes.ContainsKey(nodeId))
                    return NotFound(new { error = "NotFound", message = $"Node with ID {nodeId} not found in brain." });
                
                nn.RemoveNode(nodeId);
            }
            return NoContent();
        }

        [HttpPost("connections")]
        public IActionResult AddConnection(string expId, ulong neuronId, [FromBody] AddBrainConnectionRequestDto body)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            
            NNConnection conn;
            lock (experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot modify brain while simulation is running." });

                var nn = GetNeuralNetwork(expId, neuronId);
                if (nn == null) return BadRequest("Target neuron does not have a NeuralNetworkBrain.");
                
                conn = new NNConnection(body.FromNodeId, body.ToNodeId, body.Weight);
                if (!nn.AddConnection(conn))
                {
                     return Conflict(new { error = "Conflict", message = $"Adding connection from {body.FromNodeId} to {body.ToNodeId} would create a cycle." });
                }
            }
            return Ok(conn);
        }

        [HttpDelete("connections")]
        public IActionResult DeleteConnection(string expId, ulong neuronId, [FromQuery] int fromNodeId, [FromQuery] int toNodeId)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            lock (experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot modify brain while simulation is running." });
                
                var nn = GetNeuralNetwork(expId, neuronId);
                if (nn == null) return BadRequest("Target neuron does not have a NeuralNetworkBrain.");

                nn.RemoveConnection(fromNodeId, toNodeId);
            }
            return NoContent();
        }

        [HttpPatch("nodes/{nodeId}")]
        public IActionResult ConfigureNode(string expId, ulong neuronId, int nodeId, [FromBody] ConfigureBrainNodeRequestDto body)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            NNNode? node;
            lock (experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot modify brain while simulation is running." });

                var nn = GetNeuralNetwork(expId, neuronId);
                if (nn == null) return BadRequest("Target neuron does not have a NeuralNetworkBrain.");

                if (!nn.Nodes.TryGetValue(nodeId, out node))
                {
                    return NotFound($"Node with ID {nodeId} not found in brain.");
                }

                if (body.Bias.HasValue) node.Bias = body.Bias.Value;
                if (body.ActivationFunction.HasValue) node.ActivationFunction = body.ActivationFunction.Value;
                if (body.ActionType.HasValue) node.ActionType = body.ActionType.Value;
                if (body.InputSource.HasValue) node.InputSource = body.InputSource.Value;
                if (body.SourceIndex.HasValue) node.SourceIndex = body.SourceIndex.Value;
            }
            return Ok(node);
        }
    }
}
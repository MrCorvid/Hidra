// Hidra.API/Controllers/ManipulationController.cs

using Microsoft.AspNetCore.Mvc;
using Hidra.API.Services;
using System.Collections.Generic;
using Hidra.API.DTOs;
using System.Numerics;
using Hidra.Core;
using System;

namespace Hidra.API.Controllers
{
    /// <summary>
    /// Provides endpoints for direct manipulation of an experiment's world graph
    /// (e.g., creating/deleting neurons/synapses, and setting I/O values).
    /// </summary>
    [ApiController]
    [Route("api/experiments/{expId}/manipulate")]
    public class ManipulationController : ControllerBase
    {
        private readonly ExperimentManager _manager;

        public ManipulationController(ExperimentManager manager)
        {
            _manager = manager;
        }

        #region I/O and Hormone Manipulation

        [HttpPut("inputs")]
        public IActionResult SetInputValues(string expId, [FromBody] Dictionary<ulong, float> values)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) 
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            lock (experiment.GetLockObject())
            {
                experiment.World.SetInputValues(values);
            }

            return Ok(new { message = $"Input values updated for experiment '{expId}'." });
        }
        
        [HttpPatch("hormones")]
        public IActionResult SetHormones(string expId, [FromBody] Dictionary<int, float> values)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) 
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            lock (experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot set hormones while simulation is running. Pause first." });

                experiment.World.SetGlobalHormones(values);
            }
            
            return Ok(new { message = "Global hormone values updated." });
        }

        [HttpPost("inputs")]
        public IActionResult AddInputNode(string expId, [FromBody] AddIoNodeRequestDto body)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) 
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            lock (experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot add input node while simulation is running. Pause first." });

                experiment.World.AddInputNode(body.Id, body.InitialValue);
            }
            return Ok(new { message = $"Input node {body.Id} added to experiment '{expId}'." });
        }

        [HttpPost("outputs")]
        public IActionResult AddOutputNode(string expId, [FromBody] AddIoNodeRequestDto body)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) 
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            lock (experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot add output node while simulation is running. Pause first." });

                experiment.World.AddOutputNode(body.Id);
            }
            return Ok(new { message = $"Output node {body.Id} added to experiment '{expId}'." });
        }
        
        [HttpDelete("inputs/{id}")]
        public IActionResult DeleteInputNode(string expId, ulong id)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) 
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            
            bool success;
            lock(experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot delete input node while simulation is running. Pause first." });

                success = experiment.World.RemoveInputNode(id);
            }
            
            if (!success)
                return NotFound(new { error = "NotFound", message = $"Input node with ID {id} not found." });

            return NoContent();
        }
        
        [HttpDelete("outputs/{id}")]
        public IActionResult DeleteOutputNode(string expId, ulong id)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) 
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            
            bool success;
            lock(experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot delete output node while simulation is running. Pause first." });

                success = experiment.World.RemoveOutputNode(id);
            }

            if (!success)
                return NotFound(new { error = "NotFound", message = $"Output node with ID {id} not found." });

            return NoContent();
        }

        #endregion

        #region Neuron Manipulation

        [HttpPost("neurons")]
        public IActionResult CreateNeuron(string expId, [FromBody] CreateNeuronRequestDto body)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) 
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            Neuron newNeuron;
            lock (experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot modify world while simulation is running. Pause first." });

                newNeuron = experiment.World.AddNeuron(new Vector3(body.Position.X, body.Position.Y, body.Position.Z));
            }
            
            return CreatedAtAction(nameof(QueryController.GetNeuronById), "Query", new { expId = expId, id = newNeuron.Id }, newNeuron);
        }
        
        [HttpPost("neurons/{id}/mitosis")]
        public IActionResult PerformMitosis(string expId, ulong id, [FromBody] MitosisRequestDto body)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) 
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            
            Neuron child;
            lock(experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot perform mitosis while simulation is running. Pause first." });

                var parent = experiment.World.GetNeuronById(id);
                if(parent == null) 
                    return NotFound(new { error = "NotFound", message = $"Parent neuron with ID {id} not found in experiment '{expId}'." });

                child = experiment.World.PerformMitosis(parent, new Vector3(body.Offset.X, body.Offset.Y, body.Offset.Z));
            }

            return CreatedAtAction(nameof(QueryController.GetNeuronById), "Query", new { expId = expId, id = child.Id }, child);
        }

        [HttpDelete("neurons/{id}")]
        public IActionResult DeleteNeuron(string expId, ulong id)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) 
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });
            
            bool success;
            lock(experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot delete neuron while simulation is running. Pause first." });

                success = experiment.World.RemoveNeuron(id);
            }

            if (!success)
                return NotFound(new { error = "NotFound", message = $"Neuron with ID {id} not found in experiment '{expId}'." });
            
            return NoContent();
        }
        
        [HttpPost("neurons/{id}:deactivate")]
        public IActionResult DeactivateNeuron(string expId, ulong id)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null)
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            lock (experiment.GetLockObject())
            {
                 if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot deactivate neuron while simulation is running. Pause first." });

                var neuron = experiment.World.GetNeuronById(id);
                if (neuron == null)
                    return NotFound(new { error = "NotFound", message = $"Neuron with ID {id} not found." });
                
                experiment.World.MarkNeuronForDeactivation(neuron);
            }

            return Accepted(new { message = $"Neuron {id} has been queued for deactivation at the end of the next tick." });
        }
        
        [HttpPatch("neurons/{id}/lvars")]
        public IActionResult PatchLocalVariables(string expId, ulong id, [FromBody] PatchLVarRequestDto body)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) 
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            lock (experiment.GetLockObject())
            {
                 if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot modify LVars while simulation is running. Pause first." });
                 
                 var neuron = experiment.World.GetNeuronById(id);
                 if (neuron == null)
                     return NotFound(new { error = "NotFound", message = $"Neuron with ID {id} not found." });
                 
                 experiment.World.SetLocalVariables(neuron.Id, body.LocalVariables);
            }
            
            return Ok(new { message = $"Local variables for neuron {id} updated." });
        }

        #endregion

        #region Synapse Manipulation

        [HttpPost("synapses")]
        public IActionResult CreateSynapse(string expId, [FromBody] CreateSynapseRequestDto body)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null) 
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            Synapse? synapse;
            lock(experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot create synapse while simulation is running. Pause first." });

                synapse = experiment.World.AddSynapse(body.SourceId, body.TargetId, body.SignalType, body.Weight, body.Parameter);
            }

            if (synapse == null)
                return BadRequest("Invalid SourceId or TargetId provided.");

            return Ok(synapse);
        }
        
        [HttpPatch("synapses/{id}")]
        public IActionResult ModifySynapse(string expId, ulong id, [FromBody] ModifySynapseRequestDto body)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null)
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            Synapse? synapse;
            lock (experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot modify synapse while simulation is running. Pause first." });

                synapse = experiment.World.GetSynapseById(id);
                if (synapse == null)
                    return NotFound(new { error = "NotFound", message = $"Synapse with ID {id} not found in experiment '{expId}'." });

                if (body.Weight.HasValue) synapse.Weight = body.Weight.Value;
                if (body.Parameter.HasValue) synapse.Parameter = body.Parameter.Value;
                if (body.SignalType.HasValue) synapse.SignalType = body.SignalType.Value;
                if (body.Condition != null)
                {
                    try
                    {
                        synapse.Condition = BuildConditionFromDto(body.Condition);
                    }
                    catch (ArgumentException ex)
                    {
                        return BadRequest(new { error = "BadRequest", message = ex.Message });
                    }
                }
            }
            return Ok(synapse);
        }

        [HttpDelete("synapses/{id}")]
        public IActionResult DeleteSynapse(string expId, ulong id)
        {
            var experiment = _manager.GetExperiment(expId);
            if (experiment == null)
                return NotFound(new { error = "NotFound", message = $"Experiment '{expId}' not found." });

            bool success;
            lock (experiment.GetLockObject())
            {
                if (experiment.State == SimulationState.Running)
                    return Conflict(new { error = "Conflict", message = "Cannot delete synapse while simulation is running. Pause first." });

                success = experiment.World.RemoveSynapse(id);
            }

            if (!success)
                return NotFound(new { error = "NotFound", message = $"Synapse with ID {id} not found in experiment '{expId}'." });
            
            return NoContent();
        }

        #endregion
        
        private ICondition? BuildConditionFromDto(SynapseConditionDto dto)
        {
            return dto.Type.ToLowerInvariant() switch
            {
                "none" => null,
                "lvar" => new LVarCondition
                {
                    Target = dto.Target ?? throw new ArgumentException("LVar condition requires 'target'."),
                    LVarIndex = dto.Index ?? throw new ArgumentException("LVar condition requires 'index'."),
                    Operator = dto.Op ?? throw new ArgumentException("LVar condition requires 'op'."),
                    Value = dto.Value ?? throw new ArgumentException("LVar condition requires 'value'.")
                },
                "gvar" => new GVarCondition
                {
                    GVarIndex = dto.Index ?? throw new ArgumentException("GVar condition requires 'index'."),
                    Operator = dto.Op ?? throw new ArgumentException("GVar condition requires 'op'."),
                    Value = dto.Value ?? throw new ArgumentException("GVar condition requires 'value'.")
                },
                "temporal" => new TemporalCondition
                {
                    Operator = dto.TemporalOperator ?? throw new ArgumentException("Temporal condition requires 'temporalOperator'."),
                    Threshold = dto.Threshold ?? throw new ArgumentException("Temporal condition requires 'threshold'."),
                    Duration = dto.Duration ?? 0
                },
                "relational" => new RelationalCondition
                {
                    Operator = dto.Op ?? throw new ArgumentException("Relational condition requires 'op'.")
                },
                _ => throw new ArgumentException($"Unknown condition type: '{dto.Type}'")
            };
        }
    }
}
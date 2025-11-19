// Hidra.Core/World/HidraWorld.Serialization.cs
using Hidra.Core.Brain;
using Hidra.Core.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Hidra.Core
{
    /// <summary>
    /// A simple DTO used strictly for serialization to ensure the JSON structure 
    /// matches exactly what the HidraWorld constructor expects.
    /// </summary>
    internal class HidraWorldStateDto
    {
        public ulong CurrentTick { get; set; }
        public HidraConfig Config { get; set; } = default!;
        public float[] GlobalHormones { get; set; } = default!;
        public string ExperimentId { get; set; } = default!;
        
        public string HGLGenome { get; set; } = default!;
        
        public Dictionary<ulong, Neuron> Neurons { get; set; } = new();
        public List<Synapse> Synapses { get; set; } = new();
        public Dictionary<ulong, InputNode> InputNodes { get; set; } = new();
        public Dictionary<ulong, OutputNode> OutputNodes { get; set; } = new();
        
        public EventQueue EventQueue { get; set; } = default!;
        
        public long NextNeuronId { get; set; }
        public long NextSynapseId { get; set; }
        public long NextEventId { get; set; }
        
        public ulong RngS0 { get; set; }
        public ulong RngS1 { get; set; }
        public ulong MetricsRngS0 { get; set; }
        public ulong MetricsRngS1 { get; set; }
    }

    public class HidraSerializationBinder : ISerializationBinder
    {
        private static readonly HashSet<Type> WhitelistedTypes = new HashSet<Type>
        {
            typeof(HidraWorld), typeof(HidraConfig), typeof(HidraWorldStateDto),
            typeof(Neuron), typeof(Synapse), typeof(InputNode), typeof(OutputNode),
            typeof(Event), typeof(EventPayload), typeof(KahanAccumulator),
            typeof(NeuralNetworkBrain), typeof(DummyBrain), typeof(LogicGateBrain),
            typeof(BrainInput), typeof(BrainOutput), typeof(NNNode), typeof(NNConnection),
            typeof(LogicGateType), typeof(FlipFlopType),
            typeof(LVarCondition), typeof(GVarCondition), typeof(RelationalCondition),
            typeof(TemporalCondition), typeof(CompositeCondition),
            typeof(List<Synapse>), typeof(List<ICondition>),
            typeof(SortedDictionary<ulong, Neuron>), typeof(SortedDictionary<ulong, InputNode>),
            typeof(SortedDictionary<ulong, OutputNode>), typeof(Dictionary<ulong, KahanAccumulator>),
            typeof(List<BrainInput>), typeof(List<BrainOutput>),
            typeof(List<InputNode>), typeof(List<OutputNode>), typeof(List<Neuron>), typeof(List<Event>),
            typeof(Dictionary<ulong, Neuron>), typeof(Dictionary<ulong, InputNode>), typeof(Dictionary<ulong, OutputNode>)
        };

        public Type BindToType(string? assemblyName, string typeName)
        {
            var resolvedType = Type.GetType($"{typeName}, {assemblyName}") ?? Type.GetType(typeName);
            if (resolvedType != null && IsTypeWhitelisted(resolvedType)) return resolvedType;
            
            if (typeName.StartsWith("Hidra.Core.HidraWorldStateDto")) return typeof(HidraWorldStateDto);
            
            throw new JsonSerializationException($"Deserialization of untrusted type '{typeName}' is not allowed.");
        }

        private static bool IsTypeWhitelisted(Type type)
        {
            if (WhitelistedTypes.Contains(type)) return true;
            if (type.IsArray && type.HasElementType && WhitelistedTypes.Contains(type.GetElementType()!)) return true;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                return WhitelistedTypes.Contains(type.GetGenericArguments()[0]);
            return false;
        }

        public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
        {
            assemblyName = serializedType.Assembly.FullName;
            typeName = serializedType.FullName;
        }
    }

    public partial class HidraWorld
    {
        private string _hglGenome = "";

        public (string FilePath, string JsonContent) SaveStateToJson(string directoryPath, string fileName = "world_state.json")
        {
            Directory.CreateDirectory(directoryPath);
            string jsonContent;
            lock(_worldApiLock) { jsonContent = SaveStateToJson(); }
            string savePath = Path.Combine(directoryPath, fileName);
            File.WriteAllText(savePath, jsonContent);
            return (savePath, jsonContent);
        }
        
        public string SaveStateToJson()
        {
            // Sync the current internal state of the PRNG objects to the storage fields
            // using the IPrng interface method GetState.
            if (this._rng != null)
            {
                this._rng.GetState(out this._rngS0, out this._rngS1);
            }

            if (this._metricsRng != null)
            {
                this._metricsRng.GetState(out this._metricsRngS0, out this._metricsRngS1);
            }

            var dto = new HidraWorldStateDto
            {
                CurrentTick = this.CurrentTick,
                Config = this.Config,
                GlobalHormones = this.GlobalHormones,
                ExperimentId = this.ExperimentId,
                HGLGenome = this._hglGenome,
                Neurons = new Dictionary<ulong, Neuron>(this._neurons),
                Synapses = this._synapses,
                InputNodes = new Dictionary<ulong, InputNode>(this._inputNodes),
                OutputNodes = new Dictionary<ulong, OutputNode>(this._outputNodes),
                EventQueue = this._eventQueue,
                NextNeuronId = this._nextNeuronId,
                NextSynapseId = this._nextSynapseId,
                NextEventId = this._nextEventId,
                // Use the fields we just updated
                RngS0 = this._rngS0,
                RngS1 = this._rngS1,
                MetricsRngS0 = this._metricsRngS0,
                MetricsRngS1 = this._metricsRngS1
            };

            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                TypeNameHandling = TypeNameHandling.Auto
            };
            return JsonConvert.SerializeObject(dto, settings);
        }

        public static HidraWorld LoadStateFromJson(
            string json, 
            string hglGenome, 
            HidraConfig config, 
            IEnumerable<ulong> inputNodeIds, 
            IEnumerable<ulong> outputNodeIds, 
            Action<string, LogLevel, string>? logAction = null)
        {
            var settings = new JsonSerializerSettings
            {
                PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new HidraSerializationBinder()
            };

            var dto = JsonConvert.DeserializeObject<HidraWorldStateDto>(json, settings);
            if (dto == null) throw new InvalidOperationException("Failed to deserialize world state.");

            var world = new HidraWorld(
                dto.Config, dto.CurrentTick, dto.GlobalHormones, dto.ExperimentId,
                new SortedDictionary<ulong, Neuron>(dto.Neurons), 
                dto.Synapses,
                new SortedDictionary<ulong, InputNode>(dto.InputNodes), 
                new SortedDictionary<ulong, OutputNode>(dto.OutputNodes),
                dto.EventQueue, 
                dto.NextNeuronId, dto.NextSynapseId, dto.NextEventId,
                // Pass the PRNG state from the DTO into the private fields
                dto.RngS0, dto.RngS1, dto.MetricsRngS0, dto.MetricsRngS1
            );

            world.Config = config;
            foreach (var id in inputNodeIds) if (!world._inputNodes.ContainsKey(id)) world.AddInputNode(id);
            foreach (var id in outputNodeIds) if (!world._outputNodes.ContainsKey(id)) world.AddOutputNode(id);

            world.SetLogAction(logAction);
            
            string genomeToLoad = !string.IsNullOrEmpty(hglGenome) ? hglGenome : dto.HGLGenome;
            if (string.IsNullOrEmpty(genomeToLoad))
            {
                logAction?.Invoke("SERIALIZATION", LogLevel.Warning, "Loading world state without a Genome. Gene execution events may fail.");
            }

            world.InitializeFromLoad(genomeToLoad);
            return world;
        }

        private void InitializeFromLoad(string hglGenome)
        {
            this._hglGenome = hglGenome;
            
            // Explicitly instantiate XorShift128PlusPrng with the saved state.
            // This class is defined in Hidra.Core (HidraWorld.cs).
            _rng = new XorShift128PlusPrng(_rngS0, _rngS1);
            _metricsRng = new XorShift128PlusPrng(_metricsRngS0, _metricsRngS1);
            
            InitMetrics();
            
            var parser = new HGLParser(); 
            _compiledGenes = parser.ParseGenome(hglGenome, Config.SystemGeneCount);

            foreach (var neuron in _neurons.Values)
            {
                // Re-assign the restored PRNG to all brains
                neuron.Brain.SetPrng(_rng);
                neuron.Brain.InitializeFromLoad();
            }
            
            _synapses.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var neuron in _neurons.Values)
            {
                neuron.OwnedSynapses.Sort((a, b) => a.Id.CompareTo(b.Id));
            }

            _spatialHash = new SpatialHash(Config.CompetitionRadius * 2);
            RebuildSpatialHash();

            _topologicallySortedNeurons = null;
            _incomingSynapseCache = null;
            _inputSynapseCache = null;
        }
    }
}
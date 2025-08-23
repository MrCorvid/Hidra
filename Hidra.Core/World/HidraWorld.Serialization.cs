// Hidra.Core/World/HidraWorld.Serialization.cs
using Hidra.Core.Brain;
using Hidra.Core.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Hidra.Core
{
    /// <summary>
    /// A custom serialization binder that whitelists allowed types to prevent
    /// deserialization vulnerabilities. Only types from the Hidra.Core assembly
    /// and a few known safe system types are permitted. This is a security measure
    /// to mitigate risks associated with deserializing untrusted data.
    /// </summary>
    public class HidraSerializationBinder : ISerializationBinder
    {
        private static readonly HashSet<Type> WhitelistedTypes = new HashSet<Type>
        {
            // Core simulation types
            typeof(HidraWorld), typeof(HidraConfig),
            typeof(Neuron), typeof(Synapse), typeof(InputNode), typeof(OutputNode),
            typeof(Event), typeof(EventPayload),
            typeof(KahanAccumulator),
            
            // Brain-related types
            typeof(NeuralNetworkBrain), typeof(DummyBrain),
            typeof(BrainInput), typeof(BrainOutput),
            typeof(NNNode), typeof(NNConnection),
            
            // Condition types
            typeof(LVarCondition), typeof(GVarCondition), typeof(RelationalCondition),
            typeof(TemporalCondition), typeof(CompositeCondition),
            
            // System collection types used in the world state
            typeof(List<Synapse>), typeof(List<ICondition>),
            typeof(SortedDictionary<ulong, Neuron>), typeof(SortedDictionary<ulong, InputNode>),
            typeof(SortedDictionary<ulong, OutputNode>), typeof(Dictionary<ulong, KahanAccumulator>),
            typeof(List<BrainInput>),
            typeof(List<BrainOutput>)
        };

        public Type BindToType(string? assemblyName, string typeName)
        {
            var resolvedType = Type.GetType($"{typeName}, {assemblyName}");

            if (resolvedType != null && IsTypeWhitelisted(resolvedType))
            {
                return resolvedType;
            }

            throw new JsonSerializationException($"Deserialization of untrusted type '{typeName}' is not allowed.");
        }

        private static bool IsTypeWhitelisted(Type type)
        {
            if (WhitelistedTypes.Contains(type)) return true;
            // This handles arrays like float[], etc.
            if (type.IsArray && type.HasElementType && WhitelistedTypes.Contains(type.GetElementType()!)) return true;
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
        /// <summary>
        /// Saves the current state of the world to a JSON file at a specified path.
        /// </summary>
        /// <remarks>
        /// This method is now simplified to focus only on serialization and file writing,
        /// removing the non-thread-safe run counter logic. The responsibility for generating
        /// unique experiment paths now lies with the application layer calling this method.
        /// </remarks>
        /// <param name="directoryPath">The directory where the state file will be saved. It will be created if it doesn't exist.</param>
        /// <param name="fileName">The name of the file to save (e.g., "world_state_tick_1000.json").</param>
        /// <returns>A tuple containing the full path to the saved file and its JSON content.</returns>
        public (string FilePath, string JsonContent) SaveStateToJson(string directoryPath, string fileName = "world_state.json")
        {
            Directory.CreateDirectory(directoryPath);
            
            string jsonContent;
            lock(_worldApiLock) // Ensure a consistent state is serialized
            {
                jsonContent = SaveStateToJson();
            }
            
            string savePath = Path.Combine(directoryPath, fileName);
            File.WriteAllText(savePath, jsonContent);
            
            Log("SIM_CORE", LogLevel.Info, $"World state saved to: {savePath}");
            return (savePath, jsonContent);
        }
        
        /// <summary>
        /// Serializes the complete state of the HidraWorld instance to a JSON string.
        /// This operation must be performed under a lock if called externally.
        /// </summary>
        /// <returns>A formatted JSON string representing the world state.</returns>
        public string SaveStateToJson()
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                TypeNameHandling = TypeNameHandling.Auto
            };
            return JsonConvert.SerializeObject(this, settings);
        }

        /// <summary>
        /// Creates a new HidraWorld instance by deserializing it from a JSON string, using
        /// provided configuration for proper context initialization. This method is decoupled from API DTOs.
        /// </summary>
        /// <param name="json">The JSON string representing a previously saved world state.</param>
        /// <param name="hglGenome">The HGL genome script required to re-initialize non-serialized gene logic.</param>
        /// <param name="config">The new configuration to apply to the loaded world.</param>
        /// <param name="inputNodeIds">The set of input nodes for the new world context.</param>
        /// <param name="outputNodeIds">The set of output nodes for the new world context.</param>
        /// <param name="logAction">An optional delegate for routing log messages.</param>
        /// <returns>A new, fully initialized <see cref="HidraWorld"/> instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown if deserialization fails and results in a null object.</exception>
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

            var world = JsonConvert.DeserializeObject<HidraWorld>(json, settings);
            
            if (world == null)
            {
                throw new InvalidOperationException("Failed to deserialize world state from JSON. The resulting object was null.");
            }

            // Explicitly override the config and re-initialize context-dependent fields.
            world.Config = config;
            world._inputNodes.Clear();
            world._outputNodes.Clear();
            foreach (var id in inputNodeIds) world.AddInputNode(id);
            foreach (var id in outputNodeIds) world.AddOutputNode(id);

            world.SetLogAction(logAction);
            world.InitializeFromLoad(hglGenome);
            world.Log("SIM_CORE", LogLevel.Info, "World state successfully loaded from JSON with provided config.");
            return world;
        }

        /// <summary>
        /// Re-initializes non-serialized fields after a world has been loaded from JSON.
        /// </summary>
        /// <param name="hglGenome">The HGL genome script needed to re-compile the non-serialized genes.</param>
        private void InitializeFromLoad(string hglGenome)
        {
            InitRngsFromState();
            InitMetrics();
            
            var parser = new HGLParser(); 
            _compiledGenes = parser.ParseGenome(hglGenome, Config.SystemGeneCount);

            foreach (var neuron in _neurons.Values)
            {
                neuron.Brain.SetPrng(_rng);
                neuron.Brain.InitializeFromLoad();
            }
            
            // Explicitly sort lists to guarantee deterministic order after deserialization
            _synapses.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var neuron in _neurons.Values)
            {
                neuron.OwnedSynapses.Sort((a, b) => a.Id.CompareTo(b.Id));
            }

            _spatialHash = new SpatialHash(Config.CompetitionRadius * 2);
            RebuildSpatialHash();

            // Caches will be rebuilt on the first call to Step()
            _topologicallySortedNeurons = null;
            _incomingSynapseCache = null;
            _inputSynapseCache = null;
        }
    }
}
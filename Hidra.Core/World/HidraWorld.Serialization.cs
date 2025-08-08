// Hidra.Core/World/HidraWorld.Serialization.cs
using Hidra.Core.Brain;
using Hidra.Core.Logging;
using Newtonsoft.Json;
using System;
using System.IO;

namespace Hidra.Core
{
    // Note: The following fields are defined in other partial class files of HidraWorld.
    // They are listed here for context.
    /*
     * private readonly SortedDictionary<ulong, Neuron> _neurons = new();
     * private SpatialHash _spatialHash = default!;
     * private Dictionary<uint, AST> _compiledGenes = default!;
     * // The HGLParser field has been removed.
     */

    public partial class HidraWorld
    {
        /// <summary>
        /// Saves the current state of the world to a uniquely named JSON file within a structured experiment directory.
        /// </summary>
        /// <remarks>
        /// This method orchestrates the creation of experiment directories and filenames based on templates
        /// defined in the application's configuration. It handles run counters to prevent overwriting previous saves.
        /// </remarks>
        /// <param name="experimentName">The base name for the experiment, used to generate the directory name.</param>
        /// <returns>The full path to the newly created world state JSON file.</returns>
        /// <exception cref="IOException">Thrown if there are issues creating directories or writing files.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown if the application lacks permissions to write to the target directory.</exception>
        public string SaveStateToJson(string experimentName)
        {
            var expConfig = Logger.GetConfig().ExperimentSerialization;
            
            string counterFilePath = Path.Combine(expConfig.BaseOutputDirectory, expConfig.RunCounterFile);
            int runCount = 0;

            if (File.Exists(counterFilePath))
            {
                try
                {
                    int.TryParse(File.ReadAllText(counterFilePath), out runCount);
                }
                catch (IOException ex)
                {
                    Logger.Log("SERIALIZATION", LogLevel.Warning, $"Could not read run counter file at {counterFilePath}. Resetting to 0. Error: {ex.Message}");
                    runCount = 0;
                }
            }
            runCount++;

            Directory.CreateDirectory(expConfig.BaseOutputDirectory);
            File.WriteAllText(counterFilePath, runCount.ToString());

            string dirName = expConfig.NameTemplate
                .Replace("{name}", experimentName)
                .Replace("{count}", runCount.ToString("D4")) 
                .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"))
                .Replace("{time}", DateTime.Now.ToString("HH-mm-ss"));

            string finalDir = Path.Combine(expConfig.BaseOutputDirectory, dirName);
            Directory.CreateDirectory(finalDir);

            string json = SaveStateToJson();
            
            string savePath = Path.Combine(finalDir, "world_state.json");
            File.WriteAllText(savePath, json);
            
            Logger.Log("SIM_CORE", LogLevel.Info, $"World state saved to: {savePath}");
            return savePath;
        }
        
        /// <summary>
        /// Serializes the complete state of the HidraWorld instance to a JSON string.
        /// </summary>
        /// <remarks>
        /// Uses Newtonsoft.Json with settings configured to handle complex object graphs and type information,
        /// which is essential for correct deserialization.
        /// </remarks>
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
        /// Creates a new HidraWorld instance by deserializing it from a JSON string.
        /// </summary>
        /// <param name="json">The JSON string representing a previously saved world state.</param>
        /// <param name="hglGenome">The HGL genome script. This is required to re-initialize the non-serialized gene execution logic.</param>
        /// <returns>A new, fully initialized <see cref="HidraWorld"/> instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown if deserialization fails and results in a null object.</exception>
        public static HidraWorld LoadStateFromJson(string json, string hglGenome)
        {
            var settings = new JsonSerializerSettings
            {
                PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                // TypeNameHandling is required for polymorphic deserialization of the IBrain interface.
                TypeNameHandling = TypeNameHandling.Auto 
            };

            var world = JsonConvert.DeserializeObject<HidraWorld>(json, settings);
            
            if (world == null)
            {
                throw new InvalidOperationException("Failed to deserialize world state from JSON. The resulting object was null.");
            }

            world.InitializeFromLoad(hglGenome);
            Logger.Log("SIM_CORE", LogLevel.Info, "World state successfully loaded from JSON.");
            return world;
        }

        /// <summary>
        /// Performs post-deserialization initialization. This method reconstructs all non-serialized,
        /// runtime-only data structures like lookups, caches, and the spatial hash.
        /// </summary>
        /// <param name="hglGenome">The HGL genome script needed to compile the genes.</param>
        private void InitializeFromLoad(string hglGenome)
        {
            // The HGLParser is a stateless service. A local instance is created to
            // re-compile the non-serialized gene abstract syntax trees (ASTs). This resolves
            // the error since the _parser field was removed from the class.
            var parser = new HGLParser();
            _compiledGenes = parser.ParseGenome(hglGenome);

            // The neuron lookup was removed for determinism. We still loop through neurons
            // to initialize any non-serialized parts of their components, like the brain.
            foreach (var neuron in _neurons.Values)
            {
                // Check the concrete type of the brain and call its specific initialization logic.
                if (neuron.Brain is NeuralNetworkBrain nnBrain)
                {
                    nnBrain.InitializeFromLoad();
                }
                // When other brain types are added, their post-load logic would be called here.
                // else if (neuron.Brain is SomeOtherBrainType someOtherBrain) { ... }
            }

            // CRITICAL FOR DETERMINISM: The order of synapses in the JSON file is not guaranteed.
            // We must re-sort the list to ensure deterministic processing in the Step() method.
            _synapses.Sort((a, b) => a.Id.CompareTo(b.Id));

            // The spatial hash is a runtime structure and must be recreated and populated.
            // This requires the Config property, which is populated by the deserializer.
            _spatialHash = new SpatialHash(Config.CompetitionRadius * 2);
            RebuildSpatialHash(); // This helper method already contains the Clear/Insert logic.
        }
    }
}
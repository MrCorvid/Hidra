// Hidra.Core/World/HidraWorld.Serialization.cs
namespace Hidra.Core;

using System;
using System.IO;
using Hidra.Core.Brain;
using Hidra.Core.Logging;
using Newtonsoft.Json;

public partial class HidraWorld
{
    /// <summary>
    /// Saves the current state of the world to a uniquely named JSON file within a structured experiment directory.
    /// </summary>
    /// <param name="experimentName">The base name for the experiment, used to generate the directory name.</param>
    /// <returns>A tuple containing the full path to the saved file and the JSON content itself.</returns>
    /// <exception cref="IOException">Thrown if there are issues creating directories or writing files.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown if the application lacks permissions to write to the target directory.</exception>
    /// <remarks>
    /// This method orchestrates the creation of experiment directories and filenames based on templates
    /// defined in the application's configuration. It handles run counters to prevent overwriting previous saves.
    /// </remarks>
    public (string FilePath, string JsonContent) SaveStateToJson(string experimentName)
    {
        var expConfig = Logger.GetConfig().ExperimentSerialization;
        
        var counterFilePath = Path.Combine(expConfig.BaseOutputDirectory, expConfig.RunCounterFile);
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

        var dirName = expConfig.NameTemplate
            .Replace("{name}", experimentName, StringComparison.Ordinal)
            .Replace("{count}", runCount.ToString("D4"), StringComparison.Ordinal) 
            .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"), StringComparison.Ordinal)
            .Replace("{time}", DateTime.Now.ToString("HH-mm-ss"), StringComparison.Ordinal);

        var finalDir = Path.Combine(expConfig.BaseOutputDirectory, dirName);
        Directory.CreateDirectory(finalDir);

        var jsonContent = SaveStateToJson();
        
        var savePath = Path.Combine(finalDir, "world_state.json");
        File.WriteAllText(savePath, jsonContent);
        
        Logger.Log("SIM_CORE", LogLevel.Info, $"World state saved to: {savePath}");
        return (savePath, jsonContent);
    }
    
    /// <summary>
    /// Serializes the complete state of the HidraWorld instance to a JSON string.
    /// </summary>
    /// <returns>A formatted JSON string representing the world state.</returns>
    /// <remarks>
    /// Uses Newtonsoft.Json with settings configured to handle complex object graphs and type information,
    /// which is essential for correct deserialization of interfaces like <see cref="IBrain"/>.
    /// </remarks>
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
    /// Performs post-deserialization initialization to reconstruct runtime-only data structures.
    /// </summary>
    /// <param name="hglGenome">The HGL genome script needed to re-compile the non-serialized genes.</param>
    /// <remarks>
    /// This method is critical for ensuring correctness and determinism after loading a saved state. It performs several key actions:
    /// 1. Re-compiles the HGL gene ASTs, which are not serialized.
    /// 2. Initializes any non-serialized state within components (e.g., neuron brains).
    /// 3. Re-sorts the global synapse list and each neuron's `OwnedSynapses` list by ID to guarantee deterministic processing order.
    /// 4. Rebuilds the spatial hash, which is a transient runtime structure.
    /// 5. Seeds `PreviousSourceValue` for all synapses to prevent false temporal condition triggers on the first tick after load.
    /// </remarks>
    private void InitializeFromLoad(string hglGenome)
    {
        var parser = new HGLParser();
        _compiledGenes = parser.ParseGenome(hglGenome, Config.SystemGeneCount);

        foreach (var neuron in _neurons.Values)
        {
            if (neuron.Brain is NeuralNetworkBrain nnBrain)
            {
                nnBrain.InitializeFromLoad();
            }
        }

        _synapses.Sort((a, b) => a.Id.CompareTo(b.Id));
        foreach (var neuron in _neurons.Values)
        {
            neuron.OwnedSynapses.Sort((a, b) => a.Id.CompareTo(b.Id));
        }
        
        foreach (var s in _synapses)
        {
            float initial = 0f;
            if (_inputNodes.TryGetValue(s.SourceId, out var inNode))
            {
                initial = inNode.Value;
            }
            else if (_neurons.TryGetValue(s.SourceId, out var srcNeuron))
            {
                initial = srcNeuron.LocalVariables[(int)LVarIndex.SomaPotential] + srcNeuron.LocalVariables[(int)LVarIndex.DendriticPotential];
            }
            s.PreviousSourceValue = initial;
        }

        _spatialHash = new SpatialHash(Config.CompetitionRadius * 2);
        RebuildSpatialHash();
    }
}
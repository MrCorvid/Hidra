// Hidra.API/Services/ExperimentManager.cs
using Hidra.API.DTOs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Hidra.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace Hidra.API.Services
{
    public class ExperimentManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, Experiment> _experiments = new();
        private readonly string _baseStoragePath;

        public ExperimentManager()
        {
            _baseStoragePath = Path.Combine(Directory.GetCurrentDirectory(), "_experiments");
            if (!Directory.Exists(_baseStoragePath)) Directory.CreateDirectory(_baseStoragePath);
            DiscoverExistingExperiments();
        }

        private void DiscoverExistingExperiments()
        {
            var dbFiles = Directory.GetFiles(_baseStoragePath, "*.db");
            foreach (var file in dbFiles)
            {
                try
                {
                    string expId = Path.GetFileNameWithoutExtension(file);
                    var experiment = LoadExperimentFromDb(expId);
                    if (experiment != null)
                    {
                        _experiments.TryAdd(expId, experiment);
                        Console.WriteLine($"[ExperimentManager] Discovered and resumed experiment: {expId}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ExperimentManager] Failed to load discovered experiment '{file}': {ex.Message}");
                }
            }
        }

        public Experiment CreateExperiment(CreateExperimentRequestDto request)
        {
            if (request.Seed.HasValue)
            {
                request.Config.Seed0 = (ulong)request.Seed.Value;
                request.Config.Seed1 = (ulong)(request.Seed.Value >> 32) + 1;
            }

            var expId = $"exp_{Guid.NewGuid():N}";
            var experiment = new Experiment(expId, request.Name, request, _baseStoragePath);
            
            // Persist the Genome metadata immediately
            experiment.Db.SaveMetadata("Genome", request.HGLGenome);
            
            _experiments.TryAdd(expId, experiment);
            return experiment;
        }

        public Experiment CreateExperimentFromWorld(CreateExperimentRequestDto request, HidraWorld world)
        {
            var expId = $"exp_{Guid.NewGuid():N}";
            var experiment = new Experiment(expId, request.Name, world, _baseStoragePath);
            
            experiment.Db.SaveMetadata("Name", request.Name);
            experiment.Db.SaveMetadata("Config", JsonConvert.SerializeObject(request.Config));
            
            string genomeToSave = request.HGLGenome;
            
            // Fallback: If request genome is empty (e.g. during restoration/cloning flows),
            // extract it from the live world's private field via reflection.
            if (string.IsNullOrEmpty(genomeToSave))
            {
                 var field = typeof(HidraWorld).GetField("_hglGenome", BindingFlags.NonPublic | BindingFlags.Instance);
                 if (field != null)
                 {
                     genomeToSave = (string)field.GetValue(world) ?? "";
                 }
            }

            experiment.Db.SaveMetadata("Genome", genomeToSave);
            
            _experiments.TryAdd(expId, experiment);
            return experiment;
        }

        public Experiment CloneExperiment(string sourceExpId, CloneExperimentRequestDto request)
        {
            if (!_experiments.TryGetValue(sourceExpId, out var sourceExp))
            {
                throw new KeyNotFoundException($"Source experiment '{sourceExpId}' not found.");
            }

            var newExpId = $"exp_{Guid.NewGuid():N}";
            var newDbPath = Path.Combine(_baseStoragePath, $"{newExpId}.db");

            // Lock the source experiment to prevent writes during the copy process
            lock (sourceExp.GetLockObject())
            {
                if (sourceExp.State == SimulationState.Running)
                {
                    throw new InvalidOperationException("Cannot clone a running experiment. Please pause or stop it first.");
                }

                // Verify the requested tick exists in the source
                if (request.Tick > sourceExp.World.CurrentTick)
                {
                    throw new ArgumentOutOfRangeException(nameof(request.Tick), $"Requested tick {request.Tick} is in the future. Current tick is {sourceExp.World.CurrentTick}.");
                }

                // Perform the Database Clone and Prune
                sourceExp.Db.CloneAndPruneTo(newDbPath, request.Tick, request.Name);
            }

            // Load the new experiment from the cloned database.
            var newExperiment = LoadExperimentFromDb(newExpId, request.Tick);

            if (newExperiment == null)
            {
                throw new InvalidOperationException("Failed to load the cloned experiment.");
            }

            _experiments.TryAdd(newExpId, newExperiment);
            return newExperiment;
        }

        private Experiment? LoadExperimentFromDb(string expId, ulong tickToLoad = 0)
        {
            var dbPath = Path.Combine(_baseStoragePath, $"{expId}.db");
            if (!File.Exists(dbPath)) return null;

            var db = new ExperimentDbService(_baseStoragePath, expId);
            
            try
            {
                var name = db.GetMetadata("Name") ?? "unnamed_recovered";
                var configJson = db.GetMetadata("Config");
                var hglGenome = db.GetMetadata("Genome") ?? ""; 

                var config = !string.IsNullOrEmpty(configJson) 
                    ? JsonConvert.DeserializeObject<HidraConfig>(configJson) 
                    : new HidraConfig();

                ulong targetTick = (tickToLoad > 0) ? tickToLoad : db.GetLatestTick();
                string? jsonSnapshot = db.LoadSnapshotJson(targetTick);
                
                if (string.IsNullOrEmpty(jsonSnapshot))
                    throw new InvalidOperationException($"Snapshot for tick {targetTick} not found or empty.");

                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                    SerializationBinder = new ApiSerializationBinder()
                };

                var persistedTick = JsonConvert.DeserializeObject<PersistedTick>(jsonSnapshot, settings);
                if (persistedTick == null || string.IsNullOrEmpty(persistedTick.WorldStateJson))
                    throw new InvalidOperationException("Failed to deserialize world state from persistence wrapper.");

                // --- ROBUST ID EXTRACTION ---
                // We need to find 'InputNodes' OR 'inputNodes' (and internal field '_inputNodes')
                // because the ContractResolver in Program.cs now forces CamelCase, but older DBs might be PascalCase.
                var jObj = JObject.Parse(persistedTick.WorldStateJson);
                
                // Helper to find a property regardless of casing
                JToken? GetPropertyCaseInsensitive(JObject obj, string name)
                {
                    return obj.GetValue(name, StringComparison.OrdinalIgnoreCase) 
                           ?? obj.GetValue("_" + name, StringComparison.OrdinalIgnoreCase);
                }

                var inputNodesDict = GetPropertyCaseInsensitive(jObj, "inputNodes")?.ToObject<Dictionary<ulong, object>>() 
                                     ?? new Dictionary<ulong, object>();
                                     
                var outputNodesDict = GetPropertyCaseInsensitive(jObj, "outputNodes")?.ToObject<Dictionary<ulong, object>>()
                                      ?? new Dictionary<ulong, object>();
                                   
                var inputIds = inputNodesDict.Keys.ToList();
                var outputIds = outputNodesDict.Keys.ToList();

                var world = HidraWorld.LoadStateFromJson(
                    persistedTick.WorldStateJson, 
                    hglGenome, 
                    config!, 
                    inputIds, 
                    outputIds
                );

                db.Dispose(); 
                return new Experiment(expId, name, world, _baseStoragePath);
            }
            catch (Exception)
            {
                db.Dispose();
                throw;
            }
        }

        public Experiment? GetExperiment(string id)
        {
            _experiments.TryGetValue(id, out var experiment);
            return experiment;
        }

        public IEnumerable<Experiment> ListExperiments(SimulationState? stateFilter = null)
        {
            var query = _experiments.Values.AsEnumerable();
            if (stateFilter.HasValue) query = query.Where(e => e.State == stateFilter.Value);
            return query;
        }

        public bool DeleteExperiment(string id)
        {
            if (_experiments.TryRemove(id, out var experiment))
            {
                experiment.Dispose();
                var dbPath = Path.Combine(_baseStoragePath, $"{id}.db");
                try 
                {
                    if (File.Exists(dbPath)) File.Delete(dbPath);
                    if (File.Exists(dbPath + "-wal")) File.Delete(dbPath + "-wal");
                    if (File.Exists(dbPath + "-shm")) File.Delete(dbPath + "-shm");
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"[ExperimentManager] Warning: Could not delete DB file immediately for {id}: {ex.Message}");
                }
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            foreach (var experiment in _experiments.Values) experiment.Dispose();
            _experiments.Clear();
        }
    }

    public class ApiSerializationBinder : HidraSerializationBinder
    {
        public new Type BindToType(string? assemblyName, string typeName)
        {
            if (typeName == "Hidra.API.DTOs.PersistedTick")
                return typeof(PersistedTick);
            return base.BindToType(assemblyName, typeName);
        }
    }
}
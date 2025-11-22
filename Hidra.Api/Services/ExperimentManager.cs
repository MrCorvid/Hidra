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
using Newtonsoft.Json.Serialization;
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
                // Skip the master registry database to prevent loading errors
                if (Path.GetFileName(file).Equals("master_registry.db", StringComparison.OrdinalIgnoreCase)) 
                    continue;

                try
                {
                    string expId = Path.GetFileNameWithoutExtension(file);
                    // We attempt to load it to verify integrity and resume state.
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
                     var val = field.GetValue(world);
                     genomeToSave = val as string ?? "";
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

            // --- CRITICAL FIX: Unlock the clone so it is writable/runnable ---
            newExperiment.Unlock();
            // ----------------------------------------------------------------

            _experiments.TryAdd(newExpId, newExperiment);
            return newExperiment;
        }

        /// <summary>
        /// Renames an experiment in both memory and persistent storage.
        /// </summary>
        public bool RenameExperiment(string expId, string newName)
        {
            bool found = false;

            // 1. Update In-Memory Instance if active
            if (_experiments.TryGetValue(expId, out var exp))
            {
                exp.Rename(newName);
                found = true;
            }

            // 2. Update Persistence (Disk Metadata)
            // Even if not currently loaded in memory, update the DB file if it exists.
            string dbPath = Path.Combine(_baseStoragePath, $"{expId}.db");
            if (File.Exists(dbPath))
            {
                try 
                {
                    // Use a short-lived DB service just to update metadata
                    using var db = new ExperimentDbService(_baseStoragePath, expId);
                    db.SaveMetadata("Name", newName);
                    found = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ExperimentManager] Error updating name metadata for {expId}: {ex.Message}");
                }
            }

            return found;
        }

        /// <summary>
        /// Manually registers an experiment that was loaded externally (e.g., via Lazy Loading in the controller).
        /// </summary>
        public void RegisterLoadedExperiment(Experiment exp)
        {
            if (exp != null && !string.IsNullOrEmpty(exp.Id))
            {
                _experiments.TryAdd(exp.Id, exp);
            }
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
                var activityType = db.GetMetadata("ActivityType") ?? "Manual"; 

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
                var jObj = JObject.Parse(persistedTick.WorldStateJson);
                
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
                
                // Pass activityType to the constructor
                return new Experiment(expId, name, world, _baseStoragePath, activityType);
            }
            catch (Exception)
            {
                db.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Retrieves an experiment by ID.
        /// Includes Automatic Lazy Loading from disk if the experiment exists but is not in memory.
        /// </summary>
        public Experiment? GetExperiment(string id)
        {
            // 1. Check Memory
            if (_experiments.TryGetValue(id, out var experiment))
            {
                return experiment;
            }

            // 2. Check Disk (Lazy Load)
            string dbPath = Path.Combine(_baseStoragePath, $"{id}.db");
            if (File.Exists(dbPath))
            {
                try
                {
                    var loadedExp = LoadExperimentFromDb(id);
                    if (loadedExp != null)
                    {
                        _experiments.TryAdd(id, loadedExp);
                        Console.WriteLine($"[ExperimentManager] Lazy loaded experiment from disk: {id}");
                        return loadedExp;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ExperimentManager] Failed to lazy load {id}: {ex.Message}");
                }
            }

            return null;
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
            
            // If not in memory, check if file exists and delete it anyway
            string path = Path.Combine(_baseStoragePath, $"{id}.db");
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                    return true;
                }
                catch { return false; }
            }

            return false;
        }

        public void Dispose()
        {
            foreach (var experiment in _experiments.Values) experiment.Dispose();
            _experiments.Clear();
        }
    }

    /// <summary>
    /// Helper class to handle polymorphic deserialization of PersistedTick.
    /// Defines which DTO types are allowed during JSON deserialization.
    /// </summary>
    public class ApiSerializationBinder : HidraSerializationBinder
    {
        public new Type BindToType(string? assemblyName, string typeName)
        {
            // Specifically bind PersistedTick which is defined in the API assembly
            if (typeName == "Hidra.API.DTOs.PersistedTick" || typeName.EndsWith("PersistedTick"))
                return typeof(PersistedTick);
            
            // Delegate to core binder for everything else (HidraWorld, Config, etc.)
            return base.BindToType(assemblyName, typeName);
        }
    }
}
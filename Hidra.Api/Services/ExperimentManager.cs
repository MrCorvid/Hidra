// In Hidra.API/Services/ExperimentManager.cs
using Hidra.API.DTOs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Hidra.Core;

namespace Hidra.API.Services
{
    /// <summary>
    /// A thread-safe, singleton service that manages the lifecycle of all active Experiment instances.
    /// </summary>
    public class ExperimentManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, Experiment> _experiments = new();
        private readonly string _baseStoragePath;

        public ExperimentManager()
        {
            // Define a root folder for all experiment data
            _baseStoragePath = Path.Combine(Directory.GetCurrentDirectory(), "_experiments");
            if (!Directory.Exists(_baseStoragePath))
            {
                Directory.CreateDirectory(_baseStoragePath);
            }
        }

        /// <summary>
        /// Creates a new Experiment instance from a request DTO. The Experiment will
        /// internally create and manage its own HidraWorld and logging context.
        /// </summary>
        public Experiment CreateExperiment(CreateExperimentRequestDto request)
        {
            if (request.Seed.HasValue)
            {
                request.Config.Seed0 = (ulong)request.Seed.Value;
                request.Config.Seed1 = (ulong)(request.Seed.Value >> 32) + 1;
            }

            var expId = $"exp_{Guid.NewGuid():N}";
            
            // This call now correctly matches the Experiment constructor.
            var experimentStoragePath = Path.Combine(_baseStoragePath, expId);
            var experiment = new Experiment(expId, request.Name, request, experimentStoragePath);

            _experiments.TryAdd(expId, experiment);
            return experiment;
        }

        /// <summary>
        /// Wraps a pre-existing HidraWorld instance (e.g., from a deserialized file)
        /// in a new managed Experiment object, ensuring it's correctly integrated.
        /// </summary>
        public Experiment CreateExperimentFromWorld(CreateExperimentRequestDto request, HidraWorld world)
        {
            var expId = $"exp_{Guid.NewGuid():N}";
            
            // This call now correctly matches the Experiment constructor.
            var experimentStoragePath = Path.Combine(_baseStoragePath, expId);
            var experiment = new Experiment(expId, request.Name, world, experimentStoragePath);

            _experiments.TryAdd(expId, experiment);
            return experiment;
        }

        /// <summary>
        /// Retrieves an experiment by its unique ID.
        /// </summary>
        /// <returns>The Experiment instance, or null if not found.</returns>
        public Experiment? GetExperiment(string id)
        {
            _experiments.TryGetValue(id, out var experiment);
            return experiment;
        }

        /// <summary>
        /// Returns a collection of all currently managed experiments, optionally filtered by state.
        /// </summary>
        public IEnumerable<Experiment> ListExperiments(SimulationState? stateFilter = null)
        {
            var query = _experiments.Values.AsEnumerable();
            if (stateFilter.HasValue)
            {
                query = query.Where(e => e.State == stateFilter.Value);
            }
            return query;
        }

        /// <summary>
        /// Removes an experiment from the manager and ensures its resources are disposed of.
        /// </summary>
        /// <returns>True if the experiment was found and removed; otherwise, false.</returns>
        public bool DeleteExperiment(string id)
        {
            if (_experiments.TryRemove(id, out var experiment))
            {
                experiment.Dispose();
                
                // Also delete the persisted data from the filesystem
                var experimentPath = Path.Combine(_baseStoragePath, id);
                if (Directory.Exists(experimentPath))
                {
                    try
                    {
                        Directory.Delete(experimentPath, true);
                    }
                    catch (IOException ex)
                    {
                        Console.WriteLine($"Error deleting experiment data for {id}: {ex.Message}");
                    }
                }
                
                return true;
            }
            return false;
        }

        /// <summary>
        /// Disposes all managed experiments. Called during application shutdown.
        /// </summary>
        public void Dispose()
        {
            foreach (var experiment in _experiments.Values)
            {
                experiment.Dispose();
            }
            _experiments.Clear();
        }
    }
}   
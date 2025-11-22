// Hidra.API/Services/EvolutionService.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hidra.API.Activities;
using Hidra.API.Evolution;
using Hidra.API.Evolution.Strategies; 
using Hidra.Core;
using Hidra.API.DTOs; 

namespace Hidra.API.Services
{
    public class EvolutionService
    {
        public enum EvoState { Idle, Running, Paused, Finished, Error }
        
        public EvoState CurrentState { get; private set; } = EvoState.Idle;
        public string CurrentRunId { get; private set; } = "";

        private EvolutionRunConfig _config = default!;
        private List<string> _population = new(); 
        private readonly List<GenerationStats> _history = new();
        private float _bestFitnessAllTime = -float.MaxValue;

        private CancellationTokenSource _cts = new();
        private Task? _workerTask;
        private readonly object _lock = new();

        private readonly ExperimentManager _experimentManager; 
        private readonly ExperimentRegistryService _registry;
        private readonly string _storagePath;

        public EvolutionService(ExperimentManager experimentManager, ExperimentRegistryService registry)
        {
            _experimentManager = experimentManager;
            _registry = registry;
            _storagePath = Path.Combine(Directory.GetCurrentDirectory(), "_experiments");
        }

        public void StartRun(EvolutionRunConfig config)
        {
            lock (_lock)
            {
                if (CurrentState == EvoState.Running) 
                    throw new InvalidOperationException("Evolution is already running.");
                
                _config = config;
                _history.Clear();
                _bestFitnessAllTime = -float.MaxValue;
                _population.Clear();
                
                // 1. Register the Run (Parent Group)
                CurrentRunId = $"run_{Guid.NewGuid():N}";
                _registry.RegisterExperiment(new ExperimentMetadata
                {
                    Id = CurrentRunId,
                    Name = config.RunName,
                    Type = ExperimentType.EvolutionRun,
                    ActivityType = config.Activity.Type,
                    CreatedAt = DateTime.UtcNow,
                    FitnessScore = 0 
                });

                CurrentState = EvoState.Running;
                _cts = new CancellationTokenSource();
                
                _workerTask = Task.Run(() => RunEvolutionLoop(_cts.Token, CurrentRunId), _cts.Token);
            }
        }

        public void StopRun()
        {
            lock (_lock)
            {
                if (CurrentState == EvoState.Running || CurrentState == EvoState.Paused)
                {
                    _cts.Cancel();
                    try { _workerTask?.Wait(1000); } catch { }
                    CurrentState = EvoState.Idle;
                }
            }
        }

        public EvolutionStatusDto GetStatus()
        {
            lock (_lock)
            {
                return new EvolutionStatusDto
                {
                    State = CurrentState.ToString(),
                    CurrentGeneration = _history.Count,
                    TotalGenerations = _config?.MaxGenerations ?? 0,
                    BestFitnessAllTime = _bestFitnessAllTime,
                    LatestGeneration = _history.LastOrDefault(),
                    History = new List<GenerationStats>(_history)
                };
            }
        }

        /// <summary>
        /// Returns the ID of the best saved experiment for a specific generation.
        /// Useful for the UI "Visualize" button.
        /// </summary>
        public string CreateExperimentFromGeneration(int generationIndex)
        {
            // Since we now save the actual experiment during the loop, we just need to find its ID in the registry.
            var children = _registry.GetByGroup(CurrentRunId);
            
            // We assume the highest fitness one is the "Best" if multiple were saved,
            // or just the one matching the index if only one was saved.
            var best = children.Where(c => c.GenerationIndex == generationIndex)
                               .OrderByDescending(c => c.FitnessScore)
                               .FirstOrDefault();
            
            if (best == null) 
                throw new Exception($"No saved experiment found for generation {generationIndex}. It may have been discarded based on configuration.");
            
            return best.Id;
        }

        private void RunEvolutionLoop(CancellationToken token, string runId)
        {
            try
            {
                // Initialize Strategy (e.g. BasicMutation, RandomSearch)
                IEvolutionStrategy strategy = EvolutionStrategyFactory.Create(_config.GeneticAlgorithm.Strategy);
                strategy.Initialize(_config.GeneticAlgorithm);

                // Generate Gen 0
                _population = strategy.GenerateInitialPopulation();

                for (int gen = 0; gen < _config.MaxGenerations; gen++)
                {
                    if (token.IsCancellationRequested) break;

                    var generationResults = new ConcurrentBag<OrganismResult>();
                    
                    // Keep track of the experiment instances created so we can dispose/clean them
                    var generationExperiments = new ConcurrentBag<Experiment>();

                    // 1. Run Experiments in Parallel
                    // We limit parallelism to ProcessorCount to avoid disk I/O thrashing with SQLite DB creation
                    var parallelOptions = new ParallelOptions 
                    { 
                        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1), 
                        CancellationToken = token 
                    };

                    Parallel.ForEach(_population.Select((g, i) => (g, i)), parallelOptions, (item) =>
                    {
                        var genome = item.g;
                        var index = item.i;
                        
                        // Unique ID for this specific organism instance
                        string expId = $"org_{runId}_gen{gen}_ind{index}";

                        // Create the full Experiment object (this creates the .db file)
                        Experiment exp = CreateExperimentInstance(expId, genome, gen, index);
                        generationExperiments.Add(exp);

                        // Run the simulation logic
                        // The Activity inside the Experiment controls when it finishes.
                        RunSimulationLoop(exp, _config.Activity.MaxTicksPerAttempt);

                        float fit = exp.GetFitness();
                        
                        // --- TELEMETRY COLLECTION ---
                        // Gather metrics before the experiment is disposed.
                        var meta = new Dictionary<string, string> 
                        { 
                            { "ExperimentId", expId },
                            { "Nodes", exp.World.Neurons.Count.ToString() },
                            { "Edges", exp.World.Synapses.Count.ToString() } 
                        };

                        // Merge activity-specific metadata (e.g., CartPole steps, TicTacToe moves)
                        if (exp.ActiveActivity != null)
                        {
                            foreach(var kvp in exp.ActiveActivity.GetRunMetadata())
                            {
                                meta[kvp.Key] = kvp.Value;
                            }
                        }
                        
                        generationResults.Add(new OrganismResult 
                        { 
                            Genome = genome, 
                            Fitness = fit,
                            Metadata = meta 
                        });
                    });

                    // 2. Analyze Generation Results
                    var sorted = generationResults.OrderByDescending(r => r.Fitness).ToList();
                    var best = sorted.First();
                    string bestId = best.Metadata["ExperimentId"];
                    
                    float avg = sorted.Average(r => r.Fitness);
                    float min = sorted.Last().Fitness;

                    // 3. Cleanup vs Persistence Logic
                    foreach (var exp in generationExperiments)
                    {
                        bool isBest = (exp.Id == bestId);
                        
                        // Config determines what we keep:
                        // - SaveAll: Keep everything.
                        // - SaveBest: Keep only the best of the generation.
                        bool keep = _config.SaveAllExperiments || (_config.SaveBestPerGeneration && isBest);

                        if (keep)
                        {
                            // --- LOCK BEFORE REGISTERING ---
                            // This marks the experiment as a historical artifact, preventing
                            // accidental modification (Step/Run) via the API later.
                            exp.Lock();
                            
                            // Dispose to release file locks
                            exp.Dispose();

                            // Register in Master Registry so it appears in the UI
                            _registry.RegisterExperiment(new ExperimentMetadata
                            {
                                Id = exp.Id,
                                Name = exp.Name,
                                Type = ExperimentType.GenerationOrganism,
                                ParentGroupId = runId,
                                ActivityType = _config.Activity.Type,
                                GenerationIndex = gen,
                                FitnessScore = exp.GetFitness(), // Use the fitness from the experiment instance
                                CreatedAt = DateTime.UtcNow
                            });
                        }
                        else
                        {
                            // Delete the .db file and logs to free space
                            exp.Dispose();
                            CleanupExperimentFile(exp.Id);
                        }
                    }

                    // 4. Update In-Memory History (for Graph)
                    lock (_lock)
                    {
                        if (best.Fitness > _bestFitnessAllTime) _bestFitnessAllTime = best.Fitness;
                        _history.Add(new GenerationStats
                        {
                            GenerationIndex = gen,
                            MaxFitness = best.Fitness,
                            AvgFitness = avg,
                            MinFitness = min,
                            BestGenomeHex = best.Genome
                        });
                    }

                    Console.WriteLine($"[Evolution] Gen {gen}: Best={best.Fitness:F2} (ID: {bestId})");

                    // 5. Evolve Next Generation
                    if (gen < _config.MaxGenerations - 1)
                    {
                        _population = strategy.Evolve(sorted);
                    }
                }

                CurrentState = EvoState.Finished;
            }
            catch (OperationCanceledException)
            {
                CurrentState = EvoState.Idle;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Evolution FATAL] {ex}");
                CurrentState = EvoState.Error;
            }
        }

        private Experiment CreateExperimentInstance(string expId, string genome, int gen, int index)
        {
            // Create DTO for initialization
            var req = new CreateExperimentRequestDto
            {
                Name = $"Gen {gen} - #{index}",
                Config = _config.OrganismConfig,
                HGLGenome = genome,
                IOConfig = new API.DTOs.IOConfigDto 
                { 
                    InputNodeIds = _config.InputNodeIds, 
                    OutputNodeIds = _config.OutputNodeIds 
                }
            };

            // Instantiate Experiment directly.
            // This constructor creates the SQLite DB file immediately.
            var exp = new Experiment(expId, req.Name, req, _storagePath, _config.Activity.Type);
            
            // Initialize the activity logic
            if (exp.ActiveActivity != null)
            {
                exp.ActiveActivity.Initialize(_config.Activity);
            }
            
            // Save metadata linking it to the run
            exp.Db.SaveMetadata("ParentGroupId", CurrentRunId);
            exp.Db.SaveMetadata("Generation", gen.ToString());

            return exp;
        }

        private void RunSimulationLoop(Experiment exp, int maxTicks)
        {
            // Run steps until activity says stop or we hit max ticks.
            // exp.Step() handles: Activity I/O -> Physics Step -> DB Snapshot
            int t = 0;
            bool done = false;
            while (!done && t < maxTicks)
            {
                done = exp.Step();
                t++;
            }
        }

        private void CleanupExperimentFile(string expId)
        {
            try
            {
                string dbPath = Path.Combine(_storagePath, $"{expId}.db");
                
                // Delete main DB
                if (File.Exists(dbPath)) File.Delete(dbPath);
                
                // Delete temporary journal files if they exist
                if (File.Exists(dbPath + "-wal")) File.Delete(dbPath + "-wal");
                if (File.Exists(dbPath + "-shm")) File.Delete(dbPath + "-shm");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Evolution] Warning: Failed to delete transient experiment {expId}: {ex.Message}");
            }
        }
    }
}
// Hidra.API/Services/Experiment.cs
using Hidra.Core;
using Hidra.API.DTOs;
using Hidra.API.Activities;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Linq;

namespace Hidra.API.Services
{
    public enum SimulationState { Idle, Running, Paused }

    public class Experiment : IDisposable
    {
        public string Id { get; }
        
        // Name has a private setter to allow renaming via the Rename() method
        public string Name { get; private set; }
        
        public SimulationState State { get; private set; }
        
        // Locking property to prevent modification of historical/evolutionary experiments
        public bool IsLocked { get; private set; } = false;
        
        public Hidra.Core.Logging.LogLevel MinimumLogLevel { get; private set; } = Hidra.Core.Logging.LogLevel.Info;
        public long Seed { get; }
        public Hidra.Core.HidraWorld World { get; } 
        
        public List<Run> RunHistory { get; } = new List<Run>();
        public ExperimentDbService Db { get; }

        // --- Activity Management ---
        public string ActivityType { get; private set; } = "Manual";
        public ISimulationActivity? ActiveActivity { get; private set; }
        // ---------------------------

        private readonly Action<string, Hidra.Core.Logging.LogLevel, string> _worldLogger;
        private Task? _runLoopTask;
        private CancellationTokenSource _cts = new();
        private readonly object _worldLock = new();
        private readonly List<Hidra.Core.Logging.LogEntry> _logBuffer = new(50);
        private readonly object _logBufferLock = new();

        // Constructor for New Experiments
        public Experiment(string id, string name, CreateExperimentRequestDto request, string storagePath, string activityType = "Manual")
        {
            Id = id;
            Name = name;
            State = SimulationState.Idle;
            ActivityType = activityType;
            
            Db = new ExperimentDbService(storagePath, id);
            Db.SaveMetadata("Name", name);
            Db.SaveMetadata("Config", JsonConvert.SerializeObject(request.Config));
            Db.SaveMetadata("ActivityType", activityType);

            _worldLogger = (tag, level, message) =>
            {
                if (level >= MinimumLogLevel)
                {
                    lock (_logBufferLock) 
                    { 
                        _logBuffer.Add(new Hidra.Core.Logging.LogEntry(DateTime.Now, level, tag, message, this.Id));
                        if (_logBuffer.Count >= 1000) FlushLogs();
                    }
                }
            };
            
            World = new Hidra.Core.HidraWorld(request.Config, request.HGLGenome, request.IOConfig.InputNodeIds, request.IOConfig.OutputNodeIds, _worldLogger);
            Seed = (long)World.Config.Seed0;

            InitializeActivity();
            SaveCurrentTickState();
        }
        
        // Constructor for Restoring/Loading Experiments
        public Experiment(string id, string name, Hidra.Core.HidraWorld restoredWorld, string storagePath, string activityType = "Manual")
        {
            Id = id;
            Name = name;
            State = SimulationState.Idle;
            Db = new ExperimentDbService(storagePath, id);
            World = restoredWorld;
            Seed = (long)World.Config.Seed0;
            
            // Attempt to load ActivityType from DB if not explicitly provided (or if default)
            if (activityType == "Manual")
            {
                var storedType = Db.GetMetadata("ActivityType");
                if (!string.IsNullOrEmpty(storedType)) ActivityType = storedType;
            }
            else
            {
                ActivityType = activityType;
            }
            
            // --- RESTORE LOCK STATE ---
            var lockedStr = Db.GetMetadata("IsLocked");
            if (bool.TryParse(lockedStr, out bool locked)) 
            {
                IsLocked = locked;
            }
            
            _worldLogger = (tag, level, message) =>
            {
                if (level >= MinimumLogLevel)
                {
                     lock (_logBufferLock) 
                    { 
                        _logBuffer.Add(new Hidra.Core.Logging.LogEntry(DateTime.Now, level, tag, message, this.Id));
                         if (_logBuffer.Count >= 1000) FlushLogs();
                    }
                }
            };
            World.SetLogAction(_worldLogger);

            InitializeActivity();
        }

        /// <summary>
        /// Updates the experiment name in memory and persists the change to metadata.
        /// </summary>
        public void Rename(string newName)
        {
            Name = newName;
            Db.SaveMetadata("Name", newName);
        }
        
        /// <summary>
        /// Locks the experiment, preventing further simulation steps or runs.
        /// Used for archiving evolutionary organisms.
        /// </summary>
        public void Lock()
        {
            IsLocked = true;
            Db.SaveMetadata("IsLocked", "true");
        }

        /// <summary>
        /// Unlocks the experiment, allowing simulation execution.
        /// Used primarily when cloning a locked experiment.
        /// </summary>
        public void Unlock()
        {
            IsLocked = false;
            Db.SaveMetadata("IsLocked", "false");
        }

        private void InitializeActivity()
        {
            if (ActivityType != "Manual")
            {
                try 
                {
                    ActiveActivity = ActivityFactory.Create(ActivityType);
                    
                    // Try to load ActivityConfig from metadata, otherwise use defaults
                    var configJson = Db.GetMetadata("ActivityConfig");
                    var config = !string.IsNullOrEmpty(configJson)
                        ? JsonConvert.DeserializeObject<ActivityConfig>(configJson)
                        : new ActivityConfig { Type = ActivityType }; // Default fallback

                    if (config != null)
                    {
                        ActiveActivity.Initialize(config);
                    }
                }
                catch (Exception ex) 
                { 
                    // Log locally if possible, otherwise just fallback
                    try { Log("EXPERIMENT", Hidra.Core.Logging.LogLevel.Error, $"Failed to initialize activity {ActivityType}: {ex.Message}"); } catch {}
                    ActiveActivity = null;
                }
            }
        }
        
        public void SetMinimumLogLevel(Hidra.Core.Logging.LogLevel level)
        {
            this.MinimumLogLevel = level;
            Log("EXPERIMENT", Hidra.Core.Logging.LogLevel.Info, $"Minimum log level for this experiment changed to {level}.");
        }

        public void SaveCurrentTickState()
        {
            FlushLogs(); 
            
            // 1. Get the raw internal JSON string (required for restarts)
            string internalJson = World.SaveStateToJson();
            
            // 2. Get events from the previous execution tick
            ulong relevantTick = World.CurrentTick > 0 ? World.CurrentTick - 1 : 0;
            var events = new List<Event>(World.GetEventsForTick(relevantTick));

            // 3. Create wrapper
            var persistedData = new PersistedTick 
            { 
                WorldStateJson = internalJson, 
                Events = events 
            };

            // 4. Save
            Db.SaveSnapshot(World.CurrentTick, persistedData);
        }
        
        private void FlushLogs()
        {
            List<Hidra.Core.Logging.LogEntry> batch;
            lock (_logBufferLock)
            {
                if (_logBuffer.Count == 0) return;
                batch = new List<Hidra.Core.Logging.LogEntry>(_logBuffer);
                _logBuffer.Clear();
            }
            Db.WriteLogBatch(World.CurrentTick, batch);
        }

        private void Log(string tag, Hidra.Core.Logging.LogLevel level, string message) => _worldLogger(tag, level, message);

        public object GetLockObject() => _worldLock;

        public Run CreateAndExecuteRun(CreateRunRequestDto request)
        {
            // --- ENFORCE LOCK ---
            if (IsLocked) throw new InvalidOperationException("Experiment is locked and cannot accept new runs.");

            var run = new Run(this, request);
            lock (_worldLock) { RunHistory.Add(run); }
            Task.Run(() => run.Execute());
            return run;
        }
        
        public void Start()
        {
            // --- ENFORCE LOCK ---
            if (IsLocked) throw new InvalidOperationException("Experiment is locked.");

            lock (_worldLock)
            {
                if (State == SimulationState.Running) return;
                if (_cts.IsCancellationRequested) _cts = new CancellationTokenSource();
                State = SimulationState.Running;
                _runLoopTask = Task.Run(() => ContinuousRunLoop(_cts.Token));
                Log("EXPERIMENT", Hidra.Core.Logging.LogLevel.Info, "Continuous run loop started.");
            }
        }

        public void Pause()
        {
            lock (_worldLock)
            {
                if (State == SimulationState.Running)
                {
                    State = SimulationState.Paused;
                    Log("EXPERIMENT", Hidra.Core.Logging.LogLevel.Info, "Continuous run paused.");
                }
            }
        }
        
        public void Resume()
        {
             // --- ENFORCE LOCK ---
             if (IsLocked) throw new InvalidOperationException("Experiment is locked.");

             lock (_worldLock)
            {
                if (State == SimulationState.Paused)
                {
                    State = SimulationState.Running;
                    Log("EXPERIMENT", Hidra.Core.Logging.LogLevel.Info, "Continuous run resumed.");
                }
            }
        }

        public void Stop()
        {
            Task? taskToAwait = null;
            if (!_cts.IsCancellationRequested) _cts.Cancel();

            lock (_worldLock)
            {
                if (State != SimulationState.Idle)
                {
                    taskToAwait = _runLoopTask;
                    State = SimulationState.Idle;
                }
            }

            try { taskToAwait?.Wait(TimeSpan.FromSeconds(5)); } catch { }
            Log("EXPERIMENT", Hidra.Core.Logging.LogLevel.Info, "Continuous run stopped.");
            SaveCurrentTickState();
        }

        /// <summary>
        /// Advances the simulation by one tick.
        /// If an Activity is active, it manages I/O and determines if the simulation should stop.
        /// </summary>
        /// <returns>True if the experiment is "finished" (e.g. game over), otherwise False.</returns>
        public bool Step()
        {
            // --- ENFORCE LOCK ---
            if (IsLocked) return false; 

            bool finished = false;
            
            lock (_worldLock)
            {
                if (State != SimulationState.Idle && State != SimulationState.Paused && State != SimulationState.Running) return false;

                if (ActiveActivity != null)
                {
                    // 1. Activity Logic (Read Outs -> Logic -> Write Ins)
                    finished = ActiveActivity.Step(World);
                    if (finished)
                    {
                        State = SimulationState.Idle;
                    }
                }
                
                // 2. Physics Logic
                World.Step();
            }
            
            // 3. Persistence
            SaveCurrentTickState();
            
            return finished;
        }

        /// <summary>
        /// Gets the current fitness score from the active activity.
        /// Returns 0 if no activity is attached.
        /// </summary>
        public float GetFitness()
        {
            return ActiveActivity?.GetFitnessScore() ?? 0f;
        }

        private void ContinuousRunLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                bool performedStep = false;
                bool finished = false;

                if (State == SimulationState.Running)
                {
                    // Locking check inside the loop just in case state changes async
                    if (IsLocked) 
                    {
                        lock(_worldLock) { State = SimulationState.Idle; }
                        break; 
                    }

                    lock (_worldLock)
                    {
                        if (!token.IsCancellationRequested && State == SimulationState.Running)
                        {
                            if (ActiveActivity != null)
                            {
                                finished = ActiveActivity.Step(World);
                            }
                            
                            World.Step();
                            performedStep = true;
                        }
                    }
                    
                    if (performedStep) SaveCurrentTickState();

                    if (finished)
                    {
                        lock (_worldLock) { State = SimulationState.Idle; }
                        Log("EXPERIMENT", Hidra.Core.Logging.LogLevel.Info, "Activity signaled completion.");
                        break;
                    }
                }
                try { Task.Delay(performedStep ? 1 : 100, token).Wait(token); } catch { break; }
            }
            lock(_worldLock) { State = SimulationState.Idle; }
        }
        
        public void Dispose()
        {
            Stop();
            Db.Dispose();
            _cts.Dispose();
        }
    }
}
// In Hidra.API/Services/Experiment.cs
using Hidra.Core;
// Note: Hidra.Core.Logging is still used, but we will be more specific below.
using Hidra.API.DTOs;
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
        public string Name { get; }
        public SimulationState State { get; private set; }
        // FIX: Explicitly use the custom LogLevel enum to resolve ambiguity.
        public Hidra.Core.Logging.LogLevel MinimumLogLevel { get; private set; } = Hidra.Core.Logging.LogLevel.Info;
        public long Seed { get; }
        public Hidra.Core.HidraWorld World { get; } // Fully qualifying HidraWorld is good practice but not the error source
        
        public List<Run> RunHistory { get; } = new List<Run>();
        public List<Hidra.Core.Logging.LogEntry> LogHistory { get; } = new List<Hidra.Core.Logging.LogEntry>(8192);
        
        private readonly string _storagePath;
        private readonly Action<string, Hidra.Core.Logging.LogLevel, string> _worldLogger;

        private Task? _runLoopTask;
        private CancellationTokenSource _cts = new();
        private readonly object _worldLock = new();

        public Experiment(string id, string name, CreateExperimentRequestDto request, string storagePath)
        {
            Id = id;
            Name = name;
            State = SimulationState.Idle;
            _storagePath = storagePath;

            _worldLogger = (tag, level, message) =>
            {
                if (level >= MinimumLogLevel)
                {
                    lock (LogHistory) { LogHistory.Add(new Hidra.Core.Logging.LogEntry(DateTime.Now, level, tag, message, this.Id)); }
                }
            };
            
            World = new Hidra.Core.HidraWorld(request.Config, request.HGLGenome, request.IOConfig.InputNodeIds, request.IOConfig.OutputNodeIds, _worldLogger);
            Seed = (long)World.Config.Seed0;

            Directory.CreateDirectory(_storagePath);
            SaveCurrentTickState();
        }
        
        public Experiment(string id, string name, Hidra.Core.HidraWorld restoredWorld, string storagePath)
        {
            Id = id;
            Name = name;
            State = SimulationState.Idle;
            World = restoredWorld;
            Seed = (long)World.Config.Seed0;
            _storagePath = storagePath;

            _worldLogger = (tag, level, message) =>
            {
                if (level >= MinimumLogLevel)
                {
                    lock (LogHistory) { LogHistory.Add(new Hidra.Core.Logging.LogEntry(DateTime.Now, level, tag, message, this.Id)); }
                }
            };
            
            World.SetLogAction(_worldLogger);
            Directory.CreateDirectory(_storagePath);
            SaveCurrentTickState();
        }
        
        // FIX: Explicitly use the custom LogLevel enum in the method signature.
        public void SetMinimumLogLevel(Hidra.Core.Logging.LogLevel level)
        {
            this.MinimumLogLevel = level;
            Log("EXPERIMENT", Hidra.Core.Logging.LogLevel.Info, $"Minimum log level for this experiment changed to {level}.");
        }

        private void SaveCurrentTickState()
        {
            var state = World.GetFullWorldState();
            var filePath = Path.Combine(_storagePath, $"tick_{state.CurrentTick:D8}.json");
            var json = JsonConvert.SerializeObject(state, new JsonSerializerSettings 
            {
                Formatting = Formatting.Indented,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore 
            });
            File.WriteAllText(filePath, json);
        }

        private void Log(string tag, Hidra.Core.Logging.LogLevel level, string message) => _worldLogger(tag, level, message);

        public object GetLockObject() => _worldLock;

        public Run CreateAndExecuteRun(CreateRunRequestDto request)
        {
            var run = new Run(this, request);
            RunHistory.Add(run);
            run.Execute();
            return run;
        }
        
        #region Continuous (Non-Audited) Lifecycle Control

        public void Start()
        {
            lock (_worldLock)
            {
                if (State == SimulationState.Running) return;
                State = SimulationState.Running;
                _cts = new CancellationTokenSource();
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
            lock (_worldLock)
            {
                if (State != SimulationState.Idle)
                {
                    if (!_cts.IsCancellationRequested) _cts.Cancel();
                    State = SimulationState.Idle;
                    taskToAwait = _runLoopTask;
                    Log("EXPERIMENT", Hidra.Core.Logging.LogLevel.Info, "Continuous run stopped.");
                }
            }
            try { taskToAwait?.Wait(TimeSpan.FromSeconds(1)); } catch { /* Ignore */ }
        }

        public void Step()
        {
            lock (_worldLock)
            {
                if (State != SimulationState.Idle)
                {
                    Log("EXPERIMENT", Hidra.Core.Logging.LogLevel.Error, "Cannot 'Step' while a continuous simulation is running or paused. Stop it first.");
                    return;
                }
                World.Step();
                SaveCurrentTickState();
            }
        }

        private void ContinuousRunLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (State == SimulationState.Running)
                {
                    lock (_worldLock)
                    {
                        World.Step();
                        SaveCurrentTickState();
                    }
                }
                try { Task.Delay(10, token).Wait(token); } catch (OperationCanceledException) { break; }
            }
            lock(_worldLock) { State = SimulationState.Idle; }
        }
        
        #endregion

        public void Dispose()
        {
            Stop();
            _cts.Dispose();
        }
    }
}
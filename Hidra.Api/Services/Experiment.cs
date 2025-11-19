// Hidra.API/Services/Experiment.cs
using Hidra.Core;
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
        public Hidra.Core.Logging.LogLevel MinimumLogLevel { get; private set; } = Hidra.Core.Logging.LogLevel.Info;
        public long Seed { get; }
        public Hidra.Core.HidraWorld World { get; } 
        
        public List<Run> RunHistory { get; } = new List<Run>();
        public ExperimentDbService Db { get; }

        private readonly Action<string, Hidra.Core.Logging.LogLevel, string> _worldLogger;
        private Task? _runLoopTask;
        private CancellationTokenSource _cts = new();
        private readonly object _worldLock = new();
        private readonly List<Hidra.Core.Logging.LogEntry> _logBuffer = new(50);
        private readonly object _logBufferLock = new();

        public Experiment(string id, string name, CreateExperimentRequestDto request, string storagePath)
        {
            Id = id;
            Name = name;
            State = SimulationState.Idle;
            
            Db = new ExperimentDbService(storagePath, id);
            Db.SaveMetadata("Name", name);
            Db.SaveMetadata("Config", JsonConvert.SerializeObject(request.Config));

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

            SaveCurrentTickState();
        }
        
        public Experiment(string id, string name, Hidra.Core.HidraWorld restoredWorld, string storagePath)
        {
            Id = id;
            Name = name;
            State = SimulationState.Idle;
            Db = new ExperimentDbService(storagePath, id);
            World = restoredWorld;
            Seed = (long)World.Config.Seed0;
            
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
            var run = new Run(this, request);
            lock (_worldLock) { RunHistory.Add(run); }
            Task.Run(() => run.Execute());
            return run;
        }
        
        public void Start()
        {
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

        public void Step()
        {
            lock (_worldLock)
            {
                if (State != SimulationState.Idle) return;
                World.Step();
            }
            SaveCurrentTickState();
        }

        private void ContinuousRunLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                bool performedStep = false;
                if (State == SimulationState.Running)
                {
                    lock (_worldLock)
                    {
                        if (!token.IsCancellationRequested && State == SimulationState.Running)
                        {
                            World.Step();
                            performedStep = true;
                        }
                    }
                    if (performedStep) SaveCurrentTickState();
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
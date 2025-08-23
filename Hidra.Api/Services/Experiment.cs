// In Hidra.API/Services/Experiment.cs
using Hidra.Core;
using Hidra.Core.Logging;
using Hidra.API.DTOs;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Hidra.API.Services
{
    public enum SimulationState { Idle, Running, Paused }

    /// <summary>
    /// Represents a single, self-contained simulation instance. It encapsulates a HidraWorld,
    /// its lifecycle state (Running, Paused, etc.), its own concurrency controls, and a history
    /// of all audited Runs and logs performed within it.
    /// </summary>
    public class Experiment : IDisposable
    {
        public string Id { get; }
        public string Name { get; }
        public SimulationState State { get; private set; }
        public long Seed { get; }
        public HidraWorld World { get; }
        
        public List<Run> RunHistory { get; } = new List<Run>();
        
        public List<LogEntry> LogHistory { get; } = new List<LogEntry>(8192);
        private readonly Action<string, Hidra.Core.Logging.LogLevel, string> _worldLogger;

        private Task? _runLoopTask;
        private CancellationTokenSource _cts = new();
        private readonly object _worldLock = new();

        /// <summary>
        /// Constructor for creating a new experiment from scratch.
        /// </summary>
        public Experiment(string id, string name, CreateExperimentRequestDto request)
        {
            Id = id;
            Name = name;
            State = SimulationState.Idle;

            _worldLogger = (tag, level, message) =>
            {
                lock (LogHistory)
                {
                    LogHistory.Add(new LogEntry(DateTime.Now, level, tag, message, this.Id));
                }
            };
            
            World = new HidraWorld(request.Config, request.HGLGenome, request.IOConfig.InputNodeIds, request.IOConfig.OutputNodeIds, _worldLogger);
            Seed = (long)World.Config.Seed0;
        }

        /// <summary>
        /// Constructor for wrapping a pre-existing, restored HidraWorld.
        /// </summary>
        public Experiment(string id, string name, HidraWorld restoredWorld)
        {
            Id = id;
            Name = name;
            State = SimulationState.Idle;
            World = restoredWorld;
            Seed = (long)World.Config.Seed0;

            _worldLogger = (tag, level, message) =>
            {
                lock (LogHistory)
                {
                    LogHistory.Add(new LogEntry(DateTime.Now, level, tag, message, this.Id));
                }
            };
            
            // Inject the logger into the restored world instance.
            World.SetLogAction(_worldLogger);
        }

        private void Log(string tag, Hidra.Core.Logging.LogLevel level, string message)
        {
            _worldLogger(tag, level, message);
        }

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
                    if (!_cts.IsCancellationRequested)
                    {
                        _cts.Cancel();
                    }
                    State = SimulationState.Idle;
                    taskToAwait = _runLoopTask;
                    Log("EXPERIMENT", Hidra.Core.Logging.LogLevel.Info, "Continuous run stopped.");
                }
            }
            
            try
            {
                taskToAwait?.Wait(TimeSpan.FromSeconds(1));
            }
            catch (Exception)
            {
                // Ignore exceptions
            }
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
                    }
                }
                try { Task.Delay(10, token).Wait(token); } catch (OperationCanceledException) { break; }
            }

            lock(_worldLock)
            {
                State = SimulationState.Idle;
            }
            Log("EXPERIMENT", Hidra.Core.Logging.LogLevel.Info, "Continuous run loop finished.");
        }

        #endregion

        public void Dispose()
        {
            Stop();
            _cts.Dispose();
        }
    }
}
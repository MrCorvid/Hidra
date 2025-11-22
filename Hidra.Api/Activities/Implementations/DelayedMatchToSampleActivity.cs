// Hidra.API/Activities/Implementations/DelayedMatchToSampleActivity.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Hidra.Core;

namespace Hidra.API.Activities.Implementations
{
    /// <summary>
    /// A temporal memory task.
    /// 1. Presentation: The 'Sample' input is set to a target value (0 or 1).
    /// 2. Delay: All inputs are zeroed for N ticks. The organism must "remember".
    /// 3. Recall: The 'Recall_Signal' input fires. The organism must output the original 'Sample' value.
    /// </summary>
    public class DelayedMatchToSampleActivity : ISimulationActivity
    {
        private enum Phase { Presentation, Delay, Recall, Finished }

        private ActivityConfig _config = default!;
        private Random _rng = new Random();

        // Configuration Defaults
        private int _delayTicks = 10;       // Duration of silence
        private int _presentationTicks = 5; // Duration of input stimulus
        private int _recallTicks = 5;       // Duration of evaluation window
        private int _totalTrials = 5;       // Number of sequences per evaluation

        // Runtime State
        private Phase _phase;
        private int _timer;
        private int _currentTrial;
        private float _targetValue;         // The value to memorize (0.0 or 1.0)
        private float _accumulatedError;
        
        // I/O Node Mapping Cache
        private ulong _inSample;
        private ulong _inRecallSignal;
        private ulong _outMemory;

        public void Initialize(ActivityConfig config)
        {
            _config = config;
            _rng = new Random(); // Deterministic seeding handled by caller if necessary, or add config param

            // 1. Load Mappings from Config
            _inSample = config.InputMapping.GetValueOrDefault("Sample", 0UL);
            _inRecallSignal = config.InputMapping.GetValueOrDefault("Recall_Signal", 0UL);
            _outMemory = config.OutputMapping.GetValueOrDefault("Memory_Out", 0UL);

            // 2. Load Custom Parameters if provided
            if (config.CustomParameters.TryGetValue("DelayTicks", out var delayObj))
                int.TryParse(delayObj.ToString(), out _delayTicks);
            
            if (config.CustomParameters.TryGetValue("Trials", out var trialsObj))
                int.TryParse(trialsObj.ToString(), out _totalTrials);

            _accumulatedError = 0;
            _currentTrial = 0;
            ResetTrial();
        }

        private void ResetTrial()
        {
            _timer = 0;
            // Pick a random binary target: 0.0 or 1.0
            _targetValue = _rng.NextDouble() > 0.5 ? 1.0f : 0.0f;
            _phase = Phase.Presentation;
        }

        public bool Step(HidraWorld world)
        {
            if (_phase == Phase.Finished) return true;

            _timer++;

            // Inputs to apply this tick
            float sampleInput = 0f;
            float recallInput = 0f;

            switch (_phase)
            {
                case Phase.Presentation:
                    // Show the value to remember
                    sampleInput = _targetValue;
                    recallInput = 0f;
                    
                    if (_timer >= _presentationTicks)
                    {
                        _phase = Phase.Delay;
                        _timer = 0;
                    }
                    break;

                case Phase.Delay:
                    // Silence inputs; organism must maintain state via recurrent loops or Lvars
                    sampleInput = 0f;
                    recallInput = 0f;

                    if (_timer >= _delayTicks)
                    {
                        _phase = Phase.Recall;
                        _timer = 0;
                    }
                    break;

                case Phase.Recall:
                    // Cue the recall signal
                    sampleInput = 0f;
                    recallInput = 1.0f;

                    // Evaluate Performance (Only during recall phase)
                    var outputs = world.GetOutputValues(new List<ulong> { _outMemory });
                    float actual = outputs.GetValueOrDefault(_outMemory, 0f);
                    
                    // Accumulate absolute error
                    float error = Math.Abs(actual - _targetValue);
                    _accumulatedError += error;

                    if (_timer >= _recallTicks)
                    {
                        // End of trial
                        _currentTrial++;
                        if (_currentTrial >= _totalTrials)
                        {
                            _phase = Phase.Finished;
                        }
                        else
                        {
                            ResetTrial();
                        }
                    }
                    break;
            }

            // Apply Inputs to World
            world.SetInputValues(new Dictionary<ulong, float>
            {
                { _inSample, sampleInput },
                { _inRecallSignal, recallInput }
            });

            return _phase == Phase.Finished;
        }

        public float GetFitnessScore()
        {
            // Max possible error per tick is 1.0 (distance between 0 and 1).
            // Total evaluated ticks = Trials * RecallTicks.
            float totalRecallTicks = (float)(_totalTrials * _recallTicks);
            
            // Fitness = TotalTicks - TotalError.
            // Perfect score = TotalRecallTicks. Worst score = 0.
            return Math.Max(0f, totalRecallTicks - _accumulatedError);
        }

        public Dictionary<string, string> GetRunMetadata()
        {
            // Provide useful stats for the CSV export
            float avgError = _accumulatedError / Math.Max(1, _totalTrials * _recallTicks);
            
            return new Dictionary<string, string>
            {
                { "AvgError", avgError.ToString("F4") },
                { "Trials", _totalTrials.ToString() },
                { "Delay", _delayTicks.ToString() }
            };
        }
    }
}
// Hidra.API/Activities/Implementations/XorGateActivity.cs
using System;
using System.Collections.Generic;
using Hidra.Core;

namespace Hidra.API.Activities.Implementations
{
    public class XorGateActivity : ISimulationActivity
    {
        private ActivityConfig _config = default!;
        
        // Node IDs
        private ulong _inputA;
        private ulong _inputB;
        private ulong _outputZ;

        // Testing State
        private int _currentCaseIndex;
        private int _ticksOnCurrentCase;
        private float _accumulatedError;
        private bool _finished;
        
        // The 4 cases for XOR: 0,0->0 | 0,1->1 | 1,0->1 | 1,1->0
        private readonly (float a, float b, float expected)[] _testCases = new[]
        {
            (0f, 0f, 0f),
            (0f, 1f, 1f),
            (1f, 0f, 1f),
            (1f, 1f, 0f)
        };

        // How long to present each input case to allow the network to settle
        private const int TICKS_PER_CASE = 5; 

        public void Initialize(ActivityConfig config)
        {
            _config = config;
            // FIX: Use 0UL to match the ulong value type of the dictionary
            _inputA = config.InputMapping.GetValueOrDefault("In_A", 0UL);
            _inputB = config.InputMapping.GetValueOrDefault("In_B", 0UL);
            _outputZ = config.OutputMapping.GetValueOrDefault("Out_Z", 0UL);
            
            _currentCaseIndex = 0;
            _ticksOnCurrentCase = 0;
            _accumulatedError = 0;
            _finished = false;
        }

        public bool Step(HidraWorld world)
        {
            if (_finished) return true;

            // 1. Set Inputs for current case
            var currentCase = _testCases[_currentCaseIndex];
            world.SetInputValues(new Dictionary<ulong, float>
            {
                { _inputA, currentCase.a },
                { _inputB, currentCase.b }
            });

            // 2. Read Output
            // We only measure error at the END of the presentation window (last tick of the case)
            _ticksOnCurrentCase++;
            
            if (_ticksOnCurrentCase >= TICKS_PER_CASE)
            {
                var outputs = world.GetOutputValues(new List<ulong> { _outputZ });
                float actual = outputs.GetValueOrDefault(_outputZ, 0f);
                
                // Calculate Absolute Error
                float error = Math.Abs(currentCase.expected - actual);
                _accumulatedError += error;

                // Move to next case
                _currentCaseIndex++;
                _ticksOnCurrentCase = 0;

                if (_currentCaseIndex >= _testCases.Length)
                {
                    _finished = true;
                }
            }

            return _finished;
        }

        public float GetFitnessScore()
        {
            // Max error is 4.0 (1.0 per case). 
            // Fitness = 4 - Error.
            // Perfect XOR = 4.0. Worst XOR = 0.0.
            return Math.Max(0, 4.0f - _accumulatedError);
        }

        public Dictionary<string, string> GetRunMetadata()
        {
            return new Dictionary<string, string>
            {
                { "Error", _accumulatedError.ToString("F4") }
            };
        }
    }
}
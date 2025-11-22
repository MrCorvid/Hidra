// Hidra.API/Activities/Implementations/CartPoleActivity.cs
using System;
using System.Collections.Generic;
using Hidra.Core;

namespace Hidra.API.Activities.Implementations
{
    public class CartPoleActivity : ISimulationActivity
    {
        // Physics Constants (Standard OpenAI Gym / Barto, Sutton & Anderson)
        private const float Gravity = 9.8f;
        private const float MassCart = 1.0f;
        private const float MassPole = 0.1f;
        private const float TotalMass = MassCart + MassPole;
        private const float Length = 0.5f; // Half-length
        private const float PoleMassLength = MassPole * Length;
        private const float ForceMag = 10.0f;
        private const float Tau = 0.02f; // Seconds per tick

        // State
        private float _x;           // Cart Position
        private float _xDot;        // Cart Velocity
        private float _theta;       // Pole Angle (radians)
        private float _thetaDot;    // Pole Angular Velocity
        
        private int _steps;
        private bool _failed;
        
        // I/O Mapping Cache
        private ulong _inX, _inXDot, _inTheta, _inThetaDot;
        private ulong _outForceLeft, _outForceRight;

        public void Initialize(ActivityConfig config)
        {
            // Reset Physics with slight random perturbation
            var rng = new Random();
            _x = (float)(rng.NextDouble() * 0.1 - 0.05);
            _theta = (float)(rng.NextDouble() * 0.1 - 0.05);
            _xDot = 0;
            _thetaDot = 0;
            _steps = 0;
            _failed = false;

            // Map Nodes
            _inX = config.InputMapping.GetValueOrDefault("X", 0UL);
            _inXDot = config.InputMapping.GetValueOrDefault("X_Dot", 0UL);
            _inTheta = config.InputMapping.GetValueOrDefault("Theta", 0UL);
            _inThetaDot = config.InputMapping.GetValueOrDefault("Theta_Dot", 0UL);
            
            _outForceLeft = config.OutputMapping.GetValueOrDefault("Force_Left", 0UL);
            _outForceRight = config.OutputMapping.GetValueOrDefault("Force_Right", 0UL);
        }

        public bool Step(HidraWorld world)
        {
            if (_failed) return true;
            _steps++;

            // 1. Read Network Output
            var outputs = world.GetOutputValues(new List<ulong> { _outForceLeft, _outForceRight });
            float left = outputs.GetValueOrDefault(_outForceLeft, 0f);
            float right = outputs.GetValueOrDefault(_outForceRight, 0f);

            // Determine force direction (Bang-Bang control)
            // If both neurons fire, they cancel out to 0 force.
            float force = 0f;
            if (right > left && right > 0.5f) force = ForceMag;
            else if (left > right && left > 0.5f) force = -ForceMag;

            // 2. Physics Step (Euler Integration)
            float costheta = (float)Math.Cos(_theta);
            float sintheta = (float)Math.Sin(_theta);

            float temp = (force + PoleMassLength * _thetaDot * _thetaDot * sintheta) / TotalMass;
            float thetaAcc = (Gravity * sintheta - costheta * temp) / 
                             (Length * (4.0f / 3.0f - MassPole * costheta * costheta / TotalMass));
            float xAcc = temp - PoleMassLength * thetaAcc * costheta / TotalMass;

            _x += Tau * _xDot;
            _xDot += Tau * xAcc;
            _theta += Tau * _thetaDot;
            _thetaDot += Tau * thetaAcc;

            // 3. Check Failure Conditions
            // Angle > 12 degrees (approx 0.209 rad) or Position > 2.4 units
            if (_x < -2.4f || _x > 2.4f || _theta < -0.209f || _theta > 0.209f)
            {
                _failed = true;
            }

            // 4. Write Inputs (Normalize slightly for the neural network inputs)
            world.SetInputValues(new Dictionary<ulong, float>
            {
                { _inX, _x / 2.4f },             // Normalized by max bounds
                { _inXDot, _xDot },
                { _inTheta, _theta / 0.209f },   // Normalized by max angle
                { _inThetaDot, _thetaDot }
            });

            return _failed;
        }

        public float GetFitnessScore()
        {
            // Fitness is simply the number of ticks the pole stayed upright.
            return (float)_steps;
        }

        public Dictionary<string, string> GetRunMetadata()
        {
            return new Dictionary<string, string>
            {
                { "Steps", _steps.ToString() },
                { "FinalPos", _x.ToString("F2") },
                { "FinalAngle", _theta.ToString("F3") }
            };
        }
    }
}
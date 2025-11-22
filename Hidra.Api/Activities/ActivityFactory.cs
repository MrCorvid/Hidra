// Hidra.API/Activities/ActivityFactory.cs
using System;
using Hidra.API.Activities.Implementations;

namespace Hidra.API.Activities
{
    public static class ActivityFactory
    {
        public static ISimulationActivity Create(string activityType)
        {
            return activityType.ToLowerInvariant() switch
            {
                "tictactoe" => new TicTacToeActivity(),
                "xor" => new XorGateActivity(),
                "cartpole" => new CartPoleActivity(),
                "delayedmatchtosample" => new DelayedMatchToSampleActivity(),
                _ => throw new ArgumentException($"Unknown Activity Type: {activityType}")
            };
        }
    }
}
// Hidra.API/Activities/ISimulationActivity.cs
using System.Collections.Generic;
using Hidra.Core;

namespace Hidra.API.Activities
{
    /// <summary>
    /// Defines the contract for a task/environment that an organism interacts with.
    /// </summary>
    public interface ISimulationActivity
    {
        /// <summary>
        /// Resets the environment to its initial state.
        /// </summary>
        /// <param name="config">The configuration for this specific run.</param>
        void Initialize(ActivityConfig config);

        /// <summary>
        /// Executes one step of the environment logic.
        /// 1. Reads Organism Outputs (via <paramref name="world"/>).
        /// 2. Updates Internal Environment State.
        /// 3. writes Environment State to Organism Inputs (via <paramref name="world"/>).
        /// </summary>
        /// <param name="world">The simulation instance acting as the organism.</param>
        /// <returns>True if the activity has reached a terminal state (e.g., Game Over), otherwise False.</returns>
        bool Step(HidraWorld world);

        /// <summary>
        /// Calculates the fitness score based on performance in the current trial.
        /// </summary>
        float GetFitnessScore();

        /// <summary>
        /// Returns metadata about the run (e.g., "Result": "Win", "Moves": "5").
        /// </summary>
        Dictionary<string, string> GetRunMetadata();
    }
}
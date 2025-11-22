// Hidra.API/Activities/ActivityConfig.cs
using System.Collections.Generic;

namespace Hidra.API.Activities
{
    /// <summary>
    /// Configuration definition for an activity, typically deserialized from the Master JSON.
    /// </summary>
    public class ActivityConfig
    {
        /// <summary>
        /// The name of the activity type (e.g., "TicTacToe", "XOR", "CartPole").
        /// </summary>
        public string Type { get; set; } = "Unknown";

        /// <summary>
        /// Maximum simulation ticks allowed before the activity is force-terminated (prevents infinite loops).
        /// </summary>
        public int MaxTicksPerAttempt { get; set; } = 1000;

        /// <summary>
        /// How many times the activity should run per organism to get an average fitness 
        /// (useful for stochastic games).
        /// </summary>
        public int TrialsPerOrganism { get; set; } = 1;

        /// <summary>
        /// Maps the Activity's logical input names (e.g., "Board_0_0") to the HidraWorld's InputNode IDs.
        /// </summary>
        public Dictionary<string, ulong> InputMapping { get; set; } = new();

        /// <summary>
        /// Maps the Activity's logical output names (e.g., "Move_X") to the HidraWorld's OutputNode IDs.
        /// </summary>
        public Dictionary<string, ulong> OutputMapping { get; set; } = new();
        
        /// <summary>
        /// Custom parameters specific to the activity implementation (e.g. "OpponentDifficulty": "Hard").
        /// </summary>
        public Dictionary<string, object> CustomParameters { get; set; } = new();
    }
}
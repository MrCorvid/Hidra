// Hidra.API/Evolution/EvolutionRunConfig.cs
using Hidra.Core;
using Hidra.API.Activities;
using System.Collections.Generic;

namespace Hidra.API.Evolution
{
    public class EvolutionRunConfig
    {
        /// <summary>
        /// The name of the run, used for grouping experiments in the UI.
        /// </summary>
        public string RunName { get; set; } = "Evolution_Run";

        /// <summary>
        /// The maximum number of generations to evolve.
        /// </summary>
        public int MaxGenerations { get; set; } = 100;
        
        // --- Persistence Settings ---
        
        /// <summary>
        /// If true, every single organism from every generation is saved to disk as a playable experiment.
        /// WARNING: Consumes significant disk space.
        /// </summary>
        public bool SaveAllExperiments { get; set; } = false;

        /// <summary>
        /// If true (and SaveAll is false), only the best performing organism of each generation is saved.
        /// </summary>
        public bool SaveBestPerGeneration { get; set; } = true;
        
        // ----------------------------

        /// <summary>
        /// Configuration for the Genetic Algorithm (Mutation rates, population size, etc.).
        /// </summary>
        public GAConfig GeneticAlgorithm { get; set; } = new();

        /// <summary>
        /// Configuration for the Task/Environment (e.g., TicTacToe).
        /// </summary>
        public ActivityConfig Activity { get; set; } = new();

        /// <summary>
        /// Configuration for the Organism (HidraWorld settings).
        /// </summary>
        public HidraConfig OrganismConfig { get; set; } = new();

        /// <summary>
        /// I/O Topology for the organism (Must match Activity mappings).
        /// </summary>
        public List<ulong> InputNodeIds { get; set; } = new();
        
        /// <summary>
        /// I/O Topology for the organism (Must match Activity mappings).
        /// </summary>
        public List<ulong> OutputNodeIds { get; set; } = new();
    }

    public class EvolutionStatusDto
    {
        public string State { get; set; } // Idle, Running, Paused, Finished
        public int CurrentGeneration { get; set; }
        public int TotalGenerations { get; set; }
        public float BestFitnessAllTime { get; set; }
        public GenerationStats? LatestGeneration { get; set; }
        public List<GenerationStats> History { get; set; } = new();
    }

    public class GenerationStats
    {
        public int GenerationIndex { get; set; }
        public float MaxFitness { get; set; }
        public float AvgFitness { get; set; }
        public float MinFitness { get; set; }
        public string BestGenomeHex { get; set; } = ""; 
    }
}
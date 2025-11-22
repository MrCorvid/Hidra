// Hidra.API/Evolution/EvolutionConfig.cs
using System.Collections.Generic;

namespace Hidra.API.Evolution
{
    public class GAConfig
    {
        /// <summary>
        /// The strategy implementation to use (e.g., "BasicMutation").
        /// </summary>
        public string Strategy { get; set; } = "BasicMutation";

        /// <summary>
        /// The total number of organisms in each generation.
        /// </summary>
        public int PopulationSize { get; set; } = 50;

        /// <summary>
        /// The percentage of the population that carries over unchanged to the next generation (0.0 to 1.0).
        /// </summary>
        public float ElitismRate { get; set; } = 0.05f;

        /// <summary>
        /// The probability that a specific byte in the genome will be mutated (0.0 to 1.0).
        /// </summary>
        public float MutationRate { get; set; } = 0.01f;

        /// <summary>
        /// The base genome used for Generation 0. 
        /// Must be a Hex string (e.g. "00A1FF..."). Can include "GN" delimiters.
        /// If empty, random noise is generated.
        /// </summary>
        public string BaseGenomeTemplate { get; set; } = "";
    }

    /// <summary>
    /// Represents the outcome of a single organism's life.
    /// </summary>
    public class OrganismResult
    {
        /// <summary>
        /// The Genotype (Hex Bytecode) of the organism.
        /// </summary>
        public string Genome { get; set; } = "";
        
        /// <summary>
        /// The fitness score derived from the Activity.
        /// </summary>
        public float Fitness { get; set; }
        
        /// <summary>
        /// Optional metadata from the run (e.g. "Won", "Moves: 10").
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}
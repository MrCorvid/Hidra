// Hidra.API/Evolution/Strategies/RandomSearchStrategy.cs
using System;
using System.Collections.Generic;

namespace Hidra.API.Evolution.Strategies
{
    public class RandomSearchStrategy : IEvolutionStrategy
    {
        private GAConfig _config = default!;
        // We reuse the basic strategy instance just to leverage its 
        // reliable population generation logic (GenerateRandomHex, etc.)
        private readonly BasicMutationStrategy _generator = new BasicMutationStrategy();

        public void Initialize(GAConfig config)
        {
            _config = config;
            _generator.Initialize(config);
        }

        public List<string> GenerateInitialPopulation()
        {
            return _generator.GenerateInitialPopulation();
        }

        public List<string> Evolve(List<OrganismResult> previousGeneration)
        {
            // RANDOM SEARCH LOGIC:
            // Ignore the previous generation entirely.
            // Do not select parents. Do not mutate.
            // Simply generate a fresh batch of random genomes every single generation.
            // This establishes the "dumb luck" baseline. If Hidra cannot beat this,
            // the evolutionary pressure is not working.
            return GenerateInitialPopulation();
        }
    }
}
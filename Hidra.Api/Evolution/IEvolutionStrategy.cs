// Hidra.API/Evolution/IEvolutionStrategy.cs
using System.Collections.Generic;

namespace Hidra.API.Evolution
{
    public interface IEvolutionStrategy
    {
        void Initialize(GAConfig config);

        /// <summary>
        /// Generates the Generation 0 population.
        /// </summary>
        List<string> GenerateInitialPopulation();

        /// <summary>
        /// Takes the results of Generation N and produces the genomes for Generation N+1.
        /// </summary>
        List<string> Evolve(List<OrganismResult> previousGeneration);
    }
}
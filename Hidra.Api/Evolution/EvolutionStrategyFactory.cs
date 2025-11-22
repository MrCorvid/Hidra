// Hidra.API/Evolution/EvolutionStrategyFactory.cs
using System;
using Hidra.API.Evolution.Strategies;

namespace Hidra.API.Evolution
{
    public static class EvolutionStrategyFactory
    {
        public static IEvolutionStrategy Create(string strategyType)
        {
            return strategyType?.ToLowerInvariant() switch
            {
                "basicmutation" => new BasicMutationStrategy(),
                "randomsearch" => new RandomSearchStrategy(),
                _ => new BasicMutationStrategy()
            };
        }
    }
}
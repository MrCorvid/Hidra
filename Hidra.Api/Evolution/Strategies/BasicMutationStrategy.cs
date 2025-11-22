// Hidra.API/Evolution/Strategies/BasicMutationStrategy.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Hidra.API.Evolution.Strategies
{
    public class BasicMutationStrategy : IEvolutionStrategy
    {
        private GAConfig _config = default!;
        private readonly Random _rng = new Random();

        public void Initialize(GAConfig config)
        {
            _config = config;
        }

        public List<string> GenerateInitialPopulation()
        {
            var pop = new List<string>();
            string template = _config.BaseGenomeTemplate;

            // If no template is provided, generate a random "Genesis" gene (Gene 0)
            // to ensure the organism can at least exist.
            if (string.IsNullOrWhiteSpace(template))
            {
                // Generate 50 random bytes for Gene 0
                template = GenerateRandomHex(50);
            }

            for (int i = 0; i < _config.PopulationSize; i++)
            {
                // Keep one pristine copy of the template at index 0
                if (i == 0 && !string.IsNullOrWhiteSpace(_config.BaseGenomeTemplate))
                {
                    pop.Add(template);
                }
                else
                {
                    // Heavily mutate the rest to create initial diversity
                    pop.Add(Mutate(template, boostMutation: true));
                }
            }
            return pop;
        }

        public List<string> Evolve(List<OrganismResult> previousGeneration)
        {
            var nextGen = new List<string>();

            // 1. Elitism: Preserve the best performers exacty as they are.
            int eliteCount = (int)(_config.PopulationSize * _config.ElitismRate);
            var elites = previousGeneration
                .OrderByDescending(o => o.Fitness)
                .Take(eliteCount)
                .ToList();

            foreach (var elite in elites)
            {
                nextGen.Add(elite.Genome);
            }

            // 2. Fill the rest via Tournament Selection + Mutation
            while (nextGen.Count < _config.PopulationSize)
            {
                var parent = TournamentSelect(previousGeneration);
                var childGenome = Mutate(parent.Genome, boostMutation: false);
                nextGen.Add(childGenome);
            }

            return nextGen;
        }

        private OrganismResult TournamentSelect(List<OrganismResult> population)
        {
            // Standard Tournament Selection: Pick 3 random, choose the best.
            // This maintains diversity better than purely picking top N.
            int tournamentSize = 3;
            OrganismResult best = null!;

            for (int i = 0; i < tournamentSize; i++)
            {
                var candidate = population[_rng.Next(population.Count)];
                if (best == null || candidate.Fitness > best.Fitness)
                {
                    best = candidate;
                }
            }
            return best;
        }

        private string Mutate(string genomeHex, bool boostMutation)
        {
            // 1. Split into Genes (handling the API's "GN" text separator)
            var genes = SplitGenes(genomeHex);
            float rate = boostMutation ? _config.MutationRate * 5.0f : _config.MutationRate;

            // 2. Structural Mutation (Add/Remove/Swap entire genes)
            // Occurs rarely relative to bit-flipping.
            if (genes.Count > 1 && _rng.NextDouble() < 0.05)
            {
                genes = MutateGeneStructure(genes);
            }

            // 3. Byte-Level Mutation
            for (int i = 0; i < genes.Count; i++)
            {
                genes[i] = MutateBytes(genes[i], rate);
            }

            return JoinGenes(genes);
        }

        /// <summary>
        /// Performs insertion, deletion, and bit-flipping on a byte array.
        /// </summary>
        private byte[] MutateBytes(byte[] original, float rate)
        {
            var mutated = new List<byte>(original.Length);

            foreach (var b in original)
            {
                // Decide if we touch this byte
                if (_rng.NextDouble() < rate)
                {
                    double action = _rng.NextDouble();
                    
                    if (action < 0.1) 
                    {
                        // Deletion: Skip adding this byte
                        continue; 
                    }
                    else if (action < 0.3) 
                    {
                        // Insertion: Add random byte before current
                        mutated.Add((byte)_rng.Next(256));
                        mutated.Add(b);
                    }
                    else 
                    {
                        // Modification: Change value
                        // We use a completely random byte to allow jumping out of local optima
                        mutated.Add((byte)_rng.Next(256));
                    }
                }
                else
                {
                    mutated.Add(b);
                }
            }

            // Chance to append new code at the end of the gene
            if (_rng.NextDouble() < rate)
            {
                mutated.Add((byte)_rng.Next(256));
            }

            return mutated.ToArray();
        }

        private List<byte[]> MutateGeneStructure(List<byte[]> genes)
        {
            var result = new List<byte[]>(genes);
            double r = _rng.NextDouble();

            if (r < 0.33 && result.Count > 0) 
            {
                // Duplication: Copy a gene
                int idx = _rng.Next(result.Count);
                result.Insert(idx, (byte[])result[idx].Clone());
            }
            else if (r < 0.66 && result.Count > 1) 
            {
                // Deletion: Remove a gene (never remove the last one)
                int idx = _rng.Next(result.Count);
                result.RemoveAt(idx);
            }
            else if (result.Count > 1) 
            {
                // Swap: Change gene order (changes execution flow/event priorities)
                int idxA = _rng.Next(result.Count);
                int idxB = _rng.Next(result.Count);
                (result[idxA], result[idxB]) = (result[idxB], result[idxA]);
            }
            
            return result;
        }

        // --- Helpers ---

        private List<byte[]> SplitGenes(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return new List<byte[]>();
            // The API format uses "GN" as a delimiter between genes in the hex string.
            var parts = hex.ToUpperInvariant().Split(new[] { "GN" }, StringSplitOptions.None);
            var list = new List<byte[]>();
            foreach (var p in parts) list.Add(HexToBytes(p));
            return list;
        }

        private string JoinGenes(List<byte[]> genes)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < genes.Count; i++)
            {
                if (i > 0) sb.Append("GN");
                sb.Append(BytesToHex(genes[i]));
            }
            return sb.ToString();
        }

        private byte[] HexToBytes(string hex)
        {
            // Remove any non-hex chars just in case
            hex = Regex.Replace(hex, "[^0-9A-F]", "");
            if (hex.Length % 2 != 0) hex += "0"; // Pad if odd

            return Enumerable.Range(0, hex.Length / 2)
                             .Select(x => Convert.ToByte(hex.Substring(x * 2, 2), 16))
                             .ToArray();
        }

        private string BytesToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "");
        }

        private string GenerateRandomHex(int bytes)
        {
            var b = new byte[bytes];
            _rng.NextBytes(b);
            return BytesToHex(b);
        }
    }
}
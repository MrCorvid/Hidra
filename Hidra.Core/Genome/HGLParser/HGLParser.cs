// Hidra.Core/HGLParser.cs
using Hidra.Core.Logging;
using ProgrammingLanguageNr1;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Hidra.Core
{
    public class HGLParser
    {
        private readonly HGLDecompiler _decompiler = new();
        private readonly ASTBuilder _astBuilder = new();
        private readonly IReadOnlyList<MethodInfo> _apiFunctions;

        // As per requirements, the first 4 genes (0-3) are system genes
        // and have different validation rules (empty is allowed).
        private const int NumberOfSystemGenes = 4;

        public HGLParser()
        {
            var discoveredMethods = typeof(HidraSprakBridge)
                .GetMethods()
                .Where(m => m.GetCustomAttributes(typeof(SprakAPI), false).Length > 0)
                .ToDictionary(m => m.Name, m => m);

            var orderedApiMethods = new List<MethodInfo>();
            foreach (string instructionName in HGLOpcodes.MasterInstructionOrder)
            {
                if (instructionName.StartsWith("API_"))
                {
                    if (discoveredMethods.TryGetValue(instructionName, out var method))
                    {
                        orderedApiMethods.Add(method);
                    }
                    else
                    {
                         Logger.Log("PARSER", LogLevel.Error, $"HGL Spec Mismatch: API function '{instructionName}' is defined in HGLOpcodes but not found in HidraSprakBridge.");
                    }
                }
            }
            
            _apiFunctions = orderedApiMethods;
            Logger.Log("PARSER", LogLevel.Info, $"HGLParser initialized with {_apiFunctions.Count} API functions.");
        }

        public Dictionary<uint, AST> ParseGenome(string hglHexString)
        {
            Logger.Log("PARSER", LogLevel.Info, "Starting genome parsing...");
            var compiledGenes = new Dictionary<uint, AST>();
            var normalized = hglHexString.Replace("\r", "").Replace("\n", "").Replace(" ", "");

            // Split by 'GN' but keep empty entries to preserve positional gene IDs.
            // A leading 'GN' will result in an empty string at index 0.
            var geneStrings = normalized.ToUpper().Split(new[] { "GN" }, StringSplitOptions.None);
            Logger.Log("PARSER", LogLevel.Info, $"Found {geneStrings.Length} potential gene strings.");

            for (int i = 0; i < geneStrings.Length; i++)
            {
                uint geneId = (uint)i;
                var geneString = geneStrings[i];
                byte[] geneBytes = HexStringToByteArray(geneString);

                if (geneBytes.Length > 0)
                {
                    // The gene has content, so we parse it. This is valid for both system and non-system genes.
                    Logger.Log("PARSER", LogLevel.Info, $"--- Parsing Gene {geneId} ---");
                    compiledGenes[geneId] = ParseGene(geneBytes);
                }
                else
                {
                    // The gene is empty (or contained no valid hex characters).
                    if (geneId < NumberOfSystemGenes)
                    {
                        // This is an empty SYSTEM gene, which is valid and accepted.
                        Logger.Log("PARSER", LogLevel.Debug, $"Accepting empty system gene {geneId}.");
                        compiledGenes[geneId] = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<EMPTY_GENE>"));
                    }
                    else
                    {
                        // This is an empty NON-SYSTEM gene, which is invalid and ignored.
                        Logger.Log("PARSER", LogLevel.Debug, $"Ignoring empty non-system gene {geneId}.");
                    }
                }
            }

            Logger.Log("PARSER", LogLevel.Info, $"Genome parsing complete. Compiled {compiledGenes.Count} genes.");
            return compiledGenes;
        }

        /// <summary>
        /// Orchestrates the two-pass parsing for a single gene.
        /// </summary>
        private AST ParseGene(byte[] geneBytes)
        {
            // Pass 1: Decompile the raw bytes into a linear instruction list.
            var decompileResult = _decompiler.Decompile(geneBytes);
            
            // Pass 2: Build a structured AST from the instruction list.
            return _astBuilder.BuildAst(decompileResult, _apiFunctions);
        }

        private static byte[] HexStringToByteArray(string hex)
        {
            hex = new string(hex.Where(c => Uri.IsHexDigit(c)).ToArray());
            if (hex.Length % 2 != 0) hex = "0" + hex;
            if (string.IsNullOrEmpty(hex)) return Array.Empty<byte>();
            return Enumerable.Range(0, hex.Length).Where(x => x % 2 == 0).Select(x => Convert.ToByte(hex.Substring(x, 2), 16)).ToArray();
        }
    }
}
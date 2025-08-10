// Hidra.Core/HGLParser.cs
namespace Hidra.Core;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hidra.Core.Logging;
using ProgrammingLanguageNr1;

/// <summary>
/// Parses a genome string in HGL (Hidra Genesis Language) hexadecimal format into a collection
/// of executable Abstract Syntax Trees (ASTs).
/// </summary>
public class HGLParser
{
    private readonly HGLDecompiler _decompiler = new();
    private readonly ASTBuilder _astBuilder = new();
    private readonly IReadOnlyList<MethodInfo> _apiFunctions;

    /// <summary>
    /// Initializes a new instance of the <see cref="HGLParser"/> class, pre-loading and ordering
    /// all available HGL API functions via reflection.
    /// </summary>
    public HGLParser()
    {
        var discoveredMethods = typeof(HidraSprakBridge)
            .GetMethods()
            .Where(m => m.GetCustomAttribute<SprakAPI>() != null)
            .ToDictionary(m => m.Name, m => m);

        var orderedApiMethods = new List<MethodInfo>();
        foreach (var instructionName in HGLOpcodes.MasterInstructionOrder)
        {
            if (instructionName.StartsWith("API_", StringComparison.Ordinal))
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

    /// <summary>
    /// Parses a full genome string into a dictionary of compiled gene ASTs, keyed by gene ID.
    /// </summary>
    /// <param name="hglHexString">The complete genome content as a hexadecimal string.</param>
    /// <param name="systemGeneCount">The number of initial genes to treat as system genes, which have different validation rules (e.g., can be empty).</param>
    /// <returns>A dictionary mapping each valid gene ID to its compiled AST.</returns>
    /// <remarks>
    /// The genome string is expected to be a series of hexadecimal gene definitions, each prefixed by 'GN'.
    /// Genes are assigned IDs based on their position. System genes, up to the count specified by <paramref name="systemGeneCount"/>,
    /// are allowed to be empty. Non-system genes that are empty are ignored.
    /// </remarks>
    public Dictionary<uint, AST> ParseGenome(string hglHexString, uint systemGeneCount)
    {
        Logger.Log("PARSER", LogLevel.Info, "Starting genome parsing...");
        var compiledGenes = new Dictionary<uint, AST>();
        var normalized = hglHexString.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);

        // Split by 'GN' but keep empty entries to preserve positional gene IDs.
        var geneStrings = normalized.ToUpperInvariant().Split(new[] { "GN" }, StringSplitOptions.None);
        Logger.Log("PARSER", LogLevel.Info, $"Found {geneStrings.Length} potential gene strings.");

        for (var i = 0; i < geneStrings.Length; i++)
        {
            uint geneId = (uint)i;
            var geneString = geneStrings[i];
            var geneBytes = HexStringToByteArray(geneString);

            if (geneBytes.Length > 0)
            {
                Logger.Log("PARSER", LogLevel.Info, $"--- Parsing Gene {geneId} ---");
                compiledGenes[geneId] = ParseGene(geneBytes);
            }
            else
            {
                if (geneId < systemGeneCount)
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
    /// Orchestrates the two-pass parsing for a single gene from its bytecode.
    /// </summary>
    private AST ParseGene(byte[] geneBytes)
    {
        // Pass 1: Decompile the raw bytes into a linear instruction list.
        var decompileResult = _decompiler.Decompile(geneBytes);
        
        // Pass 2: Build a structured AST from the instruction list.
        return _astBuilder.BuildAst(decompileResult, _apiFunctions);
    }

    /// <summary>
    /// Converts a hexadecimal string to a byte array, ignoring non-hex characters.
    /// </summary>
    private static byte[] HexStringToByteArray(string hex)
    {
        hex = new string(hex.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length % 2 != 0)
        {
            hex = "0" + hex;
        }
        
        if (string.IsNullOrEmpty(hex))
        {
            return Array.Empty<byte>();
        }
        
        return Enumerable.Range(0, hex.Length)
                         .Where(x => x % 2 == 0)
                         .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                         .ToArray();
    }
}
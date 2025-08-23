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
    private readonly Dictionary<string, MethodInfo> _apiMethodsByName;

    public HGLParser()
    {
        // Build a name-based lookup of all API methods
        _apiMethodsByName = typeof(HidraSprakBridge)
            .GetMethods()
            .Where(m => m.GetCustomAttribute<SprakAPI>() != null)
            .ToDictionary(m => m.Name, m => m);

        Logger.Log("PARSER", LogLevel.Info, $"HGLParser initialized with {_apiMethodsByName.Count} API functions mapped by name.");
    }

    public Dictionary<uint, AST> ParseGenome(string hglHexString, uint systemGeneCount)
    {
        Logger.Log("PARSER", LogLevel.Info, "Starting genome parsing...");
        var compiledGenes = new Dictionary<uint, AST>();
        var normalized = hglHexString.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);

        var geneStrings = normalized.ToUpperInvariant().Split(new[] { "GN" }, StringSplitOptions.None);
        Logger.Log("PARSER", LogLevel.Info, $"Found {geneStrings.Length} potential gene strings.");

        int geneCountToProcess = geneStrings.Length;
        if (normalized.Length > 0 && normalized.EndsWith("GN"))
        {
            geneCountToProcess--;
        }

        for (var i = 0; i < geneCountToProcess; i++)
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
                    Logger.Log("PARSER", LogLevel.Debug, $"Accepting empty system gene {geneId}.");
                    compiledGenes[geneId] = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<EMPTY_GENE>"));
                }
                else
                {
                    Logger.Log("PARSER", LogLevel.Debug, $"Ignoring empty non-system gene {geneId}.");
                }
            }
        }

        Logger.Log("PARSER", LogLevel.Info, $"Genome parsing complete. Compiled {compiledGenes.Count} genes.");
        return compiledGenes;
    }

    private AST ParseGene(byte[] geneBytes)
    {
        var decompileResult = _decompiler.Decompile(geneBytes);
        return _astBuilder.BuildAst(decompileResult, _apiMethodsByName);
    }

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
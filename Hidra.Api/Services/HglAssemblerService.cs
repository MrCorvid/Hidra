// Hidra.API/Services/HglAssemblerService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Hidra.API.DTOs;

namespace Hidra.API.Services
{
    /// <summary>
    /// Assembles human-readable HGL into bytecode using a two-pass approach.
    /// - Supports labels for jump targets (e.g., 'MY_LABEL:').
    /// - Jumps (JZ, JNZ, JMP, JNE) must target a label (e.g., 'JZ MY_LABEL').
    /// - Features an intelligent 'PUSH' macro that accepts multiple arguments on one line
    ///   (e.g., 'PUSH 1 0 0.5') and expands them into the most efficient sequence of
    ///   PUSH_BYTE or PUSH_FLOAT instructions.
    /// </summary>
    public class HglAssemblerService
    {
        public class HglAssemblyException : Exception
        {
            public HglAssemblyException(string message) : base(message) { }
        }

        private readonly HglSpecificationDto _spec;

        public HglAssemblerService(HglService hglService)
        {
            if (hglService is null) throw new ArgumentNullException(nameof(hglService));

            _spec = hglService.Specification
                    ?? throw new InvalidOperationException("HGL specification is not initialized.");
        }

        public string Assemble(string sourceCode)
        {
            var lines = (sourceCode ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder(sourceCode?.Length ?? 64);
            var currentGeneLines = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = CleanLine(lines[i]);
                if (string.IsNullOrEmpty(line)) continue;

                if (line.Equals("GN", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentGeneLines.Count > 0)
                    {
                        sb.Append(AssembleSingleGene(currentGeneLines));
                        currentGeneLines.Clear();
                    }
                    sb.Append("GN");
                }
                else
                {
                    currentGeneLines.Add(lines[i]); // Keep original line for error reporting
                }
            }

            if (currentGeneLines.Count > 0)
            {
                sb.Append(AssembleSingleGene(currentGeneLines));
            }

            return sb.ToString();
        }

        // --- Intermediate Representations ---
        private abstract class GeneLine { public int LineNumber; }
        private sealed class InstructionLine : GeneLine { public string Mnemonic = ""; public List<string> Args = new(); }
        private sealed class LabelLine : GeneLine { public string Label = ""; }

        // --- Gene Assembly Pipeline ---

        private string AssembleSingleGene(List<string> geneLines)
        {
            if (geneLines == null || geneLines.Count == 0 || geneLines.All(string.IsNullOrWhiteSpace))
            {
                return string.Empty;
            }
            
            var parsedLines = ParseLines(geneLines);
            var (symbolTable, byteOffsets) = FirstPass(parsedLines);
            var bytes = SecondPass(parsedLines, symbolTable, byteOffsets);

            var hex = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) hex.Append(b.ToString("X2"));
            return hex.ToString();
        }

        private List<GeneLine> ParseLines(List<string> lines)
        {
            var parsed = new List<GeneLine>();
            for (int i = 0; i < lines.Count; i++)
            {
                string originalLine = lines[i];
                string cleanLine = CleanLine(originalLine);
                if (string.IsNullOrEmpty(cleanLine)) continue;

                if (cleanLine.EndsWith(':'))
                {
                    parsed.Add(new LabelLine { Label = cleanLine[..^1], LineNumber = i + 1 });
                    continue;
                }

                var parts = cleanLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    string mnemonic = parts[0].ToUpperInvariant();
                    
                    if (mnemonic == "PUSH")
                    {
                        if (parts.Length < 2)
                            throw new HglAssemblyException($"PUSH macro requires at least one numeric operand (line {i + 1}).");

                        // Loop through all space-separated arguments on the line
                        for (int j = 1; j < parts.Length; j++)
                        {
                            string valueStr = parts[j];

                            if (int.TryParse(valueStr, out int intVal) && intVal >= 0 && intVal <= 255)
                            {
                                parsed.Add(new InstructionLine { Mnemonic = "PUSH_BYTE", Args = new List<string> { valueStr }, LineNumber = i + 1 });
                            }
                            else if (float.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                            {
                                parsed.Add(new InstructionLine { Mnemonic = "PUSH_FLOAT", Args = new List<string> { valueStr }, LineNumber = i + 1 });
                            }
                            else
                            {
                                throw new HglAssemblyException($"Invalid operand '{valueStr}' for PUSH macro on line {i + 1}. Must be a valid number.");
                            }
                        }
                    }
                    else
                    {
                        if (mnemonic == "PUSH_CONST")
                            throw new HglAssemblyException($"PUSH_CONST is obsolete. Use the 'PUSH <value>' macro instead (line {i + 1}).");

                        parsed.Add(new InstructionLine
                        {
                            Mnemonic = mnemonic,
                            Args = parts.Skip(1).ToList(),
                            LineNumber = i + 1
                        });
                    }
                }
            }
            return parsed;
        }

        private (Dictionary<string, int> symbolTable, Dictionary<GeneLine, int> byteOffsets) FirstPass(List<GeneLine> lines)
        {
            var symbolTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var byteOffsets = new Dictionary<GeneLine, int>();
            int currentOffset = 0;

            foreach (var line in lines)
            {
                if (line is LabelLine labelLine)
                {
                    if (symbolTable.ContainsKey(labelLine.Label))
                        throw new HglAssemblyException($"Duplicate label '{labelLine.Label}' defined on line {line.LineNumber}.");
                    symbolTable[labelLine.Label] = currentOffset;
                }
                else if (line is InstructionLine instr)
                {
                    byteOffsets[instr] = currentOffset;
                    currentOffset += CalculateInstructionSize(instr);
                }
            }
            return (symbolTable, byteOffsets);
        }

        private byte[] SecondPass(List<GeneLine> lines, Dictionary<string, int> symbolTable, Dictionary<GeneLine, int> byteOffsets)
        {
            var bytes = new List<byte>();
            foreach (var line in lines)
            {
                if (line is not InstructionLine instr) continue;

                string mnemonic = MapMnemonic(instr.Mnemonic, instr.LineNumber);
                if (!_spec.Instructions.TryGetValue(mnemonic, out byte opcode))
                    throw new HglAssemblyException($"Internal error: Mnemonic '{mnemonic}' has no opcode (line {instr.LineNumber}).");
                
                bytes.Add(opcode);

                if (mnemonic == "PUSH_BYTE")
                {
                    if (instr.Args.Count != 1 || !int.TryParse(instr.Args[0], out int val))
                        throw new HglAssemblyException($"PUSH_BYTE requires one integer operand (line {instr.LineNumber}).");
                    bytes.Add((byte)ClampByte(val));
                }
                else if (mnemonic == "PUSH_FLOAT")
                {
                    if (instr.Args.Count != 1 || !float.TryParse(instr.Args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float val))
                        throw new HglAssemblyException($"PUSH_FLOAT requires one float operand (line {instr.LineNumber}).");
                    
                    bytes.AddRange(BitConverter.GetBytes(val));
                }
                else if (IsJumpMnemonic(mnemonic))
                {
                    if (instr.Args.Count != 1)
                        throw new HglAssemblyException($"{mnemonic} requires one label operand (line {instr.LineNumber}).");
                    
                    string label = instr.Args[0];
                    if (!symbolTable.TryGetValue(label, out int targetByteOffset))
                        throw new HglAssemblyException($"Undefined label '{label}' targeted on line {instr.LineNumber}.");

                    int currentByteOffset = byteOffsets[instr];
                    int instructionSize = 3; // Jumps are 3 bytes (1 opcode + 2 operand)
                    long delta = (long)targetByteOffset - (currentByteOffset + instructionSize);

                    if (delta < short.MinValue || delta > short.MaxValue)
                        throw new HglAssemblyException($"Jump target '{label}' is too far on line {instr.LineNumber}. Jumps must be within +/-32767 bytes.");

                    short displacement = (short)delta;
                    bytes.Add((byte)(displacement & 0xFF));        // Low byte
                    bytes.Add((byte)((displacement >> 8) & 0xFF)); // High byte
                }
            }
            return bytes.ToArray();
        }

        // --- Helpers ---
        
        private string CleanLine(string raw)
        {
            int hash = raw.IndexOf('#');
            return (hash >= 0 ? raw[..hash] : raw).Trim();
        }
        
        private int CalculateInstructionSize(InstructionLine instr)
        {
            string mnemonic = MapMnemonic(instr.Mnemonic, instr.LineNumber);
            
            if (mnemonic == "PUSH_FLOAT") return 5;
            if (mnemonic == "PUSH_BYTE") return 2;
            if (IsJumpMnemonic(mnemonic)) return 3;

            return 1;
        }

        private string MapMnemonic(string rawUpper, int line)
        {
            if (rawUpper == "PUSH") 
                throw new HglAssemblyException($"Internal error: PUSH macro should be expanded before this stage (line {line}).");

            if (_spec.Instructions.ContainsKey(rawUpper)) return rawUpper;
            var ciHit = _spec.Instructions.Keys.FirstOrDefault(k => k.Equals(rawUpper, StringComparison.OrdinalIgnoreCase));
            if (ciHit != null) return ciHit;

            string candidate = "API_" + rawUpper;
            if (_spec.Instructions.ContainsKey(candidate)) return candidate;
            var ciApiHit = _spec.Instructions.Keys.FirstOrDefault(k => k.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (ciApiHit != null) return ciApiHit;

            throw new HglAssemblyException($"Unknown mnemonic: '{rawUpper}' on line {line}.");
        }

        private bool IsJumpMnemonic(string mnemonic) => mnemonic is "JZ" or "JNZ" or "JMP" or "JNE";
        
        private static int ClampByte(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);
    }
}
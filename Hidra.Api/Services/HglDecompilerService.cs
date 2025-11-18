// Hidra.API/Services/HglDecompilerService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Hidra.Core;

namespace Hidra.API.Services
{
    /// <summary>
    /// Decompiles HGL hexadecimal bytecode back into human-readable assembly language.
    /// It reconstructs the assembly script, including generating labels for jump instructions
    /// and correctly formatting 4-byte float values.
    /// </summary>
    public class HglDecompilerService
    {
        private readonly HglService _hglService;
        private readonly Dictionary<byte, string> _opcodeToNameMap;

        public HglDecompilerService(HglService hglService)
        {
            _hglService = hglService ?? throw new ArgumentNullException(nameof(hglService));
            
            _opcodeToNameMap = _hglService.Specification.Instructions
                .GroupBy(kvp => kvp.Value)
                .ToDictionary(g => g.Key, g => g.First().Key);
        }

        public string Decompile(string hexBytecode)
        {
            if (string.IsNullOrWhiteSpace(hexBytecode))
            {
                return string.Empty;
            }

            var rawGeneStrings = hexBytecode.ToUpperInvariant().Split(new[] { "GN" }, StringSplitOptions.None);
            var result = new StringBuilder();

            for (int i = 0; i < rawGeneStrings.Length; i++)
            {
                if (i > 0)
                {
                    result.AppendLine("GN");
                }
                var normalizedGene = new string(rawGeneStrings[i].Where(IsHexDigit).ToArray());
                string decompiledGene = DecompileSingleGene(normalizedGene);
                result.Append(decompiledGene);
            }

            return result.ToString();
        }

        private string DecompileSingleGene(string hexGene)
        {
            if (string.IsNullOrWhiteSpace(hexGene)) return string.Empty;

            var bytes = HexStringToByteArray(hexGene);
            if (bytes.Length == 0) return string.Empty;

            var decompiler = new HGLDecompiler();
            var dr = decompiler.Decompile(bytes);
            var instructions = dr.Instructions;
            var sb = new StringBuilder();

            // 1. Create labels for all jump targets.
            var labels = new Dictionary<int, string>();
            int labelCounter = 0;
            foreach (var targetIndex in dr.JumpTargets.Keys.OrderBy(k => k))
            {
                if (!labels.ContainsKey(targetIndex))
                {
                    labels[targetIndex] = $"LBL_{labelCounter++}";
                }
            }

            // 2. Build the assembly string.
            for (int i = 0; i < instructions.Count; i++)
            {
                if (labels.TryGetValue(i, out var label))
                {
                    sb.AppendLine($"{label}:");
                }

                var instr = instructions[i];
                string mnemonic = GetMnemonic(instr.Opcode);

                if (IsJumpInstruction(instr.Opcode))
                {
                    int targetIndex = dr.JumpSources[i];
                    if (labels.TryGetValue(targetIndex, out var targetLabel))
                    {
                        sb.AppendLine($"    {mnemonic} {targetLabel}");
                    }
                    else
                    {
                        sb.AppendLine($"    {mnemonic} INVALID_TARGET_{targetIndex}");
                    }
                }
                else if (instr.Opcode == HGLOpcodes.PUSH_BYTE)
                {
                    sb.AppendLine($"    PUSH_BYTE {instr.Operand}");
                }
                else if (instr.Opcode == HGLOpcodes.PUSH_FLOAT)
                {
                    // The operand contains the raw bits of the float. We must convert them back.
                    float floatValue = BitConverter.ToSingle(BitConverter.GetBytes(instr.Operand), 0);
                    sb.AppendLine($"    PUSH_FLOAT {floatValue.ToString(CultureInfo.InvariantCulture)}");
                }
                else
                {
                    sb.AppendLine($"    {mnemonic}");
                }
            }
            
            if (labels.TryGetValue(instructions.Count, out var endLabel))
            {
                sb.AppendLine($"{endLabel}:");
            }

            return sb.ToString();
        }
        
        private static bool IsHexDigit(char c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');

        private string GetMnemonic(byte opcode)
        {
            if (_opcodeToNameMap.TryGetValue(opcode, out var name))
            {
                // To keep the service consistent with the assembler macro, we can decompile
                // both PUSH_BYTE and PUSH_FLOAT to the simpler "PUSH" mnemonic.
                if (name == "API_PUSH_BYTE" || name == "API_PUSH_FLOAT") return "PUSH";

                return name.StartsWith("API_", StringComparison.Ordinal) ? name.Substring(4) : name;
            }
            return $"DB 0x{opcode:X2}"; // Fallback for unknown opcodes.
        }

        private bool IsJumpInstruction(byte opcode) =>
            opcode == HGLOpcodes.JZ  || opcode == HGLOpcodes.JMP ||
            opcode == HGLOpcodes.JNZ || opcode == HGLOpcodes.JNE;
        
        private static byte[] HexStringToByteArray(string hex)
        {
            if (hex.Length % 2 != 0) return Array.Empty<byte>();
            
            try
            {
                return Enumerable.Range(0, hex.Length)
                                 .Where(x => x % 2 == 0)
                                 .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                                 .ToArray();
            }
            catch (FormatException)
            {
                return Array.Empty<byte>();
            }
        }
    }
}
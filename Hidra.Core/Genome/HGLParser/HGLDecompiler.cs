// Hidra.Core/Genome/HGLParser/HGLDecompiler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hidra.Core.Logging;

namespace Hidra.Core
{
    public class HGLDecompiler
    {
        public class Instruction
        {
            public int ByteOffset { get; }
            public byte Opcode { get; }
            public int Operand { get; }    // Raw immediate (unsigned byte as int for non-push; sign-extended for jumps; raw float bits for PUSH_FLOAT)
            public int Size { get; }

            public Instruction(int byteOffset, byte opcode, int operand, int size)
            {
                ByteOffset = byteOffset;
                Opcode = opcode;
                Operand = operand;
                Size = size;
            }

            public override string ToString() =>
                $"[0x{ByteOffset:X2}] {Opcode:X2} (Operand: {Operand}) Size={Size}";
        }

        public sealed class DecompileResult
        {
            public List<Instruction> Instructions { get; } = new();
            public int[] JumpSources { get; internal set; } = Array.Empty<int>();
            public Dictionary<int, List<int>> JumpTargets { get; internal set; } = new();
        }

        private readonly Action<string, LogLevel, string>? _log;

        public HGLDecompiler(Action<string, LogLevel, string>? logAction = null)
        {
            _log = logAction;
        }

        private void Log(string area, LogLevel level, string message)
        {
            _log?.Invoke(area, level, message);
        }

        public DecompileResult Decompile(byte[] bytes)
        {
            var dr = new DecompileResult();

            // 1) Linear decode of instructions
            int pc = 0;
            while (pc < bytes.Length)
            {
                byte op = bytes[pc];
                int size = GetInstructionSize(op);

                if (pc + size > bytes.Length)
                {
                    Log("PARSER", LogLevel.Warning, $"  Truncated instruction at 0x{pc:X2}. Expected size {size}, bytes left {bytes.Length - pc}. Stopping.");
                    break;
                }

                int operand = 0;
                if (op == HGLOpcodes.PUSH_BYTE)
                {
                    operand = bytes[pc + 1]; // literal byte
                }
                else if (op == HGLOpcodes.PUSH_FLOAT)
                {
                    // Store the raw float bits as an integer. The AST builder will convert this back.
                    operand = BitConverter.ToInt32(bytes, pc + 1);
                }
                else if (IsJumpInstruction(op))
                {
                    // Read two bytes for a signed short displacement
                    short rel = (short)(bytes[pc + 1] | (bytes[pc + 2] << 8));
                    operand = rel;
                }

                dr.Instructions.Add(new Instruction(pc, op, operand, size));
                pc += size;
            }

            // 2) Jump maps (including end-of-stream support)
            BuildJumpMaps(dr);

            return dr;
        }

        private void BuildJumpMaps(DecompileResult dr)
        {
            var instrs = dr.Instructions;
            int n = instrs.Count;
            var offsetToIndex = new Dictionary<int, int>(n);
            for (int i = 0; i < n; i++)
                offsetToIndex[instrs[i].ByteOffset] = i;

            var jumpSources = Enumerable.Repeat(-1, n).ToArray();
            var jumpTargets = new Dictionary<int, List<int>>();
            int totalBytes = (n == 0) ? 0 : instrs[^1].ByteOffset + instrs[^1].Size;

            for (int i = 0; i < n; i++)
            {
                var instr = instrs[i];
                if (!IsJumpInstruction(instr.Opcode)) continue;

                int targetByteOffset = instr.ByteOffset + instr.Size + instr.Operand;

                if (targetByteOffset == totalBytes)
                {
                    jumpSources[i] = n;
                    if (!jumpTargets.ContainsKey(n)) jumpTargets[n] = new List<int>();
                    jumpTargets[n].Add(i);
                }
                else if (offsetToIndex.TryGetValue(targetByteOffset, out int targetIndex))
                {
                    jumpSources[i] = targetIndex;
                    if (!jumpTargets.ContainsKey(targetIndex)) jumpTargets[targetIndex] = new List<int>();
                    jumpTargets[targetIndex].Add(i);
                }
                else
                {
                    Log("PARSER", LogLevel.Warning,
                        $"  Jump at instruction {i} has invalid target offset {targetByteOffset}. This jump will be ignored.");
                }
            }
            dr.JumpSources = jumpSources;
            dr.JumpTargets = jumpTargets;
        }

        // --- Helpers ---

        private bool IsJumpInstruction(byte opcode)
        {
            return opcode == HGLOpcodes.JZ  ||
                   opcode == HGLOpcodes.JMP ||
                   opcode == HGLOpcodes.JNZ ||
                   opcode == HGLOpcodes.JNE;
        }

        private int GetInstructionSize(byte opcode)
        {
            if (opcode == HGLOpcodes.PUSH_FLOAT) return 5; // 1 byte opcode + 4 byte float
            if (opcode == HGLOpcodes.PUSH_BYTE) return 2;  // 1 byte opcode + 1 byte data
            if (IsJumpInstruction(opcode)) return 3;       // 1 byte opcode + 2 byte offset
            return 1;
        }
    }
}
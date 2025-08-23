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
            public int Operand { get; }    // raw immediate (unsigned byte as int for non-push; sign-extended for jumps)
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
            /// <summary>
            /// For an instruction index i that is a jump, JumpSources[i] gives the
            /// destination instruction index. For non-jumps, it is -1. If the jump
            /// targets end-of-stream, this value is Instructions.Count (virtual index).
            /// </summary>
            public int[] JumpSources { get; internal set; } = Array.Empty<int>();
            /// <summary>
            /// Inverse map: dest index -> list of source indices. May contain a key equal to
            /// Instructions.Count for end-of-stream jumps.
            /// </summary>
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
                    // Truncated/garbage at end — log and stop
                    Log("PARSER", LogLevel.Warning, $"  Truncated instruction at 0x{pc:X2}. Expected size {size}, bytes left {bytes.Length - pc}. Stopping.");
                    break;
                }

                int operand = 0;
                if (op == HGLOpcodes.PUSH_BYTE)
                {
                    operand = bytes[pc + 1]; // literal byte
                }
                else if (IsJumpInstruction(op))
                {
                    // Signed sbyte displacement
                    sbyte rel = unchecked((sbyte)bytes[pc + 1]);
                    operand = rel; // store sign-extended displacement
                }

                dr.Instructions.Add(new Instruction(pc, op, operand, size));
                pc += size;
            }

            // 2) Jump maps (including end-of-stream support)
            BuildJumpMaps(dr);

            // --- TRACING ---
            var trace = new StringBuilder();
            trace.AppendLine("--- DECOMPILED INSTRUCTIONS ---");
            for (int i = 0; i < dr.Instructions.Count; i++)
            {
                var instr = dr.Instructions[i];
                string jumpInfo = "";
                if (dr.JumpSources[i] != -1)
                {
                    jumpInfo = $" -> Jumps to instruction index {dr.JumpSources[i]}";
                }
                trace.AppendLine($"  Index {i,-3} | {instr}{jumpInfo}");
            }
            trace.AppendLine("-------------------------------");
            Log("PARSER", LogLevel.Debug, trace.ToString());
            // --- END TRACING ---

            return dr;
        }

        private void BuildJumpMaps(DecompileResult dr)
        {
            var instrs = dr.Instructions;
            int n = instrs.Count;

            // Build an offset->index map for fast lookups at instruction starts
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

                // Operand is signed displacement relative to (pc + size)
                int targetByteOffset = instr.ByteOffset + instr.Size + instr.Operand;

                // Accept exact end-of-stream as a valid "virtual" target index == n
                if (targetByteOffset == totalBytes)
                {
                    Log("PARSER", LogLevel.Debug,
                        $"  Jump found at instruction {i} (0x{instr.ByteOffset:X2}) -> end-of-stream (0x{targetByteOffset:X2}).");
                    jumpSources[i] = n; // virtual index
                    if (!jumpTargets.ContainsKey(n)) jumpTargets[n] = new List<int>();
                    jumpTargets[n].Add(i);
                    continue;
                }

                // Otherwise, must land exactly on an instruction boundary we know
                if (offsetToIndex.TryGetValue(targetByteOffset, out int targetIndex))
                {
                    Log("PARSER", LogLevel.Debug,
                        $"  Jump found at instruction {i} (0x{instr.ByteOffset:X2}) -> instruction {targetIndex} (0x{targetByteOffset:X2}).");
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

        // Helpers

        private bool IsJumpInstruction(byte opcode)
        {
            return opcode == HGLOpcodes.JZ  ||
                   opcode == HGLOpcodes.JMP ||
                   opcode == HGLOpcodes.JNZ ||
                   opcode == HGLOpcodes.JNE;
        }

        private int GetInstructionSize(byte opcode)
        {
            // PUSH_BYTE and all jumps are 2 bytes; everything else 1 byte.
            if (opcode == HGLOpcodes.PUSH_BYTE) return 2;
            if (IsJumpInstruction(opcode)) return 2;
            return 1;
        }
    }
}
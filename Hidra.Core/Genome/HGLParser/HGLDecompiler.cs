// Hidra.Core/Genome/HGLParser/HGLDecompiler.cs
using Hidra.Core.Logging;
using System.Collections.Generic;
using System.Linq;

namespace Hidra.Core
{
    /// <summary>
    /// Decompiles a raw byte array representing HGL gene code into a structured, linear list of instructions.
    /// This is the first pass of the HGL parsing process.
    /// </summary>
    public class HGLDecompiler
    {
        /// <summary>
        /// Represents a single, decoded instruction from the bytecode.
        /// </summary>
        public class Instruction
        {
            /// <summary>The byte offset of this instruction within the original gene byte array.</summary>
            public int ByteOffset { get; }
            /// <summary>The operation code of the instruction.</summary>
            public byte Opcode { get; }
            /// <summary>The operand value for the instruction. For jumps, this is a relative offset.</summary>
            public int Operand { get; }
            /// <summary>The total size of the instruction in bytes (opcode + operand).</summary>
            public int Size { get; }

            /// <summary>Initializes a new instance of the <see cref="Instruction"/> class.</summary>
            public Instruction(int byteOffset, byte opcode, int operand, int size)
            {
                ByteOffset = byteOffset;
                Opcode = opcode;
                Operand = operand;
                Size = size;
            }

            /// <summary>Returns a string representation of the instruction for debugging.</summary>
            public override string ToString() => $"[0x{ByteOffset:X2}] Op=0x{Opcode:X2}, Operand={Operand}, Size={Size}";
        }

        /// <summary>
        /// Contains the complete results of a decompilation pass.
        /// </summary>
        /// <param name="Instructions">The linear list of all decoded instructions.</param>
        /// <param name="JumpSources">A map from a jump instruction's index to its target instruction's index.</param>
        /// <param name="JumpTargets">A map from a target instruction's index to a list of all instructions that jump to it.</param>
        public record DecompileResult(
            List<Instruction> Instructions,
            Dictionary<int, int> JumpSources,
            Dictionary<int, List<int>> JumpTargets
        );

        /// <summary>
        /// Decompiles a gene's bytecode into a structured list of instructions and analyzes jump targets.
        /// </summary>
        /// <param name="geneBytes">The raw byte array of the gene to decompile.</param>
        /// <returns>A <see cref="DecompileResult"/> containing the structured information.</returns>
        public DecompileResult Decompile(byte[] geneBytes)
        {
            Logger.Log("PARSER", LogLevel.Debug, $"--- Starting Decompilation of {geneBytes.Length} bytes ---");
            Logger.Log("PARSER", LogLevel.Debug, $"Input Bytes: {string.Concat(geneBytes.Select(b => b.ToString("X2")))}");

            List<Instruction> instructions = DecodeInstructions(geneBytes);
            (Dictionary<int, int> jumpSources, Dictionary<int, List<int>> jumpTargets) = AnalyzeJumps(instructions);

            foreach (var instr in instructions)
            {
                Logger.Log("PARSER", LogLevel.Debug, $"  [Instruction {instructions.IndexOf(instr)}]: {instr}");
            }

            Logger.Log("PARSER", LogLevel.Debug, $"Decompilation complete. Found {instructions.Count} instructions.");
            return new DecompileResult(instructions, jumpSources, jumpTargets);
        }

        /// <summary>
        /// First pass: Decodes the raw byte array into a linear list of Instruction objects.
        /// </summary>
        private List<Instruction> DecodeInstructions(byte[] geneBytes)
        {
            var instructions = new List<Instruction>();
            int pc = 0; // Program Counter
            while (pc < geneBytes.Length)
            {
                int instructionOffset = pc;
                byte opcode = geneBytes[pc++];
                int operand = 0;
                int size = GetInstructionSize(opcode);

                if (pc < geneBytes.Length)
                {
                    if (opcode == HGLOpcodes.PUSH_BYTE)
                    {
                        // PUSH_BYTE's operand is an unsigned byte literal.
                        operand = geneBytes[pc];
                    }
                    else if (IsJumpInstruction(opcode))
                    {
                        // Jump operands are signed bytes, representing a relative offset.
                        operand = (sbyte)geneBytes[pc];
                    }
                }
                
                instructions.Add(new Instruction(instructionOffset, opcode, operand, size));
                
                // Advance the program counter by the size of the operand, if any.
                // The opcode itself was already consumed by pc++.
                if (size > 1)
                {
                    pc++;
                }
            }
            return instructions;
        }

        /// <summary>
        /// Second pass: Iterates through the decoded instructions to build jump source and target maps.
        /// </summary>
        private (Dictionary<int, int> JumpSources, Dictionary<int, List<int>> JumpTargets) AnalyzeJumps(List<Instruction> instructions)
        {
            var jumpSources = new Dictionary<int, int>();
            var jumpTargets = new Dictionary<int, List<int>>();
            var offsetToIndexMap = instructions.Select((instr, index) => new { instr.ByteOffset, index })
                                               .ToDictionary(item => item.ByteOffset, item => item.index);

            for (int i = 0; i < instructions.Count; i++)
            {
                var instr = instructions[i];
                if (IsJumpInstruction(instr.Opcode))
                {
                    // The target byte offset is relative to the *end* of the current instruction.
                    int targetByteOffset = instr.ByteOffset + instr.Size + instr.Operand;
                    
                    if (offsetToIndexMap.TryGetValue(targetByteOffset, out int targetIndex))
                    {
                        Logger.Log("PARSER", LogLevel.Debug, $"  Jump found at instruction {i} (0x{instr.ByteOffset:X2}) -> instruction {targetIndex} (0x{targetByteOffset:X2}).");
                        jumpSources[i] = targetIndex;
                        if (!jumpTargets.ContainsKey(targetIndex))
                        {
                            jumpTargets[targetIndex] = new List<int>();
                        }
                        jumpTargets[targetIndex].Add(i);
                    }
                    else
                    {
                        Logger.Log("PARSER", LogLevel.Warning, $"  Jump at instruction {i} has invalid target offset {targetByteOffset}. It does not land on the start of an instruction.");
                    }
                }
            }
            return (jumpSources, jumpTargets);
        }

        /// <summary>
        /// Checks if an opcode corresponds to any jump instruction.
        /// </summary>
        private bool IsJumpInstruction(byte opcode) =>
            opcode >= HGLOpcodes.JZ && opcode <= HGLOpcodes.JNE;

        /// <summary>
        /// Gets the total size (in bytes) of an instruction, including its operand.
        /// </summary>
        private int GetInstructionSize(byte opcode) =>
            (opcode == HGLOpcodes.PUSH_BYTE || IsJumpInstruction(opcode)) ? 2 : 1;
    }
}
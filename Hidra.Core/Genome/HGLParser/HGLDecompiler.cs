// Hidra.Core/Genome/HGLParser/HGLDecompiler.cs
namespace Hidra.Core;

using System.Collections.Generic;
using System.Linq;
using Hidra.Core.Logging;

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
        /// <summary>Gets the byte offset of this instruction within the original gene byte array.</summary>
        public int ByteOffset { get; }
        /// <summary>Gets the operation code of the instruction.</summary>
        public byte Opcode { get; }
        /// <summary>Gets the operand value for the instruction. For jumps, this is a relative offset.</summary>
        public int Operand { get; }
        /// <summary>Gets the total size of the instruction in bytes (opcode + operand).</summary>
        public int Size { get; }

        /// <summary>Initializes a new instance of the <see cref="Instruction"/> class.</summary>
        /// <param name="byteOffset">The byte offset of this instruction within the original gene byte array.</param>
        /// <param name="opcode">The operation code of the instruction.</param>
        /// <param name="operand">The operand value for the instruction.</param>
        /// <param name="size">The total size of the instruction in bytes (opcode + operand).</param>
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

        var instructions = DecodeInstructions(geneBytes);
        var (jumpSources, jumpTargets) = AnalyzeJumps(instructions);

        foreach (var (instr, index) in instructions.Select((instr, index) => (instr, index)))
        {
            Logger.Log("PARSER", LogLevel.Debug, $"  [Instruction {index}]: {instr}");
        }

        Logger.Log("PARSER", LogLevel.Debug, $"Decompilation complete. Found {instructions.Count} instructions.");
        return new DecompileResult(instructions, jumpSources, jumpTargets);
    }

    /// <summary>
    /// Decodes the raw byte array into a linear list of Instruction objects.
    /// </summary>
    private static List<Instruction> DecodeInstructions(byte[] geneBytes)
    {
        var instructions = new List<Instruction>();
        var pc = 0;
        while (pc < geneBytes.Length)
        {
            var instructionOffset = pc;
            byte opcode = geneBytes[pc++];
            int operand = 0;
            var size = GetInstructionSize(opcode);

            if (pc < geneBytes.Length)
            {
                if (opcode == HGLOpcodes.PUSH_BYTE)
                    operand = geneBytes[pc]; // PUSH_BYTE's operand is an unsigned byte literal.
                else if (IsJumpInstruction(opcode))
                    operand = (sbyte)geneBytes[pc]; // Jump operands are signed bytes, representing a relative offset.
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
    /// Iterates through the decoded instructions to build jump source and target maps.
    /// </summary>
    private static (Dictionary<int, int> JumpSources, Dictionary<int, List<int>> JumpTargets) AnalyzeJumps(List<Instruction> instructions)
    {
        var jumpSources = new Dictionary<int, int>();
        var jumpTargets = new Dictionary<int, List<int>>();
        var offsetToIndexMap = instructions.Select((instr, index) => new { instr.ByteOffset, index })
                                           .ToDictionary(item => item.ByteOffset, item => item.index);

        for (var i = 0; i < instructions.Count; i++)
        {
            var instr = instructions[i];
            if (IsJumpInstruction(instr.Opcode))
            {
                // The target byte offset is relative to the *end* of the current instruction.
                var targetByteOffset = instr.ByteOffset + instr.Size + instr.Operand;
                
                if (offsetToIndexMap.TryGetValue(targetByteOffset, out var targetIndex))
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
    private static bool IsJumpInstruction(byte opcode) =>
        opcode >= HGLOpcodes.JZ && opcode <= HGLOpcodes.JNE;

    /// <summary>
    /// Gets the total size (in bytes) of an instruction, including its operand.
    /// </summary>
    private static int GetInstructionSize(byte opcode) =>
        (opcode == HGLOpcodes.PUSH_BYTE || IsJumpInstruction(opcode)) ? 2 : 1;
}
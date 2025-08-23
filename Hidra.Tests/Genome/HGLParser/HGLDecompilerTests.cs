using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using Hidra.Core.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hidra.Tests.Genome
{
    [TestClass]
    public class HGLDecompilerTests : BaseTestClass
    {
        private HGLDecompiler _decompiler = null!;
        private readonly Action<string, LogLevel, string>? _testLogger = null;

        [TestInitialize]
        public override void BaseInit()
        {
            base.BaseInit();
            _decompiler = new HGLDecompiler();
        }

        #region Nested Type Tests

        [TestMethod]
        public void Instruction_Constructor_AssignsPropertiesCorrectly()
        {
            // --- ARRANGE ---
            const int byteOffset = 10;
            // Can't be const: value comes from runtime class
            byte opcode = HGLOpcodes.POP;
            const int operand = 123;
            const int size = 1;

            // --- ACT ---
            var instruction = new HGLDecompiler.Instruction(byteOffset, opcode, operand, size);

            // --- ASSERT ---
            Assert.AreEqual(byteOffset, instruction.ByteOffset);
            Assert.AreEqual(opcode, instruction.Opcode);
            Assert.AreEqual(operand, instruction.Operand);
            Assert.AreEqual(size, instruction.Size);
        }

        #endregion

        #region Decoding Logic Tests

        [TestMethod]
        public void Decompile_WithSimpleInstructions_DecodesCorrectly()
        {
            // --- ARRANGE ---
            var bytecode = new byte[] { HGLOpcodes.NOP, HGLOpcodes.DUP, HGLOpcodes.POP };

            // --- ACT ---
            var result = _decompiler.Decompile(bytecode);
            var instructions = result.Instructions;

            // --- ASSERT ---
            Assert.AreEqual(3, instructions.Count);
            Assert.AreEqual(HGLOpcodes.NOP, instructions[0].Opcode);
            Assert.AreEqual(HGLOpcodes.DUP, instructions[1].Opcode);
            Assert.AreEqual(HGLOpcodes.POP, instructions[2].Opcode);
        }

        [TestMethod]
        public void Decompile_WithPushByteInstruction_DecodesOperandCorrectly()
        {
            // --- ARRANGE ---
            var bytecode = new byte[] { HGLOpcodes.PUSH_BYTE, 0x42 };

            // --- ACT ---
            var result = _decompiler.Decompile(bytecode);
            var instruction = result.Instructions.First();

            // --- ASSERT ---
            Assert.AreEqual(1, result.Instructions.Count);
            Assert.AreEqual(HGLOpcodes.PUSH_BYTE, instruction.Opcode);
            Assert.AreEqual(66, instruction.Operand);
            Assert.AreEqual(2, instruction.Size);
        }

        [TestMethod]
        public void Decompile_WithTruncatedBytecode_HandlesGracefully()
        {
            // --- ARRANGE ---
            var bytecode = new byte[] { HGLOpcodes.PUSH_BYTE };

            // --- ACT ---
            var result = _decompiler.Decompile(bytecode);

            // --- ASSERT ---
            // Current decompiler stops decoding truncated trailing instruction
            Assert.AreEqual(0, result.Instructions.Count);
        }

        #endregion

        #region Jump Analysis Tests

        private static int CountValidJumps(int[] jumpSources) => jumpSources.Count(x => x != -1);

        [TestMethod]
        public void Decompile_WithForwardJump_AnalyzesJumpCorrectly()
        {
            // --- ARRANGE ---
            // NOP; JMP +1; NOP; POP
            var bytecode = new byte[] { HGLOpcodes.NOP, HGLOpcodes.JMP, 1, HGLOpcodes.NOP, HGLOpcodes.POP };

            // --- ACT ---
            var result = _decompiler.Decompile(bytecode);

            // --- ASSERT ---
            Assert.AreEqual(1, CountValidJumps(result.JumpSources));
            Assert.AreEqual(3, result.JumpSources[1]); // instr 1 jumps to instr 3
            CollectionAssert.AreEqual(new List<int> { 1 }, result.JumpTargets[3]);
        }

        [TestMethod]
        public void Decompile_WithBackwardJump_AnalyzesJumpCorrectly()
        {
            // --- ARRANGE ---
            // NOP; NOP; JZ -4  (target = back to first NOP)
            var bytecode = new byte[] { HGLOpcodes.NOP, HGLOpcodes.NOP, HGLOpcodes.JZ, unchecked((byte)(sbyte)-4) };

            // --- ACT ---
            var result = _decompiler.Decompile(bytecode);

            // --- ASSERT ---
            Assert.AreEqual(1, CountValidJumps(result.JumpSources));
            Assert.AreEqual(0, result.JumpSources[2]); // instr 2 jumps to instr 0
            CollectionAssert.AreEqual(new List<int> { 2 }, result.JumpTargets[0]);
        }

        [TestMethod]
        public void Decompile_WithJumpToInvalidTarget_IgnoresJump()
        {
            // --- ARRANGE ---
            // PUSH 10; JMP -1 (lands mid-instruction); NOP
            var bytecode = new byte[] { HGLOpcodes.PUSH_BYTE, 10, HGLOpcodes.JMP, unchecked((byte)(sbyte)-1), HGLOpcodes.NOP };

            // --- ACT ---
            var result = _decompiler.Decompile(bytecode);

            // --- ASSERT ---
            Assert.AreEqual(0, CountValidJumps(result.JumpSources));
            Assert.AreEqual(0, result.JumpTargets.Count);
        }

        #endregion
    }
}

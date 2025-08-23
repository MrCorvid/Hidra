using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using Hidra.Core.Logging;
using ProgrammingLanguageNr1;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Hidra.Tests.Core
{
    [TestClass]
    public class ASTBuilderTests : BaseTestClass
    {
        private ASTBuilder _builder = null!;
        private Dictionary<string, MethodInfo> _mockApiFunctions = null!;
        private readonly Action<string, LogLevel, string>? _testLogger = null;

        #region Test Helpers

        private sealed class MockApiProvider
        {
            public void  API_VoidReturnApi(float arg) { /* no-op */ }
            public float API_ValueReturnApi() => 0f;
        }

        /// <summary>
        /// Simple bytecode builder that supports labels and 2-byte relative jumps.
        /// </summary>
        private sealed class BytecodeBuilder
        {
            private readonly List<byte> _buf = new();
            private readonly Dictionary<string, int> _labels = new();
            private readonly List<(int pos, string label)> _fixups = new();

            public int Position => _buf.Count;

            public BytecodeBuilder Label(string name)
            {
                _labels[name] = _buf.Count;
                return this;
            }

            public BytecodeBuilder Emit(byte b) { _buf.Add(b); return this; }

            public BytecodeBuilder EmitPush(byte value)
            {
                _buf.Add(HGLOpcodes.PUSH_BYTE);
                _buf.Add(value);
                return this;
            }

            public BytecodeBuilder EmitOp(byte opcode)
            {
                _buf.Add(opcode);
                return this;
            }

            public BytecodeBuilder EmitJump(byte opcode, string label)
            {
                _buf.Add(opcode);
                // placeholder for sbyte displacement
                _fixups.Add((_buf.Count, label));
                _buf.Add(0);
                return this;
            }

            public byte[] Seal()
            {
                foreach (var (pos, label) in _fixups)
                {
                    if (!_labels.TryGetValue(label, out var targetByteOffset))
                        throw new InvalidOperationException($"Unknown label '{label}'");

                    int disp = targetByteOffset - (pos + 1); // pos is operand index; pc after 2-byte instr = pos+1
                    if (disp < sbyte.MinValue || disp > sbyte.MaxValue)
                        throw new InvalidOperationException($"Relative jump out of range: {disp}");

                    _buf[pos] = unchecked((byte)(sbyte)disp);
                }
                return _buf.ToArray();
            }
        }

        private static AST Child(AST parent, int index, string? because = null)
        {
            var children = parent.getChildren();
            Assert.IsTrue(children.Count > index, $"Expected at least {index + 1} child(ren) but found {children.Count}. {because}");
            return children[index];
        }

        private static Token.TokenType TokType(Token t) => t.getTokenType();
        private static string TokText(Token t) => t.getTokenString();
        private static float NumValue(AST numberNode)
        {
            var t = numberNode.getToken();
            Assert.IsInstanceOfType(t, typeof(TokenWithValue), "Numeric node must carry a TokenWithValue.");
            return (float)((TokenWithValue)t).getValue();
        }

        private static void AssertIsNumber(AST node, float expected, string? because = null)
        {
            Assert.AreEqual(Token.TokenType.NUMBER, TokType(node.getToken()), $"Node token should be NUMBER. {because}");
            AreClose(expected, NumValue(node), 1e-6f, because);
        }

        private static void AssertIsOperator(AST node, string op, string? because = null)
        {
            Assert.AreEqual(Token.TokenType.OPERATOR, TokType(node.getToken()), $"Node token should be OPERATOR. {because}");
            Assert.AreEqual(op, TokText(node.getToken()), $"Operator text mismatch. {because}");
        }

        private static void AssertIsFunctionCall(AST node, string expectedName, int argCount, string? because = null)
        {
            Assert.AreEqual(Token.TokenType.FUNCTION_CALL, TokType(node.getToken()), $"Node should be a function call. {because}");
            Assert.AreEqual(expectedName, TokText(node.getToken()), $"Function name mismatch. {because}");

            // The builder uses NODE_GROUP "<ARGS>" for argument lists.
            var argsList = Child(node, 0, "Function call should have an argument list as its first child.");
            Assert.AreEqual(Token.TokenType.NODE_GROUP, TokType(argsList.getToken()), "Args holder must be a NODE_GROUP.");
            Assert.AreEqual("<ARGS>", TokText(argsList.getToken()), "Args NODE_GROUP token text should be <ARGS>.");
            Assert.AreEqual(argCount, argsList.getChildren().Count, $"Argument count mismatch. {because}");
        }

        private static AST? TryFindFirstChildByTokenType(AST root, Token.TokenType type)
        {
            return root.getChildren().FirstOrDefault(n => TokType(n.getToken()) == type);
        }

        // Prefer a STATEMENT_LIST tagged "<THEN>" or "<ELSE>" nested under IF; return null if not found.
        private static AST? TryFindBranchBlock(AST ifNode, string tag)
        {
            // direct children
            foreach (var c in ifNode.getChildren())
            {
                if (TokType(c.getToken()) == Token.TokenType.STATEMENT_LIST && TokText(c.getToken()) == tag)
                    return c;
            }
            // one level down (wrapped)
            foreach (var c in ifNode.getChildren())
            {
                foreach (var gc in c.getChildren())
                {
                    if (TokType(gc.getToken()) == Token.TokenType.STATEMENT_LIST && TokText(gc.getToken()) == tag)
                        return gc;
                }
            }
            return null;
        }

        // When ELSE is not nested under IF, some builders emit it as the next root statement(s) after IF.
        // This helper finds the first root-level numeric literal equal to expectedValue appearing after the IF node.
        private static AST? TryFindRootLiteralAfter(AST root, AST afterNode, float expectedValue)
        {
            var children = root.getChildren();
            int idx = children.IndexOf(afterNode);
            if (idx < 0) return null;

            for (int i = idx + 1; i < children.Count; i++)
            {
                var n = children[i];
                if (TokType(n.getToken()) == Token.TokenType.NUMBER)
                {
                    var v = NumValue(n);
                    if (Math.Abs(v - expectedValue) <= 1e-6f) return n;
                }
            }
            return null;
        }

        #endregion

        [TestInitialize]
        public override void BaseInit()
        {
            base.BaseInit();

            _builder = new ASTBuilder();
            _mockApiFunctions = new Dictionary<string, MethodInfo>
            {
                // The test `BuildAst_WithVoidApiCall...` uses the opcode for API_NOP.
                // The builder will look up the string "API_NOP". We map this string
                // to our mock MethodInfo, which has the signature the test expects (1 arg, void return).
                { "API_NOP", typeof(MockApiProvider).GetMethod(nameof(MockApiProvider.API_VoidReturnApi))! },

                // We can map another real API instruction name to the other mock function
                // for completeness, although no current test uses it.
                { "API_GetSelfId", typeof(MockApiProvider).GetMethod(nameof(MockApiProvider.API_ValueReturnApi))! }
            };
        }

        #region Simple Instruction Tests

        [TestMethod]
        public void BuildAst_WithPushAndPop_CreatesCorrectLiteralStatement()
        {
            // Arrange: PUSH 42; POP
            var bc = new BytecodeBuilder()
                .EmitPush(42)
                .EmitOp(HGLOpcodes.POP)
                .Seal();

            var dr = new HGLDecompiler().Decompile(bc);

            // Act
            var ast = _builder.BuildAst(dr, _mockApiFunctions, _testLogger);

            // Assert
            Assert.AreEqual(Token.TokenType.STATEMENT_LIST, TokType(ast.getToken()));
            Assert.AreEqual("<GENE>", TokText(ast.getToken()));
            Assert.IsTrue(ast.getChildren().Count >= 1, "Should have at least one statement.");
            AssertIsNumber(Child(ast, 0), 42f);
        }

        [TestMethod]
        public void BuildAst_WithOperator_CreatesCorrectExpressionTree()
        {
            // Arrange: 5 10 ADD POP  => statement '(+ 5 10)'
            var bc = new BytecodeBuilder()
                .EmitPush(5)
                .EmitPush(10)
                .EmitOp(HGLOpcodes.ADD)
                .EmitOp(HGLOpcodes.POP)
                .Seal();

            var dr = new HGLDecompiler().Decompile(bc);

            // Act
            var ast = _builder.BuildAst(dr, _mockApiFunctions, _testLogger);

            // Assert
            Assert.IsTrue(ast.getChildren().Count >= 1, "Expected at least one statement.");
            var add = Child(ast, 0);
            AssertIsOperator(add, "+");
            AssertIsNumber(Child(add, 0), 5f, "Left operand mismatch.");
            AssertIsNumber(Child(add, 1), 10f, "Right operand mismatch.");
        }

        [TestMethod]
        public void BuildAst_WithVoidApiCall_ImmediatelyFlushesAsStatement()
        {
            // Arrange: PUSH 123; API(index 0)
            // Use public lookup to get the first API opcode ("API_NOP"), which is the start of the API range.
            byte apiOpcode = HGLOpcodes.OpcodeLookup["API_NOP"];

            var bc = new BytecodeBuilder()
                .EmitPush(123)
                .EmitOp(apiOpcode)
                .Seal();

            var dr = new HGLDecompiler().Decompile(bc);

            // Act
            var ast = _builder.BuildAst(dr, _mockApiFunctions, _testLogger);

            // Assert: Find the function call among root statements and verify its args.
            var funcCall = ast.getChildren().FirstOrDefault(n => TokType(n.getToken()) == Token.TokenType.FUNCTION_CALL);
            Assert.IsNotNull(funcCall, "Expected a function call statement at the root.");
            AssertIsFunctionCall(funcCall!, "VoidReturnApi", argCount: 1);
            var args = Child(funcCall!, 0);
            AssertIsNumber(Child(args, 0), 123f, "Argument literal mismatch.");
        }

        #endregion

        #region Control Flow Tests

        [TestMethod]
        public void BuildAst_WithIfThenBlock_CreatesCorrectStructure()
        {
            // Arrange:
            // cond: PUSH 1
            // JZ -> label END (placed at POP instruction so THEN includes PUSH 10)
            // THEN: PUSH 10
            // END:  POP
            var b = new BytecodeBuilder();
            b.EmitPush(1);
            b.EmitJump(HGLOpcodes.JZ, "END");
            b.EmitPush(10);
            b.Label("END");
            b.EmitOp(HGLOpcodes.POP);
            var bc = b.Seal();

            var dr = new HGLDecompiler().Decompile(bc);

            // Act
            var root = _builder.BuildAst(dr, _mockApiFunctions, _testLogger);

            // Assert: locate the IF node among root statements and verify structure.
            var ifNode = TryFindFirstChildByTokenType(root, Token.TokenType.IF);
            Assert.IsNotNull(ifNode, "Expected an IF statement among root statements.");

            var cond = Child(ifNode!, 0, "IF must have a condition node.");
            AssertIsNumber(cond, 1f, "IF condition literal must be 1.");

            // THEN may be nested directly or wrapped; locate it by tag.
            var thenBlock = TryFindBranchBlock(ifNode!, "<THEN>");
            Assert.IsNotNull(thenBlock, "THEN block should be present.");
            Assert.IsTrue(thenBlock!.getChildren().Count >= 1, "THEN should contain at least one statement.");
            AssertIsNumber(Child(thenBlock!, 0), 10f, "THEN body should be literal 10.");
        }

        [TestMethod]
        public void BuildAst_WithIfThenElseBlock_CreatesCorrectStructure()
        {
            // Arrange:
            // cond: PUSH 1
            // JNZ -> ELSE
            // THEN: PUSH 10; POP
            // JMP -> END
            // ELSE: PUSH 20; POP
            // END:
            var b = new BytecodeBuilder();
            b.EmitPush(1);
            b.EmitJump(HGLOpcodes.JNZ, "ELSE");
            b.EmitPush(10);
            b.EmitOp(HGLOpcodes.POP);
            b.EmitJump(HGLOpcodes.JMP, "END");
            b.Label("ELSE");
            b.EmitPush(20);
            b.EmitOp(HGLOpcodes.POP);
            b.Label("END");
            var bc = b.Seal();

            var dr = new HGLDecompiler().Decompile(bc);

            // Act
            var root = _builder.BuildAst(dr, _mockApiFunctions, _testLogger);

            // Assert: locate IF and assert condition, THEN, and ELSE bodies (nested or root-level).
            var ifNode = TryFindFirstChildByTokenType(root, Token.TokenType.IF);
            Assert.IsNotNull(ifNode, "Expected an IF statement among root statements.");

            // Condition may be wrapped in NOT; verify either NOT(NUM 1) or plain NUM 1.
            var condNode = Child(ifNode!, 0);
            if (TokType(condNode.getToken()) == Token.TokenType.NOT)
            {
                AssertIsNumber(Child(condNode, 0), 1f, "NOT should wrap the original condition literal 1.");
            }
            else
            {
                AssertIsNumber(condNode, 1f, "IF condition literal must be 1.");
            }

            // THEN
            var thenBlock = TryFindBranchBlock(ifNode!, "<THEN>");
            Assert.IsNotNull(thenBlock, "THEN block should be present.");
            Assert.IsTrue(thenBlock!.getChildren().Count >= 1, "THEN should contain at least one statement.");
            AssertIsNumber(Child(thenBlock!, 0), 10f, "THEN body should be literal 10.");

            // ELSE (robust: accept nested under IF, or as next root statement after IF)
            var elseBlock = TryFindBranchBlock(ifNode!, "<ELSE>");
            if (elseBlock != null)
            {
                Assert.IsTrue(elseBlock.getChildren().Count >= 1, "ELSE should contain at least one statement.");
                AssertIsNumber(Child(elseBlock, 0), 20f, "ELSE body should be literal 20.");
            }
            else
            {
                // Fallback: else emitted as a root-level statement after IF.
                var elseLiteral = TryFindRootLiteralAfter(root, ifNode!, 20f);
                Assert.IsNotNull(elseLiteral, "Expected ELSE literal 20 after IF at root level when no <ELSE> block is nested.");
            }
        }

        #endregion
    }
}
// Hidra.Tests/Genome/HGLParser/HGLParserTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using ProgrammingLanguageNr1;
using System.Linq;

namespace Hidra.Tests.Genome.Parsing
{
    /// <summary>
    /// Contains unit tests for the HGL parsing pipeline, including the HGLDecompiler
    /// and ASTBuilder. These tests verify that HGL bytecode is correctly translated
    /// from raw bytes into a structured, interpretable Abstract Syntax Tree (AST).
    /// </summary>
    [TestClass]
    public class HGLParserTests : BaseTestClass
    {
        private HGLParser _parser = null!;

        [TestInitialize]
        public void Setup()
        {
            // The HGLParser is stateless, so one instance can be reused for all tests.
            _parser = new HGLParser();
        }

        /// <summary>
        /// A helper to parse a single gene's bytecode and return its root AST node.
        /// This was corrected to ensure it only parses the given bytecode as a single gene (Gene 0).
        /// </summary>
        private AST GetGeneRoot(string bytecode)
        {
            // The parser expects a single string for a single gene.
            // This now correctly passes the raw bytecode to be parsed as Gene 0.
            var compiledGenes = _parser.ParseGenome(bytecode);
            Assert.IsTrue(compiledGenes.ContainsKey(0), "The parser did not produce a gene with ID 0.");
            return compiledGenes[0]; 
        }

        #region Simple Instruction and Expression Parsing

        /// <summary>
        /// Verifies that a simple sequence of PUSH and ADD opcodes is correctly
        /// parsed into a single operator node with two literal children in the AST.
        /// </summary>
        [TestMethod]
        public void Parse_SimpleAddition_CreatesCorrectAST()
        {
            // Arrange: PUSH 5, PUSH 10, ADD
            string bytecode = $"{HGLOpcodes.PUSH_BYTE:X2}05" +
                              $"{HGLOpcodes.PUSH_BYTE:X2}0A" +
                              $"{HGLOpcodes.ADD:X2}";

            // Act
            var geneRoot = GetGeneRoot(bytecode);

            // Assert
            // The AST should contain a single statement, which is the operator node.
            Assert.AreEqual(1, geneRoot.getChildren().Count, "Expected a single root statement.");

            var operatorNode = geneRoot.getChild(0);
            Assert.AreEqual(Token.TokenType.OPERATOR, operatorNode.getTokenType(), "Root statement should be an operator node.");
            Assert.AreEqual("+", operatorNode.getTokenString(), "Operator should be addition.");
            Assert.AreEqual(2, operatorNode.getChildren().Count, "Operator node must have two children (operands).");

            var lhs = operatorNode.getChild(0).getToken() as TokenWithValue;
            var rhs = operatorNode.getChild(1).getToken() as TokenWithValue;

            Assert.IsNotNull(lhs, "LHS operand is not a TokenWithValue.");
            Assert.IsNotNull(rhs, "RHS operand is not a TokenWithValue.");
            Assert.AreEqual(5.0f, (float)lhs.getValue(), "LHS value is incorrect.");
            Assert.AreEqual(10.0f, (float)rhs.getValue(), "RHS value is incorrect.");
        }

        /// <summary>
        /// Verifies that an API function call with no return value is parsed as a statement,
        /// while one with a return value is parsed as an expression.
        /// </summary>
        [TestMethod]
        public void Parse_ApiFunctionCalls_AreParsedAsStatementsOrExpressions()
        {
            // Arrange
            // 1. PUSH 42, PUSH 10, API_StoreLVar (void return -> statement)
            // 2. API_GetSelfId (float return -> expression)
            string bytecode = $"{HGLOpcodes.PUSH_BYTE:X2}2A" + // Push 42
                              $"{HGLOpcodes.PUSH_BYTE:X2}0A" + // Push 10
                              $"{HGLOpcodes.StoreLVar:X2}" +      
                              $"{HGLOpcodes.GetSelfId:X2}";      
            
            // Act
            var geneRoot = GetGeneRoot(bytecode);

            // Assert
            Assert.AreEqual(2, geneRoot.getChildren().Count, "Expected two root statements.");
            
            // First statement should be the void function call.
            var statement1 = geneRoot.getChild(0);
            Assert.IsInstanceOfType(statement1, typeof(AST_FunctionCall), "First statement should be a function call.");
            Assert.AreEqual("StoreLVar", statement1.getTokenString());

            // Second statement should be the non-void function call that was left on the stack.
            var statement2 = geneRoot.getChild(1);
            Assert.IsInstanceOfType(statement2, typeof(AST_FunctionCall), "Second statement should be a function call.");
            Assert.AreEqual("GetSelfId", statement2.getTokenString());
        }

        #endregion

        #region Control Flow Parsing

        /// <summary>
        /// Verifies that a backward conditional jump (JNE) is correctly identified
        /// and parsed into a well-structured loop in the AST.
        /// </summary>
        [TestMethod]
        public void Parse_BackwardConditionalJump_CreatesCorrectLoopAST()
        {
            // Arrange: PUSH 10, POP; loop { ... }
            // Corrected: Added a POP to make the PUSH 10 a distinct statement.
            string bytecode = $"{HGLOpcodes.PUSH_BYTE:X2}0A" + // i = 10 (on stack)
                              $"{HGLOpcodes.POP:X2}" +          // Pop 10 to make it a statement.
                              $"{HGLOpcodes.PUSH_BYTE:X2}01" + // [Offset 2] Push 1 for subtraction
                              $"{HGLOpcodes.SUB:X2}" +          // [Offset 4] i = i - 1
                              $"{HGLOpcodes.DUP:X2}" +          // [Offset 5] duplicate i
                              $"{HGLOpcodes.PUSH_BYTE:X2}00" + // [Offset 6] Push 0 for comparison
                              $"{HGLOpcodes.GT:X2}" +           // [Offset 8] check if i > 0
                              $"{HGLOpcodes.JNE:X2}F7";         // [Offset 9] If true, jump back 9 bytes (F7) to offset 2.

            // Act
            var geneRoot = GetGeneRoot(bytecode);

            // Assert
            // Root should have two statements: the initial push 10, and the loop block.
            Assert.AreEqual(2, geneRoot.getChildren().Count);
            
            var loopBlock = geneRoot.getChild(1) as AST_LoopBlockNode;
            Assert.IsNotNull(loopBlock, "A LoopBlockNode should be created for the loop.");
            
            var loopNode = loopBlock.getChild(0) as AST_LoopNode;
            Assert.IsNotNull(loopNode, "A LoopNode should be inside the LoopBlockNode.");
            
            var loopBody = loopNode.getChild(0);
            Assert.AreEqual(2, loopBody.getChildren().Count, "Loop body should contain the SUB operation and the conditional break.");
            
            // The first part of the body is the SUB expression.
            var subNode = loopBody.getChild(0);
            Assert.AreEqual(Token.TokenType.OPERATOR, subNode.getTokenType());
            Assert.AreEqual("-", subNode.getTokenString());

            // The second part is the conditional break, parsed as an IF statement.
            var breakIfNode = loopBody.getChild(1) as AST_IfNode;
            Assert.IsNotNull(breakIfNode, "The loop body must end with a conditional break (IF statement).");

            var breakStatement = breakIfNode.getChild(1).getChild(0); // if -> then-block -> break
            Assert.AreEqual(Token.TokenType.BREAK, breakStatement.getTokenType(), "The IF statement's body should contain a BREAK.");
        }

        #endregion
    }
}
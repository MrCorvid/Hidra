// Hidra.Core/Genome/HGLParser/ASTBuilder.cs
using Hidra.Core.Logging;
using ProgrammingLanguageNr1;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using static Hidra.Core.HGLDecompiler;

namespace Hidra.Core
{
    /// <summary>
    /// Builds a structured Abstract Syntax Tree (AST) from a linear list of decompiled HGL instructions.
    /// This is the second and final pass of the HGL parsing process.
    /// </summary>
    public class ASTBuilder
    {
        /// <summary>
        /// Builds the AST for a single gene.
        /// </summary>
        /// <param name="decompileResult">The result from the HGLDecompiler pass, containing instructions and jump information.</param>
        /// <param name="apiFunctions">The ordered list of API methods available to the HGL virtual machine.</param>
        /// <returns>The root <see cref="AST"/> node for the gene.</returns>
        public AST BuildAst(DecompileResult decompileResult, IReadOnlyList<MethodInfo> apiFunctions)
        {
            Logger.Log("PARSER", LogLevel.Info, "--- Building AST ---");

            var rootStatements = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<GENE>"));
            
            // The top-level call to BuildBlock will process all instructions and populate the root statement list.
            BuildBlock(0, decompileResult.Instructions.Count, new Stack<AST>(), rootStatements, decompileResult, apiFunctions);

            Logger.Log("PARSER", LogLevel.Info, $"--- AST Building Complete. Root has {rootStatements.getChildren().Count} statements. ---");
            return rootStatements;
        }

        /// <summary>
        /// Recursively builds a block of statements, identifying and parsing control flow structures.
        /// </summary>
        /// <returns>The index of the instruction immediately following the processed block.</returns>
        private int BuildBlock(int start, int end, Stack<AST> expressionStack, AST statements, DecompileResult dr, IReadOnlyList<MethodInfo> api)
        {
            Logger.Log("PARSER", LogLevel.Debug, $"BuildBlock started. Range: [{start}, {end}). Stack depth: {expressionStack.Count}");

            int i = start;
            while (i < end)
            {
                var currentInstruction = dr.Instructions[i];
                Logger.Log("PARSER", LogLevel.Debug, $"  Processing instruction at index {i}: {currentInstruction}");
                
                // Heuristic: A jump target inside the current block that comes from a JUMP *after* it indicates a loop.
                if (dr.JumpTargets.TryGetValue(i, out var sources) && sources.Any(s => s > i && s < end))
                {
                    int loopEndIndex = sources.First(s => s > i);
                    Logger.Log("PARSER", LogLevel.Info, $"  >> Loop detected. Target: {i}, End: {loopEndIndex}. Parsing loop...");

                    FlushStackToStatements(expressionStack, statements);
                    statements.addChild(ParseLoop(i, loopEndIndex, dr, api));
                    i = loopEndIndex + 1; // Continue processing after the loop's jump instruction.
                    continue;
                }

                if (IsConditionalJump(currentInstruction.Opcode) && dr.JumpSources.TryGetValue(i, out int target) && target > i)
                {
                    Logger.Log("PARSER", LogLevel.Info, $"  >> If block detected. Condition at {i}, end at {target}. Parsing if...");
                    
                    FlushStackToStatements(expressionStack, statements);
                    statements.addChild(ParseIf(ref i, expressionStack, dr, api));
                    // The 'i' pointer is advanced by ParseIf, so we just continue the loop.
                    continue;
                }

                ProcessSimpleInstruction(currentInstruction, expressionStack, statements, api);
                i++;
            }
            
            // At the end of any block, all expressions left on the stack are finalized into statements.
            // This is crucial for handling expressions at the end of a gene or control-flow block.
            FlushStackToStatements(expressionStack, statements);
            Logger.Log("PARSER", LogLevel.Debug, $"BuildBlock finished. Range: [{start}, {end}).");
            return i;
        }

        /// <summary>
        /// Parses an if/else control structure.
        /// </summary>
        /// <param name="i">The current instruction pointer, passed by reference to be updated.</param>
        private AST ParseIf(ref int i, Stack<AST> parentStack, DecompileResult dr, IReadOnlyList<MethodInfo> api)
        {
            var ifInstruction = dr.Instructions[i];
            int thenBlockEndIndex = dr.JumpSources[i];
            
            // Pop the condition expression from the stack. If stack is empty, it's a malformed script.
            var condition = parentStack.Count > 0 ? parentStack.Pop() : CreateLiteralFloatNode(1.0f);
            
            // Invert the condition for JZ/JNZ, as they jump when the condition is NOT met.
            if (ifInstruction.Opcode == HGLOpcodes.JZ || ifInstruction.Opcode == HGLOpcodes.JNZ)
            {
                condition = CreateNotNode(condition);
            }

            var ifNode = new AST_IfNode(new Token(Token.TokenType.IF, "if"));
            ifNode.addChild(condition);

            int elseBlockStartIndex = -1;
            int elseBlockEndIndex = -1;

            // Check for an 'else' block, indicated by an unconditional JMP at the end of the 'then' block.
            if (thenBlockEndIndex < dr.Instructions.Count && dr.Instructions[thenBlockEndIndex].Opcode == HGLOpcodes.JMP)
            {
                if (dr.JumpSources.TryGetValue(thenBlockEndIndex, out int finalTarget))
                {
                    elseBlockStartIndex = thenBlockEndIndex + 1;
                    elseBlockEndIndex = finalTarget;
                }
            }
            
            // Recursively build the 'then' block.
            var thenStatements = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<THEN>"));
            BuildBlock(i + 1, elseBlockStartIndex != -1 ? elseBlockStartIndex - 1 : thenBlockEndIndex, new Stack<AST>(), thenStatements, dr, api);
            ifNode.addChild(thenStatements);

            // If an 'else' block was found, recursively build it.
            if (elseBlockStartIndex != -1)
            {
                var elseStatements = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<ELSE>"));
                BuildBlock(elseBlockStartIndex, elseBlockEndIndex, new Stack<AST>(), elseStatements, dr, api);
                ifNode.addChild(elseStatements);
                i = elseBlockEndIndex; // Advance instruction pointer past the entire if-else structure.
            }
            else
            {
                i = thenBlockEndIndex; // Advance past the 'then' block.
            }
            
            return ifNode;
        }

        /// <summary>
        /// Parses a loop control structure.
        /// </summary>
        private AST ParseLoop(int startIndex, int loopEndIndex, DecompileResult dr, IReadOnlyList<MethodInfo> api)
        {
            var loopNode = new AST_LoopNode(new Token(Token.TokenType.LOOP, "loop"));
            var bodyStatements = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<LOOP_BODY>"));
            
            // Recursively build the loop's body.
            BuildBlock(startIndex, loopEndIndex, new Stack<AST>(), bodyStatements, dr, api);
            
            var jumpInstruction = dr.Instructions[loopEndIndex];
            
            // The last statement in the loop body is the exit condition.
            if (bodyStatements.getChildren().Any())
            {
                AST condition = bodyStatements.getChildren().Last();
                bodyStatements.removeChild(bodyStatements.getChildren().Count - 1);
                
                var breakIfNode = new AST_IfNode(new Token(Token.TokenType.IF, "if"));
                
                // JZ/JNZ break the loop when the condition is NOT met.
                if (jumpInstruction.Opcode == HGLOpcodes.JZ || jumpInstruction.Opcode == HGLOpcodes.JNZ)
                {
                    breakIfNode.addChild(CreateNotNode(condition));
                }
                else
                {
                    breakIfNode.addChild(condition);
                }

                var breakBlock = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<BREAK_BLOCK>"));
                breakBlock.addChild(new AST(new Token(Token.TokenType.BREAK, "break")));
                breakIfNode.addChild(breakBlock);
                bodyStatements.addChild(breakIfNode);
            }
            
            loopNode.addChild(bodyStatements);

            // Wrap the loop in a block, as expected by the interpreter.
            var loopBlock = new AST_LoopBlockNode(new Token(Token.TokenType.LOOP_BLOCK, "<LOOP_BLOCK>"));
            loopBlock.addChild(loopNode);
            return loopBlock;
        }

        /// <summary>
        /// Processes a single, non-control-flow instruction.
        /// </summary>
        private void ProcessSimpleInstruction(Instruction instr, Stack<AST> expressionStack, AST statements, IReadOnlyList<MethodInfo> api)
        {
            if (instr.Opcode >= HGLOpcodes.ApiOpcodeStart && instr.Opcode < HGLOpcodes.OperatorOpcodeStart)
            {
                int apiIndex = instr.Opcode - HGLOpcodes.ApiOpcodeStart;
                var methodToCall = api[apiIndex];
                string sprakFunctionName = methodToCall.Name.Replace("API_", "");
                
                var functionCall = new AST_FunctionCall(new Token(Token.TokenType.FUNCTION_CALL, sprakFunctionName));
                var argList = new AST(new Token(Token.TokenType.NODE_GROUP, "<ARGS>"));
                
                var parameters = methodToCall.GetParameters();
                for (int i = 0; i < parameters.Length; i++)
                {
                    argList.addChildFirst(expressionStack.Count > 0 ? expressionStack.Pop() : CreateLiteralFloatNode(0.0f));
                }

                functionCall.addChild(argList);
                
                // CORRECTED LOGIC:
                // All function calls are expressions and are pushed to the stack.
                expressionStack.Push(functionCall);

                // If a function returns void, it's a sequence point. It cannot be part
                // of a larger expression, so we finalize it (and any preceding
                // expressions) into a statement immediately. This is equivalent to
                // an implicit POP instruction following a void function call.
                if (methodToCall.ReturnType == typeof(void))
                {
                    FlushStackToStatements(expressionStack, statements);
                }
            }
            else if (instr.Opcode >= HGLOpcodes.OperatorOpcodeStart && instr.Opcode < HGLOpcodes.JZ)
            {
                var b = expressionStack.Count > 0 ? expressionStack.Pop() : CreateLiteralFloatNode(0f);
                var a = expressionStack.Count > 0 ? expressionStack.Pop() : CreateLiteralFloatNode(0f);
                string? opStr = HGLOpcodes.OperatorLookup.FirstOrDefault(x => x.Value == instr.Opcode).Key;
                if (opStr != null)
                {
                    var opNode = new AST(new Token(Token.TokenType.OPERATOR, opStr));
                    opNode.addChild(a);
                    opNode.addChild(b);
                    expressionStack.Push(opNode);
                }
            }
            else if (instr.Opcode == HGLOpcodes.PUSH_BYTE)
            {
                expressionStack.Push(CreateLiteralFloatNode(instr.Operand));
            }
            else if (instr.Opcode == HGLOpcodes.DUP)
            {
                if (expressionStack.Count > 0) expressionStack.Push(expressionStack.Peek());
            }
            else if (instr.Opcode == HGLOpcodes.POP)
            {
                // The POP instruction is a sequence point. It finalizes all expressions
                // currently on the stack, converting them into statements in FIFO order.
                FlushStackToStatements(expressionStack, statements);
            }
        }
        
        #region Helper Methods
        
        /// <summary>
        /// Empties the expression stack, converting each expression into a statement
        /// and appending it to a statement list in the correct evaluation order.
        /// </summary>
        private void FlushStackToStatements(Stack<AST> stack, AST statementList)
        {
            if (stack.Count == 0) return;

            // Expressions on the stack need to be added as statements in the order
            // they were pushed. Iterating over the stack normally (or popping) gives
            // a LIFO order. To get the original FIFO order, we use LINQ's Reverse()
            // which iterates from the bottom of the stack to the top.
            foreach (var expression in stack.Reverse())
            {
                statementList.addChild(expression);
            }
            
            stack.Clear();
        }
        
        private bool IsConditionalJump(byte opcode) => opcode >= HGLOpcodes.JZ && opcode <= HGLOpcodes.JNE;

        private AST CreateLiteralFloatNode(float value) => new AST(new TokenWithValue(Token.TokenType.NUMBER, value.ToString(CultureInfo.InvariantCulture), value));
        
        private AST CreateNotNode(AST condition)
        {
            var notNode = new AST(new Token(Token.TokenType.NOT, "!"));
            notNode.addChild(condition);
            return notNode;
        }

        #endregion
    }
}
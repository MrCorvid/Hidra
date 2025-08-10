// Hidra.Core/Genome/HGLParser/ASTBuilder.cs
namespace Hidra.Core;

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Hidra.Core.Logging;
using ProgrammingLanguageNr1;
using static Hidra.Core.HGLDecompiler;

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
        
        BuildBlock(0, decompileResult.Instructions.Count, new Stack<AST>(), rootStatements, decompileResult, apiFunctions);

        Logger.Log("PARSER", LogLevel.Info, $"--- AST Building Complete. Root has {rootStatements.getChildren().Count} statements. ---");
        return rootStatements;
    }

    /// <summary>
    /// Recursively builds a block of statements from a range of instructions, identifying and parsing control flow structures.
    /// </summary>
    /// <param name="start">The starting instruction index (inclusive).</param>
    /// <param name="end">The ending instruction index (exclusive).</param>
    /// <param name="expressionStack">The current stack of expression AST nodes.</param>
    /// <param name="statements">The statement list AST node to append new statements to.</param>
    /// <param name="dr">The decompiled gene data.</param>
    /// <param name="api">The list of available API functions.</param>
    /// <returns>The index of the instruction immediately following the processed block.</returns>
    private static int BuildBlock(int start, int end, Stack<AST> expressionStack, AST statements, DecompileResult dr, IReadOnlyList<MethodInfo> api)
    {
        Logger.Log("PARSER", LogLevel.Debug, $"BuildBlock started. Range: [{start}, {end}). Stack depth: {expressionStack.Count}");

        var i = start;
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
                i = loopEndIndex + 1;
                continue;
            }

            if (IsConditionalJump(currentInstruction.Opcode) && dr.JumpSources.TryGetValue(i, out var target) && target > i)
            {
                Logger.Log("PARSER", LogLevel.Info, $"  >> If block detected. Condition at {i}, end at {target}. Parsing if...");
                
                FlushStackToStatements(expressionStack, statements);
                statements.addChild(ParseIf(ref i, expressionStack, dr, api));
                continue;
            }

            ProcessSimpleInstruction(currentInstruction, expressionStack, statements, api);
            i++;
        }
        
        FlushStackToStatements(expressionStack, statements);
        Logger.Log("PARSER", LogLevel.Debug, $"BuildBlock finished. Range: [{start}, {end}).");
        return i;
    }

    /// <summary>
    /// Parses an if/else control structure from the instruction stream.
    /// </summary>
    /// <param name="i">The current instruction pointer, passed by reference to be updated past the parsed structure.</param>
    /// <param name="parentStack">The expression stack of the parent block.</param>
    /// <param name="dr">The decompiled gene data.</param>
    /// <param name="api">The list of available API functions.</param>
    /// <returns>An <see cref="AST_IfNode"/> representing the parsed structure.</returns>
    private static AST ParseIf(ref int i, Stack<AST> parentStack, DecompileResult dr, IReadOnlyList<MethodInfo> api)
    {
        var ifInstruction = dr.Instructions[i];
        int thenBlockEndIndex = dr.JumpSources[i];
        
        var condition = parentStack.Count > 0 ? parentStack.Pop() : CreateLiteralFloatNode(1.0f);
        
        // JNZ (Jump if Not Zero) jumps when the condition is true, skipping the 'then' block.
        // So, the 'then' block should execute when the condition is false. We invert it to match if-node semantics.
        if (ifInstruction.Opcode == HGLOpcodes.JNZ)
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
        
        var thenStatements = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<THEN>"));
        BuildBlock(i + 1, elseBlockStartIndex != -1 ? elseBlockStartIndex - 1 : thenBlockEndIndex, new Stack<AST>(), thenStatements, dr, api);
        ifNode.addChild(thenStatements);

        if (elseBlockStartIndex != -1)
        {
            var elseStatements = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<ELSE>"));
            BuildBlock(elseBlockStartIndex, elseBlockEndIndex, new Stack<AST>(), elseStatements, dr, api);
            ifNode.addChild(elseStatements);
            i = elseBlockEndIndex;
        }
        else
        {
            i = thenBlockEndIndex;
        }
        
        return ifNode;
    }

    /// <summary>
    /// Parses a loop control structure from the instruction stream.
    /// </summary>
    private static AST ParseLoop(int startIndex, int loopEndIndex, DecompileResult dr, IReadOnlyList<MethodInfo> api)
    {
        var loopNode = new AST_LoopNode(new Token(Token.TokenType.LOOP, "loop"));
        var bodyStatements = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<LOOP_BODY>"));
        
        BuildBlock(startIndex, loopEndIndex, new Stack<AST>(), bodyStatements, dr, api);
        
        var jumpInstruction = dr.Instructions[loopEndIndex];
        
        // The last statement in the loop body is the exit condition.
        if (bodyStatements.getChildren().Any())
        {
            var condition = bodyStatements.getChildren().Last();
            bodyStatements.removeChild(bodyStatements.getChildren().Count - 1);
            
            var breakIfNode = new AST_IfNode(new Token(Token.TokenType.IF, "if"));
            
            // The jump instruction breaks the loop. For JZ/JNZ, this happens when the condition is NOT met relative to the loop continuation.
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

        var loopBlock = new AST_LoopBlockNode(new Token(Token.TokenType.LOOP_BLOCK, "<LOOP_BLOCK>"));
        loopBlock.addChild(loopNode);
        return loopBlock;
    }

    /// <summary>
    /// Processes a single, non-control-flow instruction, pushing results onto the expression stack or flushing to a statement list.
    /// </summary>
    /// <remarks>
    /// A key behavior is that function calls returning `void` act as sequence points. They finalize the current expression stack
    /// into a statement, similar to an explicit `POP` instruction.
    /// </remarks>
    private static void ProcessSimpleInstruction(Instruction instr, Stack<AST> expressionStack, AST statements, IReadOnlyList<MethodInfo> api)
    {
        if (instr.Opcode >= HGLOpcodes.ApiOpcodeStart && instr.Opcode < HGLOpcodes.OperatorOpcodeStart)
        {
            int apiIndex = instr.Opcode - HGLOpcodes.ApiOpcodeStart;
            var methodToCall = api[apiIndex];
            var sprakFunctionName = methodToCall.Name.Replace("API_", "", StringComparison.Ordinal);
            
            var functionCall = new AST_FunctionCall(new Token(Token.TokenType.FUNCTION_CALL, sprakFunctionName));
            var argList = new AST(new Token(Token.TokenType.NODE_GROUP, "<ARGS>"));
            
            var parameters = methodToCall.GetParameters();
            for (var i = 0; i < parameters.Length; i++)
            {
                argList.addChildFirst(expressionStack.Count > 0 ? expressionStack.Pop() : CreateLiteralFloatNode(0.0f));
            }

            functionCall.addChild(argList);
            expressionStack.Push(functionCall);

            if (methodToCall.ReturnType == typeof(void))
            {
                FlushStackToStatements(expressionStack, statements);
            }
        }
        else if (instr.Opcode >= HGLOpcodes.OperatorOpcodeStart && instr.Opcode < HGLOpcodes.JZ)
        {
            var b = expressionStack.Count > 0 ? expressionStack.Pop() : CreateLiteralFloatNode(0f);
            var a = expressionStack.Count > 0 ? expressionStack.Pop() : CreateLiteralFloatNode(0f);
            var opStr = HGLOpcodes.OperatorLookup.FirstOrDefault(x => x.Value == instr.Opcode).Key;
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
            // The POP instruction is a sequence point, finalizing expressions into statements.
            FlushStackToStatements(expressionStack, statements);
        }
    }
    
    /// <summary>
    /// Empties the expression stack, converting each expression into a statement
    /// and appending it to a statement list in the correct evaluation order.
    /// </summary>
    /// <remarks>
    /// To preserve the original evaluation order (FIFO), this method reverses the stack
    /// during iteration, as a simple pop would yield LIFO order.
    /// </remarks>
    private static void FlushStackToStatements(Stack<AST> stack, AST statementList)
    {
        if (stack.Count == 0) return;

        foreach (var expression in stack.Reverse())
        {
            statementList.addChild(expression);
        }
        
        stack.Clear();
    }
    
    private static bool IsConditionalJump(byte opcode) => opcode >= HGLOpcodes.JZ && opcode <= HGLOpcodes.JNE;

    private static AST CreateLiteralFloatNode(float value) => new(new TokenWithValue(Token.TokenType.NUMBER, value.ToString(CultureInfo.InvariantCulture), value));
    
    private static AST CreateNotNode(AST condition)
    {
        var notNode = new AST(new Token(Token.TokenType.NOT, "!"));
        notNode.addChild(condition);
        return notNode;
    }
}
// Hidra.Core/ASTBuilder.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Hidra.Core.Logging;
using ProgrammingLanguageNr1;

namespace Hidra.Core
{
    /// <summary>
    /// Builds a Sprak AST from HGL decompiled instructions.
    /// This builder targets the node shapes expected by SprakRunner/InterpreterTwo:
    /// PROGRAM_ROOT
    ///   ├── STATEMENT_LIST "<GENE>"
    ///   │   └── STATEMENT_LIST "<GLOBAL_VARIABLE_DEFINITIONS_LIST>"   (must be the first child)
    ///   └── STATEMENT_LIST "<FUNCTION_DECLARATIONS>"
    ///
    /// Function calls are emitted as:
    ///   FUNCTION_CALL
    ///     ├── NAME("API_Foo")
    ///     └── NODE_GROUP "<ARGS>" (arg0, arg1, ...)
    ///
    /// IF statements are emitted as AST_IfNode with children:
    ///   IF
    ///     ├── <condition expr>
    ///     ├── STATEMENT_LIST "<THEN>"
    ///     └── STATEMENT_LIST "<ELSE>" (optional)
    /// </summary>
    public sealed class ASTBuilder
    {
        private const string ArgsTag = "<ARGS>";
        private const string ThenTag = "<THEN>";
        private const string ElseTag = "<ELSE>";

        private Action<string, LogLevel, string>? _logger;
        private int _tempVarCounter; // Counter for generating unique temporary variable names.

        private string DumpStack(BlockCtx ctx)
        {
            if (ctx.Stack.Count == 0) return "[]";
            var sb = new StringBuilder();
            sb.Append("[ ");
            sb.Append(string.Join(", ", ctx.Stack.Select(n => n.getTokenString())));
            sb.Append(" ]");
            return sb.ToString();
        }

        private void Log(string area, LogLevel level, string message)
        {
            _logger?.Invoke(area, level, message);
        }

        private sealed class BlockCtx
        {
            public readonly HGLDecompiler.DecompileResult DR;
            public readonly List<AST> Stack;
            public readonly Dictionary<string, MethodInfo> ApiMethodsByName;
            public BlockCtx(HGLDecompiler.DecompileResult dr, Dictionary<string, MethodInfo> apiMethodsByName)
            {
                DR = dr;
                ApiMethodsByName = apiMethodsByName;
                Stack = new List<AST>(8);
            }
        }

        public AST BuildAst(HGLDecompiler.DecompileResult dr,
                    Dictionary<string, MethodInfo> apiMethodsByName,
                    Action<string, LogLevel, string>? logger = null)
        {
            _logger = logger;
            _tempVarCounter = 0; // Reset for each gene parse.
            var geneStatements = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<GENE>"));
            var ctx = new BlockCtx(dr, apiMethodsByName);
            BuildBlock(ctx, 0, dr.Instructions.Count, geneStatements, "gene");
            return geneStatements;
        }

        private static bool IsConditional(byte opcode) => opcode == HGLOpcodes.JZ || opcode == HGLOpcodes.JNZ;

        private static bool IsApiOpcode(byte opcode)
        {
            int start = HGLOpcodes.ApiOpcodeStart;
            int end = HGLOpcodes.OperatorOpcodeStart; // API opcodes are between ApiOpcodeStart and OperatorOpcodeStart
            return opcode >= start && opcode < end;
        }

        private static string GetApiNameFromOpcode(byte opcode)
        {
            // Use the opcode lookup to get the instruction name
            var instructionName = HGLOpcodes.MasterInstructionOrder.ElementAtOrDefault(opcode);
            if (instructionName != null && instructionName.StartsWith("API_"))
            {
                return instructionName;
            }
            return $"API_UNKNOWN_{opcode}";
        }

        private static string OpcodeToOperator(byte opcode)
        {
            if (opcode == HGLOpcodes.ADD) return "+";
            if (opcode == HGLOpcodes.SUB) return "-";
            if (opcode == HGLOpcodes.MUL) return "*";
            if (opcode == HGLOpcodes.DIV) return "/";
            if (opcode == HGLOpcodes.MOD) return "%";
            if (opcode == HGLOpcodes.EQ) return "==";
            if (opcode == HGLOpcodes.NEQ) return "!=";
            if (opcode == HGLOpcodes.GT) return ">";
            if (opcode == HGLOpcodes.LT) return "<";
            if (opcode == HGLOpcodes.GTE) return ">=";
            if (opcode == HGLOpcodes.LTE) return "<=";
            return "?";
        }

        private static AST Pop(BlockCtx ctx)
        {
            var node = ctx.Stack[^1];
            ctx.Stack.RemoveAt(ctx.Stack.Count - 1);
            return node;
        }

        private void FlushStackAsStatements(BlockCtx ctx, AST outList, string reason)
        {
            while (ctx.Stack.Count > 0)
            {
                var n = Pop(ctx);
                outList.addChild(n);
            }
        }

        private static int? TargetOf(BlockCtx ctx, int condIndex)
        {
            object? js = ctx.DR.JumpSources;
            if (js is int[] arr)
            {
                if (condIndex >= 0 && condIndex < arr.Length)
                {
                    int t = arr[condIndex];
                    if (t >= 0) return t;
                }
                return null;
            }
            if (js is System.Collections.Generic.Dictionary<int, int> dict)
            {
                return dict.TryGetValue(condIndex, out int t) ? t : (int?)null;
            }
            return null;
        }

        private AST WrapWithNot(AST conditionExpr)
        {
            var notToken = new Token(Token.TokenType.NOT, "NOT");
            var notNode = new AST(notToken);
            notNode.addChild(conditionExpr);
            return notNode;
        }

        private static string NormalizeApiName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return rawName ?? string.Empty;
            return rawName.StartsWith("API_", StringComparison.Ordinal) ? rawName.Substring(4) : rawName;
        }

        private void BuildBlock(BlockCtx ctx, int start, int end, AST outList, string dbg)
        {
            Log("PARSER", LogLevel.Debug, $"BuildBlock started. Range: [{start}, {end}). Stack depth: {ctx.Stack.Count}");
            int i = start;
            while (i < end)
            {
                var ins = ctx.DR.Instructions[i];
                
                Log("PARSER", LogLevel.Debug, $"--> Processing Instruction [Index {i}]: {ins}");
                Log("PARSER", LogLevel.Debug, $"    Stack Before: {DumpStack(ctx)}");

                if (IsConditional(ins.Opcode))
                {
                    if (TryParseIf(ctx, i, end, out int nextI, out AST? ifNode))
                    {
                        FlushStackAsStatements(ctx, outList, "before-if");
                        outList.addChild(ifNode!);
                        i = nextI;
                        continue;
                    }
                }

                if (ins.Opcode == HGLOpcodes.PUSH_BYTE)
                {
                    float value = (float)ins.Operand;
                    string valueString = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var lit = new TokenWithValue(Token.TokenType.NUMBER, valueString, value);
                    ctx.Stack.Add(new AST(lit));
                }
                else if (ins.Opcode == HGLOpcodes.DUP)
                {
                    if (ctx.Stack.Count == 0)
                        ctx.Stack.Add(new AST(new TokenWithValue(Token.TokenType.NUMBER, "0", 0f)));
                    else
                        ctx.Stack.Add(ctx.Stack[^1]);
                }
                else if (ins.Opcode == HGLOpcodes.POP)
                {
                    if (ctx.Stack.Count > 0)
                    {
                        var node = Pop(ctx);
                        Log("PARSER", LogLevel.Debug, $"    POP: Flushing '{node.getTokenString()}' to statements.");
                        outList.addChild(node);
                    }
                }
                else if (ins.Opcode >= HGLOpcodes.ADD && ins.Opcode <= HGLOpcodes.LTE)
                {
                    while (ctx.Stack.Count < 2)
                        ctx.Stack.Add(new AST(new TokenWithValue(Token.TokenType.NUMBER, "0", 0f)));
                    var rhs = Pop(ctx);
                    var lhs = Pop(ctx);
                    string opStr = OpcodeToOperator(ins.Opcode);
                    var opNode = new AST(new Token(Token.TokenType.OPERATOR, opStr));
                    opNode.addChild(lhs);
                    opNode.addChild(rhs);
                    ctx.Stack.Add(opNode);
                }
                else if (IsApiOpcode(ins.Opcode))
                {
                    // Get the API method name from the opcode using name-based lookup
                    string apiInstructionName = GetApiNameFromOpcode(ins.Opcode);
                    
                    // Look up the actual method by name
                    ctx.ApiMethodsByName.TryGetValue(apiInstructionName, out MethodInfo? apiMethod);
                    
                    string rawName = apiMethod?.Name ?? apiInstructionName;
                    string apiName = NormalizeApiName(rawName);
                    int argCount = apiMethod?.GetParameters().Length ?? 0;
                    
                    var args = new List<AST>(argCount);
                    for (int a = 0; a < argCount; a++)
                    {
                        args.Add(ctx.Stack.Count > 0
                            ? Pop(ctx)
                            : new AST(new TokenWithValue(Token.TokenType.NUMBER, "0", 0f)));
                    }
                    args.Reverse();

                    var argsGroup = new AST(new Token(Token.TokenType.NODE_GROUP, ArgsTag));
                    foreach (var a in args) argsGroup.addChild(a);
                    
                    var callNode = new AST_FunctionCall(new Token(Token.TokenType.FUNCTION_CALL, apiName));
                    callNode.addChild(argsGroup);

                    bool isVoid = apiMethod?.ReturnType == typeof(void);

                    if (isVoid)
                    {
                        Log("PARSER", LogLevel.Debug, $"    Action: Adding void call '{callNode.getTokenString()}' to statements.");
                        outList.addChild(callNode);
                    }
                    else
                    {
                        string tempVarName = $"__t{_tempVarCounter++}";
                        var nameToken = new Token(Token.TokenType.NAME, tempVarName);

                        var declarationNode = new AST_VariableDeclaration(
                            new Token(Token.TokenType.VAR_DECLARATION, "<IMPLICIT_TEMP_VAR>"),
                            ReturnValueType.UNKNOWN_TYPE,
                            tempVarName
                        );
                        var assignmentNode = new AST_Assignment(
                            new Token(Token.TokenType.ASSIGNMENT, "="),
                            tempVarName
                        );
                        assignmentNode.addChild(callNode);

                        Log("PARSER", LogLevel.Debug, $"    Action: Adding non-void call '{callNode.getTokenString()}' as assignment to '{tempVarName}'.");
                        outList.addChild(declarationNode);
                        outList.addChild(assignmentNode);

                        var variableNode = new AST_Variable(nameToken);
                        Log("PARSER", LogLevel.Debug, $"    Action: Pushing result '{variableNode.getTokenString()}' onto stack.");
                        ctx.Stack.Add(variableNode);
                    }
                }
                else
                {
                    Log("PARSER", LogLevel.Warning, $"Unknown/unsupported opcode 0x{ins.Opcode:X2} at {i}. Ignored.");
                }

                Log("PARSER", LogLevel.Debug, $"    Stack After:  {DumpStack(ctx)}");
                i += 1;
            }
            FlushStackAsStatements(ctx, outList, "block-end");
            Log("PARSER", LogLevel.Debug, $"BuildBlock finished. Range: [{start}, {end}). Final statement count: {outList.getChildren().Count}");
        }

        private bool TryParseIf(BlockCtx ctx, int condIndex, int blockEndExclusive,
                                out int nextIndex, out AST? ifNode)
        {
            nextIndex = condIndex + 1;
            ifNode = null;
            var condInstr = ctx.DR.Instructions[condIndex];

            // The condition should be on the stack from the preceding instruction(s)
            AST condition = ctx.Stack.Count > 0
                ? Pop(ctx)
                : new AST(new TokenWithValue(Token.TokenType.NUMBER, "0", 0f));

            int? target = TargetOf(ctx, condIndex);
            if (target == null) return false;

            var ifAst = new AST_IfNode(new Token(Token.TokenType.IF, "if"));
            int thenStart = condIndex + 1;
            
            int jmpIndex = target.Value - 1;
            if (jmpIndex >= thenStart && jmpIndex < blockEndExclusive && ctx.DR.Instructions[jmpIndex].Opcode == HGLOpcodes.JMP)
            {
                int? endTarget = TargetOf(ctx, jmpIndex);
                if (endTarget != null && endTarget.Value > target.Value && endTarget.Value <= blockEndExclusive)
                {
                    var thenList = new AST(new Token(Token.TokenType.STATEMENT_LIST, ThenTag));
                    BuildBlock(ctx, thenStart, jmpIndex, thenList, "if-else then");

                    var elseList = new AST(new Token(Token.TokenType.STATEMENT_LIST, ElseTag));
                    BuildBlock(ctx, target.Value, endTarget.Value, elseList, "if-else else");

                    // JZ jumps if the condition is false/zero, so the 'then' block executes when the condition is true.
                    // This means the IF condition should be NOT(condition).
                    // JNZ jumps if the condition is true/non-zero, so the 'then' block executes when false.
                    // This means the IF condition should be the condition as-is.
                    // The original code seems to have this backwards for an if-else structure where the jump goes to the ELSE block.
                    // Let's assume JZ jumps over the THEN block to the ELSE.
                    // The THEN block runs if condition is NON-ZERO. So the 'if' condition is `condition`.
                    // To make `JZ` work, the compiler should have put a `NOT` before it.
                    // Sticking to standard compilation: JZ branches when false. The code following is the TRUE block.
                    // JZ target: `if (condition==0) goto target`. Block after JZ is the THEN block. Condition to run THEN is `condition != 0`. So for JZ, if condition is `!condition`
                    ifAst.addChild(condInstr.Opcode == HGLOpcodes.JZ ? WrapWithNot(condition) : condition);
                    ifAst.addChild(thenList);
                    ifAst.addChild(elseList);

                    ifNode = ifAst;
                    nextIndex = endTarget.Value;
                    return true;
                }
            }

            int thenEnd = target.Value;
            if (thenEnd < thenStart || thenEnd > blockEndExclusive) return false;

            var thenOnlyList = new AST(new Token(Token.TokenType.STATEMENT_LIST, ThenTag));
            BuildBlock(ctx, thenStart, thenEnd, thenOnlyList, "if-then");
            
            // For a simple IF-THEN, the jump skips the THEN block.
            // JZ target: `if (condition==0) goto target`. The THEN block is skipped. The condition to EXECUTE the THEN block is `condition != 0`. So for JZ, the AST condition is `condition`.
            // JNZ target: `if (condition!=0) goto target`. The THEN block is skipped. The condition to EXECUTE the THEN block is `condition == 0`. So for JNZ, the AST condition is `!condition`.
            ifAst.addChild(condInstr.Opcode == HGLOpcodes.JNZ ? WrapWithNot(condition) : condition);
            ifAst.addChild(thenOnlyList);

            ifNode = ifAst;
            nextIndex = thenEnd;
            return true;
        }
    }
}
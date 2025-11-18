// Hidra.Core/ASTBuilder.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Hidra.Core.Logging;
using ProgrammingLanguageNr1;

namespace Hidra.Core
{
    public sealed class ASTBuilder
    {
        #region Private Helper Classes

        private class AstBuildContext
        {
            public readonly HGLDecompiler.DecompileResult DR;
            public readonly Dictionary<string, MethodInfo> ApiMethodsByName;
            public readonly Dictionary<int, string> Labels = new();
            public int NextLabelId = 0;

            public AstBuildContext(HGLDecompiler.DecompileResult dr, Dictionary<string, MethodInfo> apiMethodsByName)
            {
                DR = dr;
                ApiMethodsByName = apiMethodsByName;

                foreach (var targetIndex in dr.JumpTargets.Keys.OrderBy(k => k))
                {
                    GetLabelForInstruction(targetIndex);
                }
            }

            public string GetLabelForInstruction(int instructionIndex)
            {
                if (!Labels.TryGetValue(instructionIndex, out var label))
                {
                    label = $"LBL_{NextLabelId++}";
                    Labels[instructionIndex] = label;
                }
                return label;
            }
        }

        #endregion

        private const string ArgsTag = "<ARGS>";
        private Action<string, LogLevel, string>? _logger;
        private int _tempVarCounter;

        private void Log(string area, LogLevel level, string message) => _logger?.Invoke(area, level, string.Concat("[ASTBuilder] ", message));

        public AST BuildAst(HGLDecompiler.DecompileResult dr,
                            Dictionary<string, MethodInfo> apiMethodsByName,
                            Action<string, LogLevel, string>? logger = null)
        {
            _logger = logger;
            _tempVarCounter = 0;
            var ctx = new AstBuildContext(dr, apiMethodsByName);
            var geneStatements = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<GENE>"));

            if (dr.Instructions.Count == 0) return geneStatements;
            
            BuildGeneBody(ctx, geneStatements);

            return geneStatements;
        }

        private void BuildGeneBody(AstBuildContext ctx, AST outList)
        {
            var virtualStack = new List<AST>();
            int instructionCount = ctx.DR.Instructions.Count;

            for (int i = 0; i < instructionCount; i++)
            {
                if (ctx.Labels.ContainsKey(i))
                {
                    outList.addChild(new AST(new Token(Token.TokenType.LABEL, ctx.GetLabelForInstruction(i))));
                }

                var instr = ctx.DR.Instructions[i];

                if (IsConditionalJump(instr.Opcode))
                {
                    int? targetIndex = GetJumpTargetIndex(ctx, i);
                    if (!targetIndex.HasValue) continue;

                    string targetLabel = ctx.GetLabelForInstruction(targetIndex.Value);
                    
                    AST condition = Pop(virtualStack);

                    // Translate HGL opcodes into generic interpreter logic.
                    // The interpreter only knows one operation: IF_GOTO (which jumps on true).
                    // For JZ (jump if false), we must invert the condition before giving it to the node.
                    if (instr.Opcode == HGLOpcodes.JZ)
                    {
                        var notNode = new AST(new Token(Token.TokenType.NOT, "!"));
                        notNode.addChild(condition);
                        condition = notNode;
                    }

                    var ifGotoToken = new Token(Token.TokenType.IF_GOTO, "IF_GOTO");
                    var ifGotoNode = new AST_IfGotoNode(ifGotoToken, targetLabel);
                    ifGotoNode.addChild(condition);

                    outList.addChild(ifGotoNode);
                }
                else if (instr.Opcode == HGLOpcodes.JMP)
                {
                    int? targetIndex = GetJumpTargetIndex(ctx, i);
                    if (!targetIndex.HasValue) continue;

                    string targetLabel = ctx.GetLabelForInstruction(targetIndex.Value);
                    outList.addChild(new AST(new Token(Token.TokenType.GOTO, targetLabel)));
                }
                else
                {
                    ProcessInstruction(ctx, instr, virtualStack, outList);
                }
            }
            
            foreach(var remainingExpression in virtualStack)
            {
                outList.addChild(remainingExpression);
            }
        }

        private void ProcessInstruction(AstBuildContext ctx, HGLDecompiler.Instruction instr, List<AST> stack, AST outList)
        {
             try
            {
                byte op = instr.Opcode;

                if (op == HGLOpcodes.PUSH_BYTE)
                {
                    float byteValue = (float)instr.Operand;
                    stack.Add(new AST(new TokenWithValue(Token.TokenType.NUMBER, byteValue.ToString(CultureInfo.InvariantCulture), byteValue)));
                }
                else if (op == HGLOpcodes.PUSH_FLOAT)
                {
                    float floatValue = BitConverter.ToSingle(BitConverter.GetBytes(instr.Operand), 0);
                    stack.Add(new AST(new TokenWithValue(Token.TokenType.NUMBER, floatValue.ToString(CultureInfo.InvariantCulture), floatValue)));
                }
                else if (op == HGLOpcodes.DUP)
                {
                    if (stack.Count > 0) stack.Add(stack.Last().Clone());
                    else stack.Add(new AST(new TokenWithValue(Token.TokenType.NUMBER, "0", 0f)));
                }
                else if (op == HGLOpcodes.POP)
                {
                    if (stack.Count > 0) outList.addChild(Pop(stack));
                }
                else if (op >= HGLOpcodes.ADD && op <= HGLOpcodes.LTE)
                {
                    var rhs = Pop(stack);
                    var lhs = Pop(stack);
                    var opNode = new AST(new Token(Token.TokenType.OPERATOR, OpcodeToOperator(instr.Opcode)));
                    opNode.addChild(lhs);
                    opNode.addChild(rhs);
                    stack.Add(opNode);
                }
                else if (IsApiOpcode(op))
                {
                    BuildApiCall(ctx, instr.Opcode, stack, outList);
                }
            }
            catch (Exception ex)
            {
                Log("AST_BUILD", LogLevel.Error, $"Error processing instruction at {instr.ByteOffset}: {ex.Message}. Inserting placeholder.");
                outList.addChild(new AST(new Token(Token.TokenType.NAME, "<ERROR_PLACEHOLDER>")));
            }
        }

        #region Shared Helpers

        private static bool IsApiOpcode(byte opcode) => opcode >= HGLOpcodes.ApiOpcodeStart && opcode < HGLOpcodes.OperatorOpcodeStart;
        
        private static bool IsConditionalJump(byte opcode) => 
            opcode == HGLOpcodes.JZ  || 
            opcode == HGLOpcodes.JNZ || 
            opcode == HGLOpcodes.JNE;

        private static string GetApiNameFromOpcode(byte opcode) => HGLOpcodes.MasterInstructionOrder.ElementAtOrDefault(opcode) ?? $"API_UNKNOWN_{opcode}";
        private static int? GetJumpTargetIndex(AstBuildContext ctx, int instrIndex) => (instrIndex >= 0 && instrIndex < ctx.DR.JumpSources.Length && ctx.DR.JumpSources[instrIndex] >= 0) ? ctx.DR.JumpSources[instrIndex] : null;
        
        private static AST Pop(List<AST> stack)
        {
            if (stack.Count == 0)
            {
                return new AST(new TokenWithValue(Token.TokenType.NUMBER, "0", 0f));
            }
            var node = stack.Last();
            stack.RemoveAt(stack.Count - 1);
            return node;
        }

        private void BuildApiCall(AstBuildContext ctx, byte opcode, List<AST> stack, AST outList)
        {
            string apiInstructionName = GetApiNameFromOpcode(opcode);
            ctx.ApiMethodsByName.TryGetValue(apiInstructionName, out MethodInfo? apiMethod);

            string rawName = apiMethod?.Name ?? apiInstructionName;
            string apiName = NormalizeApiName(rawName);
            int argCount = apiMethod?.GetParameters().Length ?? 0;

            var args = new List<AST>(argCount);
            for (int a = 0; a < argCount; a++)
                args.Add(Pop(stack));
            args.Reverse();

            var argsGroup = new AST(new Token(Token.TokenType.NODE_GROUP, ArgsTag));
            foreach (var a in args) argsGroup.addChild(a);

            var callNode = new AST_FunctionCall(new Token(Token.TokenType.FUNCTION_CALL, apiName));
            callNode.addChild(argsGroup);

            if (apiMethod == null || apiMethod.ReturnType == typeof(void))
            {
                outList.addChild(callNode);
            }
            else
            {
                ReturnValueType rvType = ReturnValueConversions.SystemTypeToReturnValueType(apiMethod.ReturnType);

                string tempVarName = $"__t{_tempVarCounter++}";
                var declarationNode = new AST_VariableDeclaration(new Token(Token.TokenType.VAR_DECLARATION, "<IMPLICIT>"), rvType, tempVarName);
                var assignmentNode = new AST_Assignment(new Token(Token.TokenType.ASSIGNMENT, "="), tempVarName);
                assignmentNode.addChild(callNode);

                outList.addChild(declarationNode);
                outList.addChild(assignmentNode);

                stack.Add(new AST_Variable(new Token(Token.TokenType.NAME, tempVarName)));
            }
        }

        private static string OpcodeToOperator(byte opcode) => HGLOpcodes.OperatorLookup.FirstOrDefault(x => x.Value == opcode).Key ?? "?";
        private static string NormalizeApiName(string rawName) => rawName.StartsWith("API_", StringComparison.Ordinal) ? rawName.Substring(4) : rawName;

        #endregion
    }
}
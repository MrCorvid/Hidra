// Hidra.Core/World/HidraWorld.Genes.cs
using Hidra.Core.Logging;
using ProgrammingLanguageNr1;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hidra.Core
{
    /// <summary>
    /// This partial class of HidraWorld contains all functionality related to the
    /// execution of the HGL (Hidra Genesis Language) scripting engine. It manages
    /// the interpretation of compiled genes and provides the bridge between the
    /// script environment and the core C# simulation state.
    /// </summary>
    public partial class HidraWorld
    {
        /// <summary>
        /// Cached function definitions for the HidraSprakBridge to avoid expensive
        /// reflection on every single gene execution.
        /// </summary>
        private static readonly IReadOnlyList<FunctionDefinition> _cachedBridgeFunctions =
            FunctionDefinitionCreator.CreateDefinitions(new HidraSprakBridge(null!, null, ExecutionContext.System), typeof(HidraSprakBridge));

        /// <summary>
        /// Executes a compiled gene from the HGL genome, providing it with a controlled
        /// environment to interact with the world.
        /// </summary>
        /// <param name="geneId">The unique ID of the gene to execute.</param>
        /// <param name="self">The neuron on which the gene is being executed. This can be null for
        /// system-level genes like Genesis that operate on the world globally.</param>
        /// <param name="context">The security context (System, Protected, General) for the execution, which
        /// determines which API functions are available to the script.</param>
        public void ExecuteGene(uint geneId, Neuron? self, ExecutionContext context)
        {
            if (!_compiledGenes.TryGetValue(geneId, out var geneAst))
            {
                return;
            }

            Log("SIM_CORE", LogLevel.Debug, $"Executing Gene {geneId} on Neuron {self?.Id.ToString() ?? "System"} (Context: {context})");

            // --- Determine Execution Fuel ---
            int maxInstructions;
            if (self != null)
            {
                maxInstructions = (int)self.LocalVariables[(int)LVarIndex.GeneExecutionFuel];
            }
            else
            {
                maxInstructions = geneId switch
                {
                    SYS_GENE_GENESIS => 5000,
                    SYS_GENE_GESTATION or SYS_GENE_MITOSIS or SYS_GENE_APOPTOSIS => 3000,
                    _ => 1000
                };
            }

            if (maxInstructions <= 0)
            {
                Log("SIM_CORE", LogLevel.Debug, $"Neuron {self?.Id} has no fuel to execute Gene {geneId}. Skipping.");
                return;
            }

            // --- Interpreter Setup ---
            var programRoot = new AST(new Token(Token.TokenType.PROGRAM_ROOT, "<PROGRAM_ROOT>"));
            var mainStatementList = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<STATEMENT_LIST>"));
            var funcList = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<FUNCTION_LIST>"));

            programRoot.addChild(mainStatementList);
            programRoot.addChild(funcList);
            mainStatementList.addChild(new AST(new Token(Token.TokenType.STATEMENT_LIST, "<GLOBAL_VARIABLE_DEFINITIONS_LIST>")));
            mainStatementList.addChild(geneAst);
            
            Action<string>? traceAction = null;
            if (Logger.IsLogLevelEnabled("HGL_TRACE", LogLevel.Trace))
            {
                traceAction = (message) => Log("HGL_TRACE", LogLevel.Trace, $"[Gene:{geneId}] [Neuron:{self?.Id ?? 0}] {message}");
            }

            // IMPORTANT: Bind function definitions to the *actual* per-execution bridge
            var bridge = new HidraSprakBridge(this, self, context);
            var boundFunctions = FunctionDefinitionCreator.CreateDefinitions(bridge, typeof(HidraSprakBridge));
            var runner = new SprakRunner(programRoot, boundFunctions);
            var interpreter = runner.GetInterpreter();

            if (interpreter == null)
            {
                Log("SIM_CORE", LogLevel.Error, $"Failed to get an interpreter for Gene {geneId}.");

                // Dump the ASTs to help diagnose scope/structure issues
                try
                {
                    string geneDump = geneAst != null ? geneAst.getTreeAsString() : "<null geneAst>";
                    Log("SIM_CORE", LogLevel.Error, $"AST for Gene {geneId}:\n{geneDump}");
                }
                catch (Exception ex)
                {
                    Log("SIM_CORE", LogLevel.Error, $"Could not dump Gene AST for {geneId}: {ex}");
                }

                try
                {
                    string programDump = programRoot.getTreeAsString();
                    Log("SIM_CORE", LogLevel.Error, $"Program AST passed to SprakRunner:\n{programDump}");
                }
                catch (Exception ex)
                {
                    Log("SIM_CORE", LogLevel.Error, $"Could not dump Program AST: {ex}");
                }

                return;
            }

            interpreter.OnTrace = traceAction;

            // This is the line where the warning occurred.
            // We add the null-forgiving operator '!' because we have already
            // confirmed that 'interpreter' is not null in the block above.
            bridge.SetInterpreter(interpreter!);

            // --- Execution with Fuel Constraint ---
            int instructionCount = 0;
            try
            {
                foreach (var status in interpreter)
                {
                    if (status == InterpreterTwo.Status.ERROR)
                    {
                        Log("SIM_CORE", LogLevel.Warning, $"An error occurred during HGL execution of Gene {geneId} on Neuron {self?.Id.ToString() ?? "System"}.");
                        break;
                    }

                    if (++instructionCount > maxInstructions)
                    {
                        Log("SIM_CORE", LogLevel.Warning, $"Gene {geneId} on Neuron {self?.Id.ToString() ?? "System"} exceeded instruction limit of {maxInstructions} and was terminated.");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("SIM_CORE", LogLevel.Error, $"A C# exception was thrown from the HGL bridge during execution of Gene {geneId}. This indicates a bug in a bridge function. Error: {ex.Message}");
            }
            finally
            {
                if (self != null)
                {
                    self.LocalVariables[(int)LVarIndex.GeneExecutionFuel] = Math.Max(0, self.LocalVariables[(int)LVarIndex.GeneExecutionFuel] - instructionCount);
                }
            }
        }

        /// <summary>
        /// Determines the initial security context for a gene based on its predefined ID.
        /// </summary>
        /// <param name="geneId">The ID of the gene.</param>
        /// <returns>The appropriate <see cref="ExecutionContext"/> for the gene.</returns>
        private ExecutionContext GetInitialContextForGene(uint geneId) => geneId switch
        {
            SYS_GENE_GENESIS => ExecutionContext.System,
            SYS_GENE_GESTATION or SYS_GENE_MITOSIS or SYS_GENE_APOPTOSIS => ExecutionContext.Protected,
            _ => ExecutionContext.General,
        };
    }
}
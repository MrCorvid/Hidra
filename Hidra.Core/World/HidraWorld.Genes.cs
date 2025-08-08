// Hidra.Core/World/HidraWorld.Genes.cs
using Hidra.Core.Logging;
using ProgrammingLanguageNr1;

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
        // Note: The fields `_compiledGenes`, `_nextEventId`, and various `SYS_GENE_*` constants
        // are defined in other partial class files of HidraWorld.

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
            // Intent: A request to execute a non-existent gene is not an error. It's a valid
            // scenario where a gene may have been removed or was never defined. We exit gracefully.
            if (!_compiledGenes.TryGetValue(geneId, out var geneAst))
            {
                return;
            }

            Logger.Log("SIM_CORE", LogLevel.Info, $"Executing Gene {geneId} on Neuron {self?.Id.ToString() ?? "System"} (Context: {context})");

            // --- Interpreter Setup ---
            // Intent: The SprakRunner interpreter expects a complete program structure, not just a
            // raw list of statements. We must wrap the specific gene's Abstract Syntax Tree (AST)
            // within this standard program root structure on the fly.
            var programRoot = new AST(new Token(Token.TokenType.PROGRAM_ROOT, "<PROGRAM_ROOT>"));
            var mainStatementList = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<STATEMENT_LIST>"));
            var funcList = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<FUNCTION_LIST>"));
            
            programRoot.addChild(mainStatementList);
            programRoot.addChild(funcList);
            mainStatementList.addChild(new AST(new Token(Token.TokenType.STATEMENT_LIST, "<GLOBAL_VARIABLE_DEFINITIONS_LIST>")));
            mainStatementList.addChild(geneAst); // The actual gene code is inserted here.
            
            // Intent: The HidraSprakBridge is the critical component that exposes C# world functions
            // (e.g., 'AddNeuron', 'GetHealth') to the HGL script. It acts as a secure API layer,
            // respecting the given ExecutionContext to limit access to sensitive functions.
            var bridge = new HidraSprakBridge(this, self, context);
            var bridgeFunctions = FunctionDefinitionCreator.CreateDefinitions(bridge, typeof(HidraSprakBridge));

            var runner = new SprakRunner(programRoot, bridgeFunctions);
            var interpreter = runner.GetInterpreter();

            if (interpreter == null)
            {
                Logger.Log("SIM_CORE", LogLevel.Error, $"Failed to get an interpreter for Gene {geneId}.");
                return;
            }
            
            // Intent: The bridge needs a reference back to the interpreter's runtime. This allows
            // API functions within the bridge to perform advanced operations, like accessing the
            // script's call stack to retrieve function arguments.
            bridge.SetInterpreter(interpreter);

            // --- Execution ---
            // Intent: The interpreter is executed as a state machine. The foreach loop drives its
            // execution step-by-step until it completes or reports an error. This allows us to
            // monitor the execution status and log any issues that arise within the script.
            foreach (var status in interpreter)
            {
                if (status == InterpreterTwo.Status.ERROR)
                {
                    Logger.Log("SIM_CORE", LogLevel.Warning, $"An error occurred during HGL execution of Gene {geneId} on Neuron {self?.Id.ToString() ?? "System"}.");
                    break;
                }
            }
        }

        /// <summary>
        /// Determines the initial security context for a gene based on its predefined ID.
        /// </summary>
        /// <param name="geneId">The ID of the gene.</param>
        /// <returns>The appropriate <see cref="ExecutionContext"/> for the gene.</returns>
        /// <remarks>
        /// Intent: To enforce a security model where core system genes (like Genesis) have the highest
        /// privileges, protected biological process genes have elevated privileges, and all other
        /// user-defined genes operate in a restricted, general-purpose context. This prevents
        /// general genes from performing highly sensitive or world-altering actions.
        /// </remarks>
        private ExecutionContext GetInitialContextForGene(uint geneId) => geneId switch
        {
            SYS_GENE_GENESIS => ExecutionContext.System,
            SYS_GENE_GESTATION or SYS_GENE_MITOSIS or SYS_GENE_APOPTOSIS => ExecutionContext.Protected,
            _ => ExecutionContext.General,
        };
    }
}
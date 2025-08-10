// Hidra.Core/World/HidraWorld.Genes.cs
namespace Hidra.Core;

using Hidra.Core.Logging;
using ProgrammingLanguageNr1;

/// <summary>
/// This partial class of HidraWorld contains all functionality related to the
/// execution of the HGL (Hidra Genesis Language) scripting engine. It manages
/// the interpretation of compiled genes and provides the bridge between the
/// script environment and the core C# simulation state.
/// </summary>
public partial class HidraWorld
{
    /// <summary>
    /// Executes a compiled gene from the HGL genome, providing it with a controlled
    /// environment to interact with the world.
    /// </summary>
    /// <param name="geneId">The unique ID of the gene to execute.</param>
    /// <param name="self">The neuron on which the gene is being executed. This can be null for
    /// system-level genes like Genesis that operate on the world globally.</param>
    /// <param name="context">The security context (System, Protected, General) for the execution, which
    /// determines which API functions are available to the script.</param>
    /// <remarks>
    /// <para>
    /// This method orchestrates the execution of an HGL gene. It performs the following steps:
    /// <list type="number">
    /// <item><description>Wraps the gene's AST in a full program structure required by the interpreter.</description></item>
    /// <item><description>Initializes a <c>HidraSprakBridge</c> to expose world functions to the script, respecting the provided <paramref name="context"/> for security.</description></item>
    /// <item><description>Runs the interpreter's state machine to execute the gene's logic.</description></item>
    /// </list>
    /// A request to execute a non-existent gene is treated as a no-op and does not throw an error.
    /// </para>
    /// </remarks>
    public void ExecuteGene(uint geneId, Neuron? self, ExecutionContext context)
    {
        if (!_compiledGenes.TryGetValue(geneId, out var geneAst))
        {
            return;
        }

        Logger.Log("SIM_CORE", LogLevel.Info, $"Executing Gene {geneId} on Neuron {self?.Id.ToString() ?? "System"} (Context: {context})");

        var programRoot = new AST(new Token(Token.TokenType.PROGRAM_ROOT, "<PROGRAM_ROOT>"));
        var mainStatementList = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<STATEMENT_LIST>"));
        var funcList = new AST(new Token(Token.TokenType.STATEMENT_LIST, "<FUNCTION_LIST>"));
        
        programRoot.addChild(mainStatementList);
        programRoot.addChild(funcList);
        mainStatementList.addChild(new AST(new Token(Token.TokenType.STATEMENT_LIST, "<GLOBAL_VARIABLE_DEFINITIONS_LIST>")));
        mainStatementList.addChild(geneAst);
        
        var bridge = new HidraSprakBridge(this, self, context);
        var bridgeFunctions = FunctionDefinitionCreator.CreateDefinitions(bridge, typeof(HidraSprakBridge));

        var runner = new SprakRunner(programRoot, bridgeFunctions);
        var interpreter = runner.GetInterpreter();

        if (interpreter == null)
        {
            Logger.Log("SIM_CORE", LogLevel.Error, $"Failed to get an interpreter for Gene {geneId}.");
            return;
        }
        
        bridge.SetInterpreter(interpreter);

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
    /// This enforces a security model where core system genes (like Genesis) have the highest
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
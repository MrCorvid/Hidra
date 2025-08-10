// Hidra.Core/Genome/Bridge/HidraSprakBridge.cs
namespace Hidra.Core;

using System.Linq;
using System.Numerics;
using Hidra.Core.Brain;
using ProgrammingLanguageNr1;

/// <summary>
/// Acts as the bridge between the Sprak virtual machine and the Hidra simulation world.
/// This class provides the C# implementation for all API functions callable from HGL scripts.
/// It is instantiated for each gene execution.
/// </summary>
/// <remarks>
/// This class is structured as a partial class, with API functions grouped into separate files
/// for better organization. This file contains the core state and common helper methods used by all
/// other parts of the bridge.
/// </remarks>
public partial class HidraSprakBridge
{
    private readonly HidraWorld _world;
    private readonly Neuron? _self;
    private readonly ExecutionContext _context;
    private Neuron? _systemTargetNeuron;
    private InterpreterTwo? _interpreter;

    /// <summary>
    /// Initializes a new instance of the <see cref="HidraSprakBridge"/> class for a single gene execution.
    /// </summary>
    /// <param name="world">A reference to the main HidraWorld instance.</param>
    /// <param name="self">The neuron executing the gene. Can be null for system-level genes.</param>
    /// <param name="context">The security context of the execution (System, Protected, or General).</param>
    public HidraSprakBridge(HidraWorld world, Neuron? self, ExecutionContext context)
    {
        _world = world;
        _self = self;
        _context = context;

        // In a System context, the initial target is 'self', but can be changed via API_SetSystemTarget.
        _systemTargetNeuron = self;
    }
    
    /// <summary>
    /// Provides the bridge with a reference to the active interpreter instance.
    /// This is called by the gene executor after the interpreter is created.
    /// </summary>
    /// <param name="interpreter">The active Sprak interpreter.</param>
    public void SetInterpreter(InterpreterTwo interpreter) => _interpreter = interpreter;

    /// <summary>
    /// Determines which neuron is the target of an API call based on the current execution context.
    /// </summary>
    /// <returns>
    /// In a <see cref="ExecutionContext.System"/> context, returns the neuron set by <c>API_SetSystemTarget</c>.
    /// In other contexts, returns the neuron that is executing the gene ('self').
    /// </returns>
    private Neuron? GetTargetNeuron() => _context == ExecutionContext.System ? _systemTargetNeuron : _self;

    /// <summary>
    /// Finds the nearest neighboring neuron to 'self' within the world's configured competition radius.
    /// </summary>
    /// <returns>The nearest <see cref="Neuron"/>, or null if 'self' does not exist or no neighbors are found.</returns>
    private Neuron? FindNearestNeighbor()
    {
        if (_self == null)
        {
            return null;
        }

        return _world.GetNeighbors(_self, _world.Config.CompetitionRadius)
                     .MinBy(neighbor => Vector3.DistanceSquared(_self.Position, neighbor.Position));
    }
}
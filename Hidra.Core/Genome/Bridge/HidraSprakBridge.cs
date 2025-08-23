// Hidra.Core/Genome/Bridge/HidraSprakBridge.cs
using Hidra.Core.Brain;
using Hidra.Core.Logging;
using ProgrammingLanguageNr1;
using System.Linq;
using System.Numerics;

namespace Hidra.Core
{
    /// <summary>
    /// Acts as the bridge between the Sprak virtual machine and the Hidra simulation world.
    /// This class provides the C# implementation for all API functions callable from HGL scripts.
    /// It is instantiated for each gene execution.
    /// </summary>
    /// <remarks>
    /// This class is structured as a partial class, with API functions grouped into separate files
    /// (CoreAPI, SensoryAPI, etc.) for better organization. This file contains the core state and
    /// common helper methods used by all other parts of the bridge.
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
        public HidraSprakBridge(HidraWorld world, Neuron? self, ExecutionContext context)
        {
            _world = world;
            _self = self;
            _context = context;

            // In a System context, the initial target is 'self', but can be changed via API_SetSystemTarget.
            _systemTargetNeuron = self;
        }
        
        /// <summary>Provides the bridge with a reference to the active interpreter instance.</summary>
        public void SetInterpreter(InterpreterTwo interpreter)
        {
            _interpreter = interpreter;
        }

        // ------------------------
        // Logging helpers (shared)
        // ------------------------

        private void LogDbg(string area, string msg)
            => _world.Log(area, LogLevel.Debug, $"[ctx={_context}] [self={_self?.Id.ToString() ?? "0"}] {msg}");

        private void LogWarn(string area, string msg)
            => _world.Log(area, LogLevel.Warning, $"[ctx={_context}] [self={_self?.Id.ToString() ?? "0"}] {msg}");

        private void LogErr(string area, string msg)
            => _world.Log(area, LogLevel.Error, $"[ctx={_context}] [self={_self?.Id.ToString() ?? "0"}] {msg}");

        /// <summary>
        /// Determines which neuron is the target of an API call based on the current execution context.
        /// </summary>
        private Neuron? GetTargetNeuron()
        {
            switch (_context)
            {
                case ExecutionContext.System:
                    return _systemTargetNeuron;
                
                case ExecutionContext.Protected:
                case ExecutionContext.General:
                default:
                    return _self;
            }
        }

        /// <summary>
        /// Finds the nearest neighboring neuron to 'self' within the world's configured competition radius.
        /// </summary>
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
}
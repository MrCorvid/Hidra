// Hidra.Core/World/HidraWorld.cs
using Hidra.Core.Brain;
using Hidra.Core.Logging;
using ProgrammingLanguageNr1;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Hidra.Core
{
    /// <summary>
    /// Helper class for high-precision, order-independent floating-point accumulation.
    /// </summary>
    public class KahanAccumulator
    {
        private float _sum;
        private float _c; // A running compensation for lost low-order bits.
        public float Sum => _sum;

        public void Add(float value)
        {
            float y = value - _c;
            float t = _sum + y;
            _c = (t - _sum) - y;
            _sum = t;
        }
    }

    /// <summary>
    /// The main container and simulation engine for the entire Hidra ecosystem.
    /// It manages all neurons, synapses, I/O nodes, and the progression of time (ticks).
    /// This class is structured as a partial class, with functionality split across multiple files.
    /// </summary>
    public partial class HidraWorld
    {
        #region Core State Properties

        /// <summary>
        /// The current simulation time-step, or "tick". This value increments with each call to Step().
        /// </summary>
        public ulong CurrentTick { get; private set; }

        /// <summary>
        /// The configuration object containing all simulation parameters.
        /// </summary>
        public HidraConfig Config { get; private set; } = default!;

        #endregion

        #region Serialized Collections

        [JsonProperty]
        private readonly SortedDictionary<ulong, Neuron> _neurons = new();
        [JsonProperty]
        private readonly List<Synapse> _synapses = new();
        [JsonProperty]
        private readonly SortedDictionary<ulong, InputNode> _inputNodes = new();
        [JsonProperty]
        private readonly SortedDictionary<ulong, OutputNode> _outputNodes = new();
        [JsonProperty]
        private readonly EventQueue _eventQueue = new();

        #endregion

        #region Public Read-Only Accessors

        [JsonIgnore]
        public IReadOnlyDictionary<ulong, Neuron> Neurons => _neurons;
        [JsonIgnore]
        public IReadOnlyList<Synapse> Synapses => _synapses;
        [JsonIgnore]
        public IReadOnlyDictionary<ulong, InputNode> InputNodes => _inputNodes;
        [JsonIgnore]
        public IReadOnlyDictionary<ulong, OutputNode> OutputNodes => _outputNodes;

        /// <summary>
        /// A collection of global floating-point values accessible by all neurons, simulating hormonal effects.
        /// </summary>
        public float[] GlobalHormones { get; private set; } = default!;

        #endregion
        
        #region Runtime-Only State

        [JsonIgnore]
        private SpatialHash _spatialHash = default!;
        // The HGLParser is a stateless service, instantiated on-demand in methods that need it.
        [JsonIgnore]
        private Dictionary<uint, AST> _compiledGenes = default!;

        #endregion
        
        #region ID Generation & System Constants
        
        // These critical state variables are serialized to save and load the simulation state.
        [JsonProperty] private long _nextNeuronId = 1;
        [JsonProperty] private long _nextSynapseId = 1;
        [JsonProperty] private long _nextEventId = 1;

        private const uint SYS_GENE_GENESIS = 0;
        private const uint SYS_GENE_GESTATION = 1;
        private const uint SYS_GENE_MITOSIS = 2;
        private const uint SYS_GENE_APOPTOSIS = 3;

        #endregion

        #region Concurrency Control

        /// <summary>
        /// A lock object to synchronize access to world collections from the public API,
        /// ensuring that operations like AddNeuron are thread-safe.
        /// </summary>
        [JsonIgnore]
        private readonly object _worldApiLock = new();

        #endregion

        /// <summary>
        /// JSON constructor used by Newtonsoft.Json during deserialization. This constructor
        /// explicitly accepts all serialized state, ensuring that readonly fields and properties
        /// with private setters are correctly initialized.
        /// </summary>
        [JsonConstructor]
        private HidraWorld(
            HidraConfig config, ulong currentTick, float[] globalHormones,
            SortedDictionary<ulong, Neuron> neurons, List<Synapse> synapses,
            SortedDictionary<ulong, InputNode> inputNodes, SortedDictionary<ulong, OutputNode> outputNodes,
            EventQueue eventQueue, long nextNeuronId, long nextSynapseId, long nextEventId)
        {
            // Assign all serialized state from constructor parameters
            Config = config;
            CurrentTick = currentTick;
            GlobalHormones = globalHormones;
            _neurons = neurons;
            _synapses = synapses;
            _inputNodes = inputNodes;
            _outputNodes = outputNodes;
            _eventQueue = eventQueue;
            _nextNeuronId = nextNeuronId;
            _nextSynapseId = nextSynapseId;
            _nextEventId = nextEventId;

            // Initialize non-serialized runtime state
            _worldApiLock = new();
        }

        /// <summary>
        /// Defines the indices for well-known values within a Neuron's `LocalVariables` array.
        /// Using an enum makes the code more readable and less prone to "magic number" errors.
        /// </summary>
        private enum LVarIndex
        {
            FiringThreshold = 0, DecayRate = 1,
            RefractoryPeriod = 2, ThresholdAdaptationFactor = 3, ThresholdRecoveryRate = 4,
            RefractoryTimeLeft = 239, FiringRate = 240, DendriticPotential = 241,
            SomaPotential = 242, Health = 243, Age = 244, AdaptiveThreshold = 245
        }
        
        /// <summary>
        /// Creates a new, fully initialized HidraWorld from a configuration and a genome script.
        /// </summary>
        /// <param name="config">The simulation configuration settings.</param>
        /// <param name="hglGenome">The HGL (Hidra Genesis Language) script defining the initial genes.</param>
        /// <exception cref="InvalidOperationException">Thrown if the provided genome is missing the required Genesis gene (ID 0).</exception>
        public HidraWorld(HidraConfig config, string hglGenome)
        {
            Logger.Log("SIM_CORE", LogLevel.Info, "Creating new HidraWorld.");

            Config = config;
            GlobalHormones = new float[256];
            _spatialHash = new SpatialHash(Config.CompetitionRadius * 2);
            
            var parser = new HGLParser();
            _compiledGenes = parser.ParseGenome(hglGenome);
            if (!_compiledGenes.ContainsKey(SYS_GENE_GENESIS))
            {
                throw new InvalidOperationException("Genome is invalid: Missing System Genesis gene (Gene 0).");
            }
            
            // The compiler will link these calls to the implementations in other partial class files.
            ExecuteGene(SYS_GENE_GENESIS, null, ExecutionContext.System);
            
            if (Neurons.Count == 0) { AddNeuron(Vector3.Zero); }

            _synapses.Sort((a, b) => a.Id.CompareTo(b.Id));
            RebuildSpatialHash();
            
            Logger.Log("SIM_CORE", LogLevel.Info, $"New HidraWorld created with {Neurons.Count} initial neurons.");
        }

        /// <summary>
        /// Provides a central shutdown point for all static managers used by the engine.
        /// This is intended for use in unit test cleanup to ensure a clean state between tests.
        /// </summary>
        public static void Shutdown()
        {
            // This acts as a hub for shutting down all static managers.
            Logger.Shutdown();
        }

        /// <summary>
        /// Clears and rebuilds the spatial hash structure with all currently active neurons.
        /// </summary>
        private void RebuildSpatialHash()
        {
            _spatialHash.Clear();
            foreach (var neuron in _neurons.Values)
            {
                if(neuron.IsActive)
                {
                    _spatialHash.Insert(neuron);
                }
            }
        }
    }
}
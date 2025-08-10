// Hidra.Core/World/HidraWorld.cs
namespace Hidra.Core;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Hidra.Core.Brain;
using Hidra.Core.Logging;
using Newtonsoft.Json;
using ProgrammingLanguageNr1;

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
    /// <summary>
    /// The current simulation time-step, or "tick". This value increments with each call to Step().
    /// </summary>
    public ulong CurrentTick { get; private set; }

    /// <summary>
    /// The configuration object containing all simulation parameters.
    /// </summary>
    public HidraConfig Config { get; private set; } = default!;

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
    [JsonProperty]
    private readonly Dictionary<ulong, KahanAccumulator> _scratchDendritic = new(256);

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

    [JsonIgnore]
    private SpatialHash _spatialHash = default!;
    
    // The HGLParser is a stateless service, instantiated on-demand in methods that need it.
    [JsonIgnore]
    private Dictionary<uint, AST> _compiledGenes = default!;

    [JsonIgnore]
    private readonly Dictionary<ulong, List<Event>> _eventHistory = new();
    
    [JsonIgnore]
    private readonly List<Neuron> _neuronsToDeactivate = new();
    [JsonIgnore]
    private readonly List<Synapse> _synapsesToRemove = new();

    // These critical state variables are serialized to save and load the simulation state.
    [JsonProperty] private long _nextNeuronId = 1;
    [JsonProperty] private long _nextSynapseId = 1;
    [JsonProperty] private long _nextEventId = 1;

    private const uint SYS_GENE_GENESIS = 0;
    private const uint SYS_GENE_GESTATION = 1;
    private const uint SYS_GENE_MITOSIS = 2;
    private const uint SYS_GENE_APOPTOSIS = 3;

    /// <summary>
    /// A lock object to synchronize access to world collections from the public API,
    /// ensuring that operations like AddNeuron are thread-safe.
    /// </summary>
    [JsonIgnore]
    private readonly object _worldApiLock = new();
    
    private readonly object _eventHistoryLock = new();

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

        _worldApiLock = new();
    }

    /// <summary>
    /// Defines the indices for well-known values within a Neuron's `LocalVariables` array.
    /// </summary>
    /// <remarks>
    /// Using an enum makes the code more readable and less prone to "magic number" errors.
    /// </remarks>
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
    /// <param name="hglGenome">The HGL script defining the initial genes.</param>
    public HidraWorld(HidraConfig config, string hglGenome)
        : this(config, hglGenome, Enumerable.Empty<ulong>(), Enumerable.Empty<ulong>())
    {
    }

    /// <summary>
    /// Creates a new, fully initialized HidraWorld from a configuration, a genome script,
    /// and declarative lists of I/O nodes.
    /// </summary>
    /// <param name="config">The simulation configuration settings.</param>
    /// <param name="hglGenome">The HGL script defining the initial genes.</param>
    /// <param name="inputNodeIds">A collection of unique IDs for the InputNodes to create.</param>
    /// <param name="outputNodeIds">A collection of unique IDs for the OutputNodes to create.</param>
    /// <exception cref="InvalidOperationException">Thrown if the provided genome is missing the required Genesis gene (ID 0).</exception>
    public HidraWorld(HidraConfig config, string hglGenome, IEnumerable<ulong> inputNodeIds, IEnumerable<ulong> outputNodeIds)
    {
        Logger.Log("SIM_CORE", LogLevel.Info, "Creating new HidraWorld.");

        Config = config;
        GlobalHormones = new float[256];
        _spatialHash = new SpatialHash(Config.CompetitionRadius * 2);

        foreach (var id in inputNodeIds)
        {
            AddInputNode(id);
        }
        foreach (var id in outputNodeIds)
        {
            AddOutputNode(id);
        }
        Logger.Log("SIM_CORE", LogLevel.Info, $"Created {InputNodes.Count} input nodes and {OutputNodes.Count} output nodes.");
        
        var parser = new HGLParser();
        _compiledGenes = parser.ParseGenome(hglGenome, Config.SystemGeneCount);
        if (!_compiledGenes.ContainsKey(SYS_GENE_GENESIS))
        {
            throw new InvalidOperationException("Genome is invalid: Missing System Genesis gene (Gene 0).");
        }
        
        ExecuteGene(SYS_GENE_GENESIS, null, ExecutionContext.System);
        
        if (Neurons.Count == 0) { AddNeuron(Vector3.Zero); }

        _synapses.Sort((a, b) => a.Id.CompareTo(b.Id));
        RebuildSpatialHash();
        
        Logger.Log("SIM_CORE", LogLevel.Info, $"New HidraWorld created with {Neurons.Count} initial neurons.");
    }

    /// <summary>
    /// Provides a central shutdown point for all static managers used by the engine.
    /// </summary>
    /// <remarks>
    /// This is intended for use in unit test cleanup to ensure a clean state between tests.
    /// </remarks>
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
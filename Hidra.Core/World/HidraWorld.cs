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
using System.Runtime.CompilerServices;

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
    /// Defines the contract for a deterministic pseudo-random number generator.
    /// </summary>
    public interface IPrng
    {
        ulong NextULong();
        int NextInt(int minInclusive, int maxExclusive);
        float NextFloat();     // [0,1)
        double NextDouble();   // [0,1)
        void GetState(out ulong s0, out ulong s1);
        void SetState(ulong s0, ulong s1);
    }

    /// <summary>
    /// A deterministic, high-performance pseudo-random number generator based on the xorshift128+ algorithm.
    /// Not for cryptographic use.
    /// </summary>
    public sealed class XorShift128PlusPrng : IPrng
    {
        private ulong _s0, _s1;
        public XorShift128PlusPrng(ulong seed0, ulong seed1)
        {
            _s0 = seed0 != 0 ? seed0 : 0x9E3779B97F4A7C15UL;
            _s1 = seed1 != 0 ? seed1 : 0xBF58476D1CE4E5B9UL;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong NextRaw()
        {
            ulong s1 = _s0;
            ulong s0 = _s1;
            _s0 = s0;
            s1 ^= s1 << 23;
            _s1 = s1 ^ s0 ^ (s1 >> 17) ^ (s0 >> 26);
            return _s1 + s0;
        }
        public ulong NextULong() => NextRaw();
        public int NextInt(int min, int max) => (int)(NextDouble() * (max - min)) + min;
        public float NextFloat() => (float)(NextRaw() >> 40) / (1u << 24);
        public double NextDouble() => (NextRaw() >> 11) * (1.0 / (1UL << 53));
        public void GetState(out ulong s0, out ulong s1) { s0 = _s0; s1 = _s1; }
        public void SetState(ulong s0, ulong s1) { _s0 = s0; _s1 = s1; }
    }

    /// <summary>
    /// The main container and simulation engine for the entire Hidra ecosystem.
    /// It manages all neurons, synapses, I/O nodes, and the progression of time (ticks).
    /// This class is structured as a partial class, with functionality split across multiple files.
    /// </summary>
    public partial class HidraWorld
    {
        #region Core State Properties
        public ulong CurrentTick { get; private set; }
        public HidraConfig Config { get; private set; } = default!;
        [JsonProperty]
        public string ExperimentId { get; private set; } = "default";
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
        [JsonProperty]
        private readonly Dictionary<ulong, KahanAccumulator> _scratchDendritic = new(256);
        [JsonProperty]
        private ulong _rngS0;
        [JsonProperty]
        private ulong _rngS1;
        [JsonProperty]
        private ulong _metricsRngS0;
        [JsonProperty]
        private ulong _metricsRngS1;
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
        [JsonIgnore]
        private IPrng _rng = default!;
        [JsonIgnore]
        private IPrng _metricsRng = default!;
        [JsonIgnore] 
        internal IPrng InternalRng => _rng;
        public float[] GlobalHormones { get; private set; } = default!;
        #endregion
        
        #region Runtime-Only State
        [JsonIgnore]
        private Action<string, LogLevel, string>? _logAction;
        [JsonIgnore]
        private SpatialHash _spatialHash = default!;
        [JsonIgnore]
        private Dictionary<uint, AST> _compiledGenes = default!;
        [JsonIgnore]
        private WorldSnapshot[] _metricsRing = default!;
        [JsonIgnore]
        private int _metricsHead = 0;
        [JsonIgnore]
        private bool _metricsWrapped = false;
        [JsonIgnore]
        private readonly Dictionary<ulong, List<Event>> _eventHistory = new();
        private readonly object _eventHistoryLock = new();
        [JsonIgnore]
        private readonly List<Neuron> _neuronsToDeactivate = new();
        [JsonIgnore]
        private readonly List<Synapse> _synapsesToRemove = new();
        #endregion
        
        #region ID Generation & System Constants
        
        [JsonProperty] private long _nextNeuronId = 0;
        [JsonProperty] private long _nextSynapseId = 0;
        [JsonProperty] private long _nextEventId = 0;

        private const uint SYS_GENE_GENESIS = 0;
        private const uint SYS_GENE_GESTATION = 1;
        private const uint SYS_GENE_MITOSIS = 2;
        private const uint SYS_GENE_APOPTOSIS = 3;
        #endregion

        #region Concurrency Control
        [JsonIgnore]
        private readonly object _worldApiLock = new();

        /// <summary>
        /// Provides access to the central synchronization root for the world.
        /// This ensures that the Sprak Bridge and external API calls share the 
        /// same thread-safety context as the internal simulation loop.
        /// </summary>
        internal object SyncRoot => _worldApiLock;
        #endregion

        /// <summary>
        /// JSON constructor used by Newtonsoft.Json during deserialization. This constructor
        /// explicitly accepts all serialized state, ensuring that readonly fields and properties
        /// with private setters are correctly initialized.
        /// </summary>
        [JsonConstructor]
        private HidraWorld(
            HidraConfig config, ulong currentTick, float[] globalHormones, string experimentId,
            SortedDictionary<ulong, Neuron> neurons, List<Synapse> synapses,
            SortedDictionary<ulong, InputNode> inputNodes, SortedDictionary<ulong, OutputNode> outputNodes,
            EventQueue eventQueue, long nextNeuronId, long nextSynapseId, long nextEventId,
            ulong rngS0, ulong rngS1, ulong metricsRngS0, ulong metricsRngS1)
        {
            Config = config;
            CurrentTick = currentTick;
            GlobalHormones = globalHormones;
            ExperimentId = experimentId ?? "deserialized_world";
            _neurons = neurons;
            _synapses = synapses;
            _inputNodes = inputNodes;
            _outputNodes = outputNodes;
            _eventQueue = eventQueue;
            _nextNeuronId = nextNeuronId;
            _nextSynapseId = nextSynapseId;
            _nextEventId = nextEventId;
            _rngS0 = rngS0;
            _rngS1 = rngS1;
            _metricsRngS0 = metricsRngS0;
            _metricsRngS1 = metricsRngS1;

            _worldApiLock = new();
            InitRngsFromState();
            InitMetrics();
        }
        
        /// <summary>
        /// Creates a new, fully initialized HidraWorld from a configuration and a genome script.
        /// </summary>
        /// <param name="config">The simulation configuration settings.</param>
        /// <param name="hglGenome">The HGL script defining the initial genes.</param>
        public HidraWorld(HidraConfig config, string hglGenome)
        : this(config, hglGenome, Enumerable.Empty<ulong>(), Enumerable.Empty<ulong>(), null)
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
        /// <param name="logAction">An optional delegate for routing log messages.</param>
        /// <exception cref="InvalidOperationException">Thrown if the provided genome is missing the required Genesis gene (ID 0).</exception>
        public HidraWorld(HidraConfig config, string hglGenome, IEnumerable<ulong> inputNodeIds, IEnumerable<ulong> outputNodeIds, Action<string, LogLevel, string>? logAction = null)
        {
            SetLogAction(logAction);
            Log("SIM_CORE", LogLevel.Info, "Creating new HidraWorld.");
            
            ExperimentId = Guid.NewGuid().ToString("N");
            Config = config;
            GlobalHormones = new float[256];
            InitRngsFromState();
            InitMetrics();
            _spatialHash = new SpatialHash(Config.CompetitionRadius * 2);

            foreach (var id in inputNodeIds) AddInputNode(id);
            foreach (var id in outputNodeIds) AddOutputNode(id);
            Log("SIM_CORE", LogLevel.Info, $"Created {InputNodes.Count} input nodes and {OutputNodes.Count} output nodes.");
            
            var parser = new HGLParser();
            _compiledGenes = parser.ParseGenome(hglGenome, Config.SystemGeneCount);
            if (!_compiledGenes.ContainsKey(SYS_GENE_GENESIS))
            {
                throw new InvalidOperationException("Genome is invalid: Missing System Genesis gene (Gene 0).");
            }

            lock (_worldApiLock)
            {
                var eventId = (ulong)Interlocked.Increment(ref _nextEventId);
                var payload = new EventPayload { GeneId = SYS_GENE_GENESIS };
                _eventQueue.Push(new Event { Id = eventId, Type = EventType.ExecuteGene, TargetId = 0, ExecutionTick = CurrentTick, Payload = payload });
            }
            
            RebuildSpatialHash();
            
            Log("SIM_CORE", LogLevel.Info, $"New HidraWorld created. World is at Tick 0.");
        }

        /// <summary>
        /// Sets or clears the action to be invoked for logging messages.
        /// </summary>
        /// <param name="logAction">The delegate to handle log messages.</param>
        public void SetLogAction(Action<string, LogLevel, string>? logAction)
        {
            if (logAction == null)
            {
                this._logAction = null;
                return;
            }
            this._logAction = (tag, level, message) => logAction($"[{ExperimentId}] {tag}", level, message);
        }

        /// <summary>
        /// Logs a message via the configured log action.
        /// </summary>
        internal void Log(string tag, LogLevel level, string message)
        {
            _logAction?.Invoke(tag, level, message);
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

        /// <summary>
        /// Initializes the pseudo-random number generators based on configuration and serialized state.
        /// </summary>
        private void InitRngsFromState()
        {
            if (_rngS0 == 0 && _rngS1 == 0)
            {
                var s0 = Config.Seed0;
                var s1 = Config.Seed1 + (Config.AutoReseedPerRun ? (ulong)ExperimentId.GetHashCode() : 0UL);
                _rng = new XorShift128PlusPrng(s0, s1);
            }
            else
            {
                _rng = new XorShift128PlusPrng(_rngS0, _rngS1);
            }
            _rng.GetState(out _rngS0, out _rngS1);

            if (_metricsRngS0 == 0 && _metricsRngS1 == 0)
            {
                var s0 = Config.Seed0 ^ 0xDEADBEEFCAFEBABEUL;
                var s1 = Config.Seed1 ^ 0xC0FFEE15BAADB00DUL;
                _metricsRng = new XorShift128PlusPrng(s0, s1);
            }
            else
            {
                _metricsRng = new XorShift128PlusPrng(_metricsRngS0, _metricsRngS1);
            }
            _metricsRng.GetState(out _metricsRngS0, out _metricsRngS1);
        }

        /// <summary>
        /// Initializes the metrics collection system based on the current configuration.
        /// </summary>
        private void InitMetrics()
        {
            if (!Config.MetricsEnabled || Config.MetricsRingCapacity <= 0) return;
            _metricsRing = new WorldSnapshot[Config.MetricsRingCapacity];
            _metricsHead = 0;
            _metricsWrapped = false;
        }

        /// <summary>
        /// Constructs a snapshot of the current world state for metrics purposes.
        /// </summary>
        /// <param name="includeSynapses">Whether to include detailed synapse data in the snapshot.</param>
        /// <returns>A new <see cref="WorldSnapshot"/> instance.</returns>
        private WorldSnapshot BuildWorldSnapshot(bool includeSynapses)
        {
            var lvarIdx = Config.MetricsLVarIndices ?? Array.Empty<int>();
            var lvars = lvarIdx.AsSpan();

            var takeAll = Config.MetricsNeuronSampleRate >= 0.9999f;
            var neurons = new List<NeuronSnapshot>(takeAll ? _neurons.Count : Math.Max(4, (int)(_neurons.Count * Config.MetricsNeuronSampleRate)));
            foreach (var n in _neurons.Values)
            {
                if (!takeAll && _metricsRng.NextFloat() > Config.MetricsNeuronSampleRate) continue;

                var arr = new float[lvars.Length];
                for (int i = 0; i < lvars.Length; i++)
                {
                    int li = lvars[i];
                    arr[i] = (li >= 0 && li < n.LocalVariables.Length) ? n.LocalVariables[li] : 0f;
                }
                neurons.Add(new NeuronSnapshot(n.Id, n.IsActive, n.Position, arr));
            }

            List<SynapseSnapshot>? syns = null;
            if (includeSynapses && Config.MetricsIncludeSynapses)
            {
                syns = new List<SynapseSnapshot>(_synapses.Count);
                foreach (var s in _synapses)
                {
                    syns.Add(new SynapseSnapshot(s.Id, s.IsActive, s.SourceId, s.TargetId, s.SignalType, s.Weight, s.Parameter, s.FatigueLevel));
                }
            }

            IOSnapshot? io = null;
            if (Config.MetricsIncludeIO)
            {
                var ins  = _inputNodes.ToDictionary(k => k.Key, v => v.Value.Value);
                var outs = _outputNodes.ToDictionary(k => k.Key, v => v.Value.Value);
                io = new IOSnapshot(ins, outs);
            }

            var summary = ComputeTickMetrics();
            return new WorldSnapshot(CurrentTick, neurons, syns, io, summary);
        }

        /// <summary>
        /// Computes summary metrics for the current tick.
        /// </summary>
        /// <returns>A new <see cref="TickMetrics"/> instance with aggregated data.</returns>
        private TickMetrics ComputeTickMetrics()
        {
            int nCount = _neurons.Count;
            int sCount = _synapses.Count;
            int nActive = 0;
            int sActive = 0;

            double sumFR=0, sumHealth=0, sumSoma=0, sumDend=0;
            
            // This relies on LVarIndex enum being defined in another file of this partial class.
            const int LVarIndexFiringRate = 240;
            const int LVarIndexDendriticPotential = 241;
            const int LVarIndexSomaPotential = 242;
            const int LVarIndexHealth = 243;
            
            foreach (var n in _neurons.Values)
            {
                if (n.IsActive) nActive++;
                sumFR     += n.LocalVariables[LVarIndexFiringRate];
                sumDend   += n.LocalVariables[LVarIndexDendriticPotential];
                sumSoma   += n.LocalVariables[LVarIndexSomaPotential];
                sumHealth += n.LocalVariables[LVarIndexHealth];
            }
            foreach (var s in _synapses) if (s.IsActive) sActive++;

            float meanFR     = nCount > 0 ? (float)(sumFR / nCount) : 0f;
            float meanHealth = nCount > 0 ? (float)(sumHealth / nCount) : 0f;
            float meanSoma   = nCount > 0 ? (float)(sumSoma / nCount) : 0f;
            float meanDend   = nCount > 0 ? (float)(sumDend / nCount) : 0f;

            return new TickMetrics(CurrentTick, nCount, nActive, sCount, sActive, meanFR, meanHealth, meanSoma, meanDend);
        }
    }
}
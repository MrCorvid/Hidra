// Hidra.Core/HidraConfig.cs
namespace Hidra.Core
{
    /// <summary>
    /// Holds all tunable configuration parameters for a Hidra simulation.
    /// An instance of this class is typically deserialized from a configuration file
    /// and used to initialize a <see cref="HidraWorld"/>.
    /// </summary>
    public class HidraConfig
    {
        /// <summary>
        /// The base amount of health deducted from each active neuron at every simulation tick.
        /// Represents the base metabolic cost of being alive.
        /// </summary>
        public float MetabolicTaxPerTick { get; set; } = 0.01f;

        /// <summary>
        /// The initial health value assigned to a newly created neuron.
        /// </summary>
        public float InitialNeuronHealth { get; set; } = 100.0f;

        /// <summary>
        /// The initial soma potential of a newly created neuron.
        /// </summary>
        public float InitialPotential { get; set; } = 0.0f;

        /// <summary>
        /// The default rate at which a neuron's soma potential decays each tick. This value is
        /// a multiplier for the remaining potential (e.g., a value of 0.1 means 10% remains).
        /// </summary>
        public float DefaultDecayRate { get; set; } = 0.1f;

        /// <summary>
        /// The default potential value a neuron must reach or exceed to fire.
        /// </summary>
        public float DefaultFiringThreshold { get; set; } = 1.0f;

        #region Oscillation Control Properties
        
        /// <summary>
        /// The default number of ticks a neuron must wait after firing before it can fire again.
        /// </summary>
        public float DefaultRefractoryPeriod { get; set; } = 5.0f;

        /// <summary>
        /// The default factor by which a neuron's firing threshold increases immediately after it fires.
        /// </summary>
        public float DefaultThresholdAdaptationFactor { get; set; } = 0.1f;

        /// <summary>
        /// The default rate at which a neuron's adaptive threshold component decays back towards zero each tick.
        /// </summary>
        public float DefaultThresholdRecoveryRate { get; set; } = 0.05f;

        /// <summary>
        /// The weight for the exponential moving average of a neuron's firing rate. A value closer to 1.0
        /// means the average changes more slowly. Used in the Step() method.
        /// </summary>
        public float FiringRateMAWeight { get; set; } = 0.95f;

        #endregion

        /// <summary>
        /// The radius used for neighborhood searches, such as in `GetNeighbors` and `FindNearestNeighbor`.
        /// Also used to determine the cell size of the `SpatialHash`.
        /// </summary>
        public float CompetitionRadius { get; set; } = 5.0f;

        /// <summary>
        /// A factor that can be used in genes to model the effects of local population density.
        /// (Currently informational, intended for use by HGL scripts).
        /// </summary>
        public float CrowdingFactor { get; set; } = 0.5f;

        /// <summary>
        /// The number of initial genes (starting from ID 0) that are considered "system genes".
        /// These genes have special validation rules during parsing (e.g., they can be empty).
        /// </summary>
        public uint SystemGeneCount { get; set; } = 4;

        /// <summary>
        /// The default number of instructions a neuron can execute via a single gene call.
        /// This is the initial value for the GeneExecutionFuel LVar.
        /// </summary>
        public float DefaultGeneFuel { get; set; } = 1000f;

        #region Determinism & Metrics Configuration

        /// <summary>
        /// If true, the simulation will use a fixed seed for its random number generator,
        /// ensuring repeatable results.
        /// </summary>
        public bool Deterministic { get; set; } = true;
        
        /// <summary>
        /// The first part of the 128-bit seed for the random number generator when
        /// <see cref="Deterministic"/> is true.
        /// </summary>
        public ulong Seed0 { get; set; } = 0x12345678UL;
        
        /// <summary>
        /// The second part of the 128-bit seed for the random number generator when
        /// <see cref="Deterministic"/> is true.
        /// </summary>
        public ulong Seed1 { get; set; } = 0x9ABCDEF0UL;
        
        /// <summary>
        /// If true, the random number generator seed will be updated automatically
        /// between simulation runs to produce different results.
        /// </summary>
        public bool AutoReseedPerRun { get; set; } = false;
        
        /// <summary>
        /// Enables or disables the collection of simulation metrics.
        /// </summary>
        public bool MetricsEnabled { get; set; } = true;
        
        /// <summary>
        /// The interval, in simulation ticks, at which metrics are collected.
        /// A value of 1 means metrics are collected every tick.
        /// </summary>
        public int MetricsCollectionInterval { get; set; } = 1;
        
        /// <summary>
        /// The capacity of the ring buffer used for storing metrics data.
        /// </summary>
        public ushort MetricsRingCapacity { get; set; } = 2048;
        
        /// <summary>
        /// An optional array of LVar indices to be specifically included in metrics collection.
        /// </summary>
        public int[]? MetricsLVarIndices { get; set; } = new[] { 240, 241, 242, 243, 244, 245 };
        
        /// <summary>
        /// The fraction of neurons to sample for metrics collection. A value of 1.0 means all
        /// neurons are sampled; a lower value selects a random subset.
        /// </summary>
        public float MetricsNeuronSampleRate { get; set; } = 1.0f;
        
        /// <summary>
        /// If true, synapse data will be included in metrics. This can be resource-intensive.
        /// </summary>
        public bool MetricsIncludeSynapses { get; set; } = false;
        
        /// <summary>
        /// If true, I/O neuron data will be included in metrics.
        /// </summary>
        public bool MetricsIncludeIO { get; set; } = true;

        #endregion
    }
}
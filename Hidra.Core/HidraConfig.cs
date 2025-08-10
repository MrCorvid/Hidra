// Hidra.Core/HidraConfig.cs
namespace Hidra.Core;

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
    /// multiplied by the current potential at each step (e.g., a value of 0.1 means 90% decay).
    /// </summary>
    public float DefaultDecayRate { get; set; } = 0.1f;

    /// <summary>
    /// The default potential value a neuron must reach or exceed to fire.
    /// </summary>
    public float DefaultFiringThreshold { get; set; } = 1.0f;
    
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
}
// Hidra.Core/Brain/BrainTypes.cs
namespace Hidra.Core.Brain;

/// <summary>
/// Represents a single node (or "neuron") within a NeuralNetwork.
/// </summary>
public class NNNode
{
    /// <summary>
    /// Gets the unique identifier for this node within its parent network.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Gets the type of the node (Input, Hidden, or Output).
    /// </summary>
    public NNNodeType NodeType { get; }

    /// <summary>
    /// Gets or sets the bias value of the node, which is added to its summed input before activation.
    /// </summary>
    public float Bias { get; set; }

    /// <summary>
    /// Gets the current activation value of the node, calculated during the network's Evaluate pass.
    /// </summary>
    /// <remarks>
    /// This property is marked 'internal set' to allow the NeuralNetwork class to modify it
    /// during evaluation, while preventing external classes from changing it.
    /// </remarks>
    public float Value { get; internal set; }

    /// <summary>
    /// Gets or sets the action the world should take if this is an output node.
    /// </summary>
    public OutputActionType ActionType { get; set; }

    /// <summary>
    /// Gets or sets the source of this node's value from the parent Neuron or World if it is an input node.
    /// </summary>
    public InputSourceType InputSource { get; set; } = InputSourceType.ActivationPotential;

    /// <summary>
    /// Gets or sets an index used by some InputSourceTypes (e.g., LocalVariable, GlobalHormone) to specify
    /// which variable or hormone to read from.
    /// </summary>
    public int SourceIndex { get; set; }

    /// <summary>
    /// Gets or sets the activation function to apply to the node's summed input.
    /// </summary>
    public ActivationFunctionType ActivationFunction { get; set; } = ActivationFunctionType.Tanh;

    /// <summary>
    /// Initializes a new instance of the <see cref="NNNode"/> class.
    /// </summary>
    /// <param name="id">The unique identifier for the node.</param>
    /// <param name="nodeType">The type of the node.</param>
    public NNNode(int id, NNNodeType nodeType)
    {
        Id = id;
        NodeType = nodeType;
    }
}

/// <summary>
/// Represents a weighted, directed connection between two NNNodes.
/// </summary>
public class NNConnection
{
    /// <summary>
    /// Gets the ID of the node where the connection originates.
    /// </summary>
    public int FromNodeId { get; }

    /// <summary>
    /// Gets the ID of the node where the connection terminates.
    /// </summary>
    public int ToNodeId { get; }

    /// <summary>
    /// Gets or sets the weight of the connection. The source node's value is multiplied by this weight
    /// before being added to the target node's input sum.
    /// </summary>
    public float Weight { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NNConnection"/> class.
    /// </summary>
    /// <param name="fromNodeId">The ID of the source node.</param>
    /// <param name="toNodeId">The ID of the target node.</param>
    /// <param name="weight">The weight of the connection.</param>
    public NNConnection(int fromNodeId, int toNodeId, float weight)
    {
        FromNodeId = fromNodeId;
        ToNodeId = toNodeId;
        Weight = weight;
    }
}

/// <summary>
/// Defines the role of a node within the neural network.
/// </summary>
public enum NNNodeType
{
    /// <summary>A node that receives external input.</summary>
    Input,
    /// <summary>An intermediate node in the network's hidden layers.</summary>
    Hidden,
    /// <summary>A node that produces the final output of the network.</summary>
    Output
}

/// <summary>
/// Defines the mathematical function used to calculate a node's output activation.
/// </summary>
public enum ActivationFunctionType
{
    /// <summary>Hyperbolic tangent function, squashes values to a range of [-1, 1].</summary>
    Tanh,
    /// <summary>No transformation is applied (output = input).</summary>
    Linear,
    /// <summary>Logistic function, squashes values to a range of [0, 1].</summary>
    Sigmoid,
    /// <summary>Rectified Linear Unit, outputs the input if it is positive, otherwise outputs 0.</summary>
    ReLU
}

/// <summary>
/// Defines properties of a brain node that can be modified by genes.
/// </summary>
public enum BrainNodeProperty
{
    /// <summary>The node's bias value.</summary>
    Bias = 0,
    /// <summary>The node's activation function type.</summary>
    ActivationFunction = 1
}
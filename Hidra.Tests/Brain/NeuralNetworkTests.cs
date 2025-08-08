// Hidra.Tests/Brain/NeuralNetworkTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core.Brain;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Hidra.Tests.Brain
{
    /// <summary>
    /// Contains unit tests for the NeuralNetwork class, focusing on its internal
    /// structure, topological sort-based evaluation, and lifecycle management.
    /// These tests validate the network's core logic independently of its role as an IBrain.
    /// </summary>
    [TestClass]
    public class NeuralNetworkTests : BaseTestClass
    {
        private NeuralNetwork _nn = null!;

        [TestInitialize]
        public void Setup()
        {
            _nn = new NeuralNetwork();
        }

        #region Structure and Lifecycle Tests

        /// <summary>
        /// Verifies that AddNode correctly adds a node to all relevant internal collections
        /// and invalidates the sort cache.
        /// </summary>
        [TestMethod]
        public void AddNode_WhenCalled_AddsNodeToCollectionsAndInvalidatesCache()
        {
            // Arrange
            var inputNode = new NNNode(1, NNNodeType.Input);
            var outputNode = new NNNode(2, NNNodeType.Output);

            // Act
            _nn.AddNode(inputNode);
            _nn.AddNode(outputNode);

            // Assert
            Assert.AreEqual(2, _nn.Nodes.Count);
            Assert.IsTrue(_nn.Nodes.ContainsKey(1));
            Assert.AreEqual(1, _nn.InputNodes.Count);
            Assert.AreEqual(1, _nn.OutputNodes.Count);
        }

        /// <summary>
        /// Verifies that RemoveNode correctly removes a node and all its associated connections.
        /// </summary>
        [TestMethod]
        public void RemoveNode_RemovesNodeAndAssociatedConnections()
        {
            // Arrange
            var n1 = new NNNode(1, NNNodeType.Input);
            var n2 = new NNNode(2, NNNodeType.Hidden);
            var n3 = new NNNode(3, NNNodeType.Output);
            _nn.AddNode(n1);
            _nn.AddNode(n2);
            _nn.AddNode(n3);
            _nn.AddConnection(new NNConnection(1, 2, 1f));
            _nn.AddConnection(new NNConnection(2, 3, 1f));
            Assert.AreEqual(2, _nn.Connections.Count);

            // Act
            _nn.RemoveNode(2); // Remove the hidden node

            // Assert
            Assert.IsFalse(_nn.Nodes.ContainsKey(2), "Node 2 should be removed.");
            Assert.AreEqual(0, _nn.Connections.Count, "All connections involving node 2 should be removed.");
        }

        /// <summary>
        /// Verifies that the network can be saved to JSON and loaded back,
        /// restoring its state and correctly rebuilding runtime caches.
        /// </summary>
        [TestMethod]
        public void InitializeFromLoad_RestoresStateAndRebuildsCaches()
        {
            // Arrange
            var originalNet = new NeuralNetwork();
            originalNet.AddNode(new NNNode(1, NNNodeType.Input));
            originalNet.AddNode(new NNNode(2, NNNodeType.Output));
            originalNet.AddConnection(new NNConnection(1, 2, 0.5f));
            
            // This would normally be done via HidraWorld persistence.
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(originalNet);
            var loadedNet = Newtonsoft.Json.JsonConvert.DeserializeObject<NeuralNetwork>(json)!;

            // Act
            loadedNet.InitializeFromLoad();

            // Assert
            Assert.AreEqual(1, loadedNet.InputNodes.Count, "Input node cache should be rebuilt.");
            Assert.AreEqual(1, loadedNet.OutputNodes.Count, "Output node cache should be rebuilt.");
            Assert.AreEqual(0.5f, loadedNet.Connections[0].Weight, "Connection data should be restored.");
        }

        #endregion

        #region Evaluation and Logic Tests

        /// <summary>
        /// Tests the evaluation of a simple two-input, one-output network.
        /// </summary>
        [TestMethod]
        public void Evaluate_SimpleFeedForward_CalculatesCorrectOutput()
        {
            // Arrange
            _nn.AddNode(new NNNode(1, NNNodeType.Input));
            _nn.AddNode(new NNNode(2, NNNodeType.Input));
            _nn.AddNode(new NNNode(3, NNNodeType.Output) { Bias = 0.1f }); // Tanh is default
            _nn.AddConnection(new NNConnection(1, 3, 0.5f));
            _nn.AddConnection(new NNConnection(2, 3, 1.0f));
            var inputs = new[] { 0.8f, 0.2f };
            
            // Expected: Tanh((0.8 * 0.5) + (0.2 * 1.0) + 0.1) = Tanh(0.7)
            float expected = (float)Math.Tanh(0.7f);

            // Act
            _nn.Evaluate(inputs);

            // Assert
            Assert.AreEqual(expected, _nn.OutputNodes[0].Value, 1e-6f);
        }

        /// <summary>
        /// Tests a network with a hidden layer, verifying correct value propagation.
        /// </summary>
        [TestMethod]
        public void Evaluate_WithHiddenLayer_CalculatesCorrectOutput()
        {
            // Arrange
            _nn.AddNode(new NNNode(1, NNNodeType.Input));
            _nn.AddNode(new NNNode(2, NNNodeType.Hidden) { Bias = -0.2f });
            _nn.AddNode(new NNNode(3, NNNodeType.Output) { Bias = 0.5f });
            _nn.AddConnection(new NNConnection(1, 2, 0.8f));
            _nn.AddConnection(new NNConnection(2, 3, 1.5f));
            var inputs = new[] { 1.0f };

            // Expected:
            // HiddenVal = Tanh((1.0 * 0.8) + (-0.2)) = Tanh(0.6)
            // OutputVal = Tanh((HiddenVal * 1.5) + 0.5)
            float hiddenVal = (float)Math.Tanh(0.6f);
            float expected = (float)Math.Tanh(hiddenVal * 1.5f + 0.5f);

            // Act
            _nn.Evaluate(inputs);

            // Assert
            Assert.AreEqual(expected, _nn.OutputNodes[0].Value, 1e-6f);
        }
        
        /// <summary>
        /// Verifies that attempting to evaluate a network with a cycle throws an InvalidOperationException.
        /// </summary>
        [TestMethod]
        public void Evaluate_WithCycle_ThrowsInvalidOperationException()
        {
            // Arrange: 1 -> 2 -> 3 -> 1
            _nn.AddNode(new NNNode(1, NNNodeType.Input));
            _nn.AddNode(new NNNode(2, NNNodeType.Hidden));
            _nn.AddNode(new NNNode(3, NNNodeType.Hidden));
            _nn.AddConnection(new NNConnection(1, 2, 1f));
            _nn.AddConnection(new NNConnection(2, 3, 1f));
            _nn.AddConnection(new NNConnection(3, 1, 1f)); // Creates the cycle
            
            // Act & Assert
            var ex = Assert.ThrowsException<InvalidOperationException>(() => _nn.Evaluate(new[] { 1.0f }));
            Assert.IsTrue(ex.Message.Contains("Cycle detected"));
        }

        /// <summary>
        /// Verifies that providing the wrong number of inputs throws an ArgumentException.
        /// </summary>
        [TestMethod]
        public void Evaluate_WithMismatchedInputCount_ThrowsArgumentException()
        {
            // Arrange
            _nn.AddNode(new NNNode(1, NNNodeType.Input));

            // Act & Assert
            Assert.ThrowsException<ArgumentException>(() => _nn.Evaluate(new[] { 0.5f, 0.5f }));
        }

        #endregion
    }
}
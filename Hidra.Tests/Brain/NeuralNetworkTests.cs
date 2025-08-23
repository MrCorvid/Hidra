// Hidra.Tests/Core/Brain/NeuralNetworkTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using Hidra.Core.Brain;
using Hidra.Core.Logging; // Keep this for LogLevel enum
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Hidra.Tests.Core.Brain
{
    [TestClass]
    public class NeuralNetworkTests : BaseTestClass
    {
        private NeuralNetwork _network = null!;

        [TestInitialize]
        public override void BaseInit()
        {
            base.BaseInit();
            _network = new NeuralNetwork();
        }

        #region Helper Methods

        private List<NNNode>? GetSortedNodesCache(NeuralNetwork network)
        {
            var field = typeof(NeuralNetwork).GetField("_sortedNodes", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "Could not find private field _sortedNodes.");
            return (List<NNNode>?)field.GetValue(network);
        }

        #endregion

        #region State Management Tests

        [TestMethod]
        public void AddNode_CorrectlyPopulatesCollectionsAndInvalidatesCache()
        {
            // --- ARRANGE ---
            var input = new NNNode(0, NNNodeType.Input);
            var output = new NNNode(1, NNNodeType.Output);
            _network.Evaluate(Array.Empty<float>()); // Prime the sort cache
            Assert.IsNotNull(GetSortedNodesCache(_network), "Cache should be primed before mutation.");

            // --- ACT ---
            _network.AddNode(input);
            _network.AddNode(output);

            // --- ASSERT ---
            Assert.AreEqual(2, _network.Nodes.Count);
            Assert.AreEqual(1, _network.InputNodes.Count);
            Assert.AreEqual(1, _network.OutputNodes.Count);
            Assert.AreEqual(input.Id, _network.InputNodes[0].Id);
            Assert.IsNull(GetSortedNodesCache(_network), "Adding a node should invalidate the sort cache.");
        }
        
        [TestMethod]
        public void AddConnection_CorrectlyPopulatesCollectionsAndInvalidatesCache()
        {
            // --- ARRANGE ---
            var node1 = new NNNode(0, NNNodeType.Input);
            var node2 = new NNNode(1, NNNodeType.Output);
            _network.AddNode(node1);
            _network.AddNode(node2);
            _network.Evaluate(new[] { 0f }); // Prime the cache
            Assert.IsNotNull(GetSortedNodesCache(_network));

            // --- ACT ---
            _network.AddConnection(new NNConnection(0, 1, 0.5f));

            // --- ASSERT ---
            Assert.AreEqual(1, _network.Connections.Count);
            Assert.IsNull(GetSortedNodesCache(_network), "Adding a connection should invalidate the sort cache.");
        }

        [TestMethod]
        public void RemoveNode_RemovesNodeAndAllAssociatedConnections()
        {
            // --- ARRANGE ---
            var n0 = new NNNode(0, NNNodeType.Input);
            var n1 = new NNNode(1, NNNodeType.Hidden);
            var n2 = new NNNode(2, NNNodeType.Output);
            _network.AddNode(n0);
            _network.AddNode(n1);
            _network.AddNode(n2);
            _network.AddConnection(new NNConnection(0, 1, 1f));
            _network.AddConnection(new NNConnection(1, 2, 1f));
            _network.AddConnection(new NNConnection(0, 2, 1f));

            // --- ACT ---
            _network.RemoveNode(1);

            // --- ASSERT ---
            Assert.AreEqual(2, _network.Nodes.Count);
            Assert.IsFalse(_network.Nodes.ContainsKey(1));
            Assert.AreEqual(1, _network.Connections.Count, "Only one connection should remain.");
            Assert.AreEqual(0, _network.Connections[0].FromNodeId);
            Assert.AreEqual(2, _network.Connections[0].ToNodeId);
        }

        [TestMethod]
        public void Clear_ResetsAllInternalState()
        {
            _network.AddNode(new NNNode(0, NNNodeType.Input));
            _network.AddConnection(new NNConnection(0, 0, 0));
            _network.InitializeFromLoad(); 

            _network.Clear();

            Assert.AreEqual(0, _network.Nodes.Count);
            Assert.AreEqual(0, _network.Connections.Count);
            Assert.AreEqual(0, _network.InputNodes.Count);
            Assert.AreEqual(0, _network.OutputNodes.Count);
            Assert.IsNull(GetSortedNodesCache(_network));
        }

        #endregion

        #region Evaluation Logic

        [TestMethod]
        public void Evaluate_WithSimpleNetwork_CalculatesCorrectOutput()
        {
            var inputNode = new NNNode(0, NNNodeType.Input);
            var outputNode = new NNNode(1, NNNodeType.Output) { Bias = 0.1f, ActivationFunction = ActivationFunctionType.Linear };
            _network.AddNode(inputNode);
            _network.AddNode(outputNode);
            _network.AddConnection(new NNConnection(0, 1, 0.5f));
            
            _network.Evaluate(new[] { 10f });

            AreClose(5.1f, outputNode.Value);
        }
        
        [TestMethod]
        public void Evaluate_AppliesActivationFunctionsCorrectly()
        {
            var input = new NNNode(0, NNNodeType.Input);
            _network.AddNode(input);
            var nodes = new Dictionary<ActivationFunctionType, NNNode>();
            
            foreach (ActivationFunctionType funcType in Enum.GetValues(typeof(ActivationFunctionType)))
            {
                var node = new NNNode((int)funcType + 1, NNNodeType.Hidden) { ActivationFunction = funcType };
                nodes[funcType] = node;
                _network.AddNode(node);
                _network.AddConnection(new NNConnection(0, node.Id, 1.0f));
            }

            _network.Evaluate(new[] { 0.8f });

            AreClose(0.8f, nodes[ActivationFunctionType.Linear].Value);
            AreClose((float)Math.Tanh(0.8), nodes[ActivationFunctionType.Tanh].Value);
            AreClose(1.0f / (1.0f + (float)Math.Exp(-0.8)), nodes[ActivationFunctionType.Sigmoid].Value);
            AreClose(0.8f, nodes[ActivationFunctionType.ReLU].Value);
        }

        [TestMethod]
        public void Evaluate_WithMultiLayerNetwork_PropagatesValuesCorrectly()
        {
            var n_in = new NNNode(0, NNNodeType.Input);
            var n_h1 = new NNNode(1, NNNodeType.Hidden) { ActivationFunction = ActivationFunctionType.Linear };
            var n_h2 = new NNNode(2, NNNodeType.Hidden) { ActivationFunction = ActivationFunctionType.Linear, Bias = 0.1f };
            var n_out = new NNNode(3, NNNodeType.Output) { ActivationFunction = ActivationFunctionType.Linear };
            _network.AddNode(n_in); _network.AddNode(n_h1); _network.AddNode(n_h2); _network.AddNode(n_out);
            _network.AddConnection(new NNConnection(0, 1, 0.5f));
            _network.AddConnection(new NNConnection(0, 2, 2.0f));
            _network.AddConnection(new NNConnection(1, 3, 1.0f));
            _network.AddConnection(new NNConnection(2, 3, 3.0f));

            _network.Evaluate(new[] { 10f });

            AreClose(65.3f, n_out.Value);
        }
        
        #endregion

        #region Topological Sort and Caching

        [TestMethod]
        public void Evaluate_CachesTopologicalSortOnFirstRun()
        {
            _network.AddNode(new NNNode(0, NNNodeType.Input));
            Assert.IsNull(GetSortedNodesCache(_network), "Cache should be null initially.");
            
            _network.Evaluate(new[] { 0f });
            var cache1 = GetSortedNodesCache(_network);
            Assert.IsNotNull(cache1, "Cache should be populated after first evaluation.");

            _network.Evaluate(new[] { 0f });
            var cache2 = GetSortedNodesCache(_network);
            Assert.AreSame(cache1, cache2, "Cache should be reused on subsequent evaluations.");
        }

        [TestMethod]
        public void Evaluate_AfterMutation_RecalculatesSortCache()
        {
            _network.AddNode(new NNNode(0, NNNodeType.Input));
            _network.Evaluate(new[] { 0f });
            var oldCache = GetSortedNodesCache(_network);
            
            _network.AddNode(new NNNode(1, NNNodeType.Output));
            Assert.IsNull(GetSortedNodesCache(_network), "Cache should be invalidated by AddNode.");

            _network.Evaluate(new[] { 0f });
            var newCache = GetSortedNodesCache(_network);
            Assert.IsNotNull(newCache);
            Assert.AreNotSame(oldCache, newCache, "A new cache should be created after mutation.");
        }

        #endregion

        #region Errors and Edge Cases

        [TestMethod]
        public void Evaluate_WithMismatchedInputs_LogsErrorAndReturns()
        {
            var loggedMessages = new List<(LogLevel Level, string Message)>();
            Action<string, LogLevel, string> testLogAction = (tag, level, message) =>
            {
                loggedMessages.Add((level, message));
            };

            _network.AddNode(new NNNode(0, NNNodeType.Input));
            
            _network.Evaluate(new float[] { 1f, 2f }, testLogAction);

            Assert.IsTrue(loggedMessages.Any(log => log.Level == LogLevel.Error && log.Message.Contains("Mismatched input count")), "Should log an error for mismatched inputs.");
        }

        [TestMethod]
        public void AddConnection_ToNonExistentNode_ReturnsFalseAndLogsWarning()
        {
            var loggedMessages = new List<(LogLevel Level, string Message)>();
            Action<string, LogLevel, string> testLogAction = (tag, level, message) =>
            {
                loggedMessages.Add((level, message));
            };

            _network.AddNode(new NNNode(0, NNNodeType.Input));
            
            bool result = _network.AddConnection(new NNConnection(0, 99, 1f), testLogAction);

            Assert.IsFalse(result, "AddConnection should return false for non-existent nodes.");
            Assert.IsTrue(loggedMessages.Any(log => log.Level == LogLevel.Warning && log.Message.Contains("Node(s) not found")), "Should log a warning for non-existent nodes.");
        }

        [TestMethod]
        public void Evaluate_WithCyclicNetwork_LogsError()
        {
            var loggedMessages = new List<(LogLevel Level, string Message)>();
            Action<string, LogLevel, string> testLogAction = (tag, level, message) =>
            {
                loggedMessages.Add((level, message));
            };
            
            var n1 = new NNNode(0, NNNodeType.Hidden);
            var n2 = new NNNode(1, NNNodeType.Hidden);
            _network.AddNode(n1);
            _network.AddNode(n2);
            
            // Intentionally bypass the AddConnection cycle check for testing the sort
            var connectionsField = typeof(NeuralNetwork).GetField("_connections", BindingFlags.NonPublic | BindingFlags.Instance);
            var connectionsList = (List<NNConnection>)connectionsField!.GetValue(_network)!;
            connectionsList.Add(new NNConnection(0, 1, 1f));
            connectionsList.Add(new NNConnection(1, 0, 1f));
            
            _network.InitializeFromLoad(testLogAction);

            _network.Evaluate(Array.Empty<float>(), testLogAction);
            
            Assert.IsTrue(loggedMessages.Any(log => log.Level == LogLevel.Error && log.Message.Contains("Cycle detected")), "Should log an error when a cycle is detected during evaluation.");
        }

        #endregion
    }
}
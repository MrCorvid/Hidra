// Hidra.Tests/Core/Brain/NeuralNetworkBrainTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using Hidra.Core.Brain;
using System.Reflection;
using System.Linq;

namespace Hidra.Tests.Core.Brain
{
    [TestClass]
    public class NeuralNetworkBrainTests : BaseTestClass
    {
        private NeuralNetworkBrain _brain = null!;
        private NeuralNetwork _network = null!;

        #region Test Helper Mocks

        private class MockPrng : IPrng
        {
            public int NextIntValue { get; set; }
            public float NextFloatValue { get; set; }
            public int NextInt(int min, int max) => NextIntValue;
            public float NextFloat() => NextFloatValue;
            public ulong NextULong() => 0;
            public double NextDouble() => 0;
            public void GetState(out ulong s0, out ulong s1) { s0 = 1; s1 = 1; }
            public void SetState(ulong s0, ulong s1) { }
        }

        #endregion

        #region Reflection Helpers

        private bool GetDirtyFlag(NeuralNetworkBrain brain)
        {
            var field = typeof(NeuralNetworkBrain).GetField("_isDirty", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "Could not find private field _isDirty.");
            return (bool)field.GetValue(brain)!;
        }

        #endregion

        [TestInitialize]
        public override void BaseInit()
        {
            base.BaseInit();
            _brain = new NeuralNetworkBrain();
            _network = _brain.GetInternalNetwork();
        }

        #region Initialization and Caching

        [TestMethod]
        public void InputMap_BuildsCacheOnFirstAccessAndReusesIt()
        {
            _network.AddNode(new NNNode(0, NNNodeType.Input) { InputSource = InputSourceType.Health });
            var map1 = _brain.InputMap;
            _ = _brain.OutputMap; // Clears the dirty flag
            var map2 = _brain.InputMap;
            Assert.AreSame(map1, map2, "When not dirty, the same cache instance should be returned.");
        }
        
        [TestMethod]
        public void OutputMap_BuildsCacheClearsDirtyFlagAndUpdateValues()
        {
            var inputNode = new NNNode(0, NNNodeType.Input);
            var outputNode = new NNNode(1, NNNodeType.Output) { ActivationFunction = ActivationFunctionType.Linear };
            _network.AddNode(inputNode);
            _network.AddNode(outputNode);
            _network.AddConnection(new NNConnection(0, 1, 1.0f));

            _brain.Evaluate(new[] { 0.25f });
            var map1 = _brain.OutputMap;

            Assert.AreEqual(1, map1.Count);
            AreClose(0.25f, map1[0].Value);
            Assert.IsFalse(GetDirtyFlag(_brain), "Accessing OutputMap should clear the dirty flag.");

            _brain.Evaluate(new[] { 0.75f });
            var map2 = _brain.OutputMap;

            Assert.AreSame(map1, map2, "The same cache instance should be returned for OutputMap.");
            AreClose(0.75f, map2[0].Value, message: "The value in the cached map should be updated on access.");
        }

        [TestMethod]
        public void GetInternalNetwork_SetsDirtyFlagToTrue()
        {
            _ = _brain.OutputMap; // clear flag
            Assert.IsFalse(GetDirtyFlag(_brain));
            
            _brain.GetInternalNetwork();
            
            Assert.IsTrue(GetDirtyFlag(_brain), "Getting the internal network must mark the cache as dirty.");
        }
        
        #endregion

        #region Core Logic

        [TestMethod]
        public void Evaluate_DelegatesToInternalNetworkCorrectly()
        {
            var inputNode = new NNNode(0, NNNodeType.Input);
            var outputNode = new NNNode(1, NNNodeType.Output) { ActivationFunction = ActivationFunctionType.Linear };
            _network.AddNode(inputNode);
            _network.AddNode(outputNode);
            _network.AddConnection(new NNConnection(0, 1, 0.5f));
            
            _brain.Evaluate(new[] { 10f });

            AreClose(5.0f, outputNode.Value);
        }

        [TestMethod]
        public void Mutate_WithPrng_ModifiesWeightAndBiasDeterministically()
        {
            // --- ARRANGE ---
            var mockPrng = new MockPrng { NextIntValue = 0, NextFloatValue = 0.75f };
            _brain.SetPrng(mockPrng);
            
            var node0 = new NNNode(0, NNNodeType.Input) { Bias = 1.0f };
            var node1 = new NNNode(1, NNNodeType.Output);
            _network.AddNode(node0);
            _network.AddNode(node1);
            
            var connection = new NNConnection(0, 1, 2.0f);
            var addResult = _network.AddConnection(connection);
            Assert.IsTrue(addResult, "Connection should be added successfully.");

            float rate = 0.1f;
            float expectedWeight = 2.0f + ((0.75f * 2f - 1f) * rate);
            float expectedBias = 1.0f + ((0.75f * 2f - 1f) * rate);
            
            // --- ACT ---
            _brain.Mutate(rate);
            
            // --- ASSERT ---
            AreClose(expectedWeight, connection.Weight, message: "Connection weight was not mutated correctly.");
            AreClose(expectedBias, node0.Bias, message: "Node bias was not mutated correctly.");
        }
        
        #endregion
    }
}
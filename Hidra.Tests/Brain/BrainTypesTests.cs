// Hidra.Tests/Core/Brain/BrainTypesTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using Hidra.Core.Brain;

namespace Hidra.Tests.Core.Brain
{
    [TestClass]
    public class BrainTypesTests : BaseTestClass
    {
        #region NNNode Tests

        [TestMethod]
        public void NNNode_Constructor_AssignsReadOnlyPropertiesAndDefaults()
        {
            // --- ARRANGE ---
            var node = new NNNode(123, NNNodeType.Hidden);

            // --- ASSERT ---
            Assert.AreEqual(123, node.Id);
            Assert.AreEqual(NNNodeType.Hidden, node.NodeType);
            IsZero(node.Bias, message: "Default Bias should be 0.");
            IsZero(node.Value, message: "Default Value should be 0.");
            
            // FIX: The default is now correctly SetOutputValue.
            Assert.AreEqual(OutputActionType.SetOutputValue, node.ActionType);
            
            Assert.AreEqual(InputSourceType.ActivationPotential, node.InputSource);
            Assert.AreEqual(0, node.SourceIndex);
            Assert.AreEqual(ActivationFunctionType.Tanh, node.ActivationFunction);
        }

        [TestMethod]
        public void NNNode_Properties_CanBeSetAndRetrieved()
        {
            // --- ARRANGE ---
            var node = new NNNode(1, NNNodeType.Output);

            // --- ACT ---
            node.Bias = -0.5f;
            node.ActionType = OutputActionType.ExecuteGene;
            node.InputSource = InputSourceType.Health;
            node.SourceIndex = 5;
            node.ActivationFunction = ActivationFunctionType.ReLU;

            // --- ASSERT ---
            AreClose(-0.5f, node.Bias);
            Assert.AreEqual(OutputActionType.ExecuteGene, node.ActionType);
            Assert.AreEqual(InputSourceType.Health, node.InputSource);
            Assert.AreEqual(5, node.SourceIndex);
            Assert.AreEqual(ActivationFunctionType.ReLU, node.ActivationFunction);
        }

        #endregion

        #region NNConnection Tests

        [TestMethod]
        public void NNConnection_Constructor_AssignsAllPropertiesCorrectly()
        {
            // --- ARRANGE ---
            const int fromId = 10;
            const int toId = 20;
            const float weight = 0.75f;

            // --- ACT ---
            var connection = new NNConnection(fromId, toId, weight);

            // --- ASSERT ---
            Assert.AreEqual(fromId, connection.FromNodeId);
            Assert.AreEqual(toId, connection.ToNodeId);
            // FIX: Use a named argument for the message to avoid type mismatch.
            AreClose(weight, connection.Weight, message: "Initial weight should be set by the constructor.");
        }

        [TestMethod]
        public void NNConnection_WeightProperty_CanBeModifiedAfterConstruction()
        {
            // --- ARRANGE ---
            var connection = new NNConnection(1, 2, 1.0f);

            // --- ACT ---
            connection.Weight = -0.25f;

            // --- ASSERT ---
            // FIX: Use a named argument for the message.
            AreClose(-0.25f, connection.Weight, message: "The Weight property should be mutable.");
        }

        #endregion

        #region Enum Tests

        [TestMethod]
        public void BrainNodePropertyEnum_HasCorrectUnderlyingValuesForApi()
        {
            Assert.AreEqual(0, (int)BrainNodeProperty.Bias);
            Assert.AreEqual(1, (int)BrainNodeProperty.ActivationFunction);
        }

        #endregion
    }
}
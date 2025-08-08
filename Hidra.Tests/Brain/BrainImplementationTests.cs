// Hidra.Tests/Brain/BrainImplementationsTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core.Brain;
using Hidra.Core;
using System.Linq;

namespace Hidra.Tests.Brain
{
    /// <summary>
    /// Contains tests for the specific IBrain implementations, such as NeuralNetworkBrain
    /// and LogicGateBrain. These tests verify that the brains correctly adapt their
    /// internal logic to the IBrain interface, managing state and I/O maps properly.
    /// </summary>
    [TestClass]
    public class BrainImplementationsTests : BaseTestClass
    {
        #region NeuralNetworkBrain Tests

        /// <summary>
        /// Verifies that the NeuralNetworkBrain's InputMap and OutputMap correctly
        /// reflect the structure of the underlying NeuralNetwork.
        /// </summary>
        [TestMethod]
        public void NeuralNetworkBrain_Maps_ReflectInternalNetworkStructure()
        {
            // Arrange
            var brain = new NeuralNetworkBrain();
            var network = brain.GetInternalNetwork(); // Get the internal network to modify it

            // Configure the network
            network.AddNode(new NNNode(1, NNNodeType.Input) { InputSource = InputSourceType.Health });
            network.AddNode(new NNNode(2, NNNodeType.Input) { InputSource = InputSourceType.LocalVariable, SourceIndex = 10 });
            network.AddNode(new NNNode(3, NNNodeType.Output) { ActionType = OutputActionType.ExecuteGene });

            // Act
            var inputMap = brain.InputMap;
            var outputMap = brain.OutputMap;

            // Assert
            Assert.AreEqual(2, inputMap.Count);
            Assert.AreEqual(InputSourceType.Health, inputMap[0].SourceType);
            Assert.AreEqual(InputSourceType.LocalVariable, inputMap[1].SourceType);
            Assert.AreEqual(10, inputMap[1].SourceIndex);

            Assert.AreEqual(1, outputMap.Count);
            Assert.AreEqual(OutputActionType.ExecuteGene, outputMap[0].ActionType);
        }

        /// <summary>
        /// Verifies that the OutputMap of a NeuralNetworkBrain is correctly updated
        /// with the latest values after an evaluation.
        /// </summary>
        [TestMethod]
        public void NeuralNetworkBrain_OutputMap_UpdatesValueAfterEvaluation()
        {
            // Arrange
            var brain = new NeuralNetworkBrain();
            var network = brain.GetInternalNetwork();
            network.AddNode(new NNNode(1, NNNodeType.Input) { InputSource = InputSourceType.ConstantOne });
            network.AddNode(new NNNode(2, NNNodeType.Output) { ActionType = OutputActionType.SetOutputValue, ActivationFunction = ActivationFunctionType.Linear });
            network.AddConnection(new NNConnection(1, 2, 5.0f));

            // Sanity check before evaluation
            Assert.AreEqual(0f, brain.OutputMap[0].Value, "Initial output value should be 0.");

            // Act: Evaluate the brain. Input is [1.0f] due to ConstantOne.
            brain.Evaluate(new[] { 1.0f });
            
            // Assert: The OutputMap should now reflect the new value.
            // Expected: (1.0 * 5.0) -> Linear activation -> 5.0
            Assert.AreEqual(5.0f, brain.OutputMap[0].Value, "OutputMap value should be updated after evaluation.");
        }

        #endregion

        #region LogicGateBrain Tests

        /// <summary>
        /// Verifies that a LogicGateBrain configured as an AND gate produces the correct output.
        /// </summary>
        [TestMethod]
        public void LogicGateBrain_AsANDGate_ProducesCorrectOutput()
        {
            // Arrange
            var brain = new LogicGateBrain { GateType = LogicGateType.AND, Threshold = 0.5f };
            brain.ClearInputs();
            brain.AddInput(InputSourceType.ActivationPotential, 0); // Not used by test, just for mapping
            brain.AddInput(InputSourceType.ActivationPotential, 0);

            // Act & Assert
            brain.Evaluate(new[] { 1.0f, 1.0f }); // TRUE, TRUE
            Assert.AreEqual(1.0f, brain.OutputMap[0].Value, "1 AND 1 should be 1.");

            brain.Evaluate(new[] { 1.0f, 0.0f }); // TRUE, FALSE
            Assert.AreEqual(0.0f, brain.OutputMap[0].Value, "1 AND 0 should be 0.");

            brain.Evaluate(new[] { 0.0f, 0.0f }); // FALSE, FALSE
            Assert.AreEqual(0.0f, brain.OutputMap[0].Value, "0 AND 0 should be 0.");
        }

        /// <summary>
        /// Verifies that a LogicGateBrain configured as a D-type flip-flop correctly
        /// latches data on the rising edge of the clock signal.
        /// </summary>
        [TestMethod]
        public void LogicGateBrain_AsDFlipFlop_LatchesDataOnRisingEdge()
        {
            // Arrange
            var brain = new LogicGateBrain { FlipFlop = FlipFlopType.D_FlipFlop, Threshold = 0.5f };
            brain.ClearInputs();
            brain.AddInput(InputSourceType.ActivationPotential, 0); // Input 0: Clock
            brain.AddInput(InputSourceType.ActivationPotential, 0); // Input 1: Data (D)
            
            // --- Step 1: Clock is low, Data is high. Output should not change.
            brain.Evaluate(new[] { 0.0f, 1.0f });
            Assert.AreEqual(0.0f, brain.OutputMap[0].Value, "Output should hold state when clock is low.");

            // --- Step 2: Clock rises. Data is high. Output should latch to high.
            brain.Evaluate(new[] { 1.0f, 1.0f });
            Assert.AreEqual(1.0f, brain.OutputMap[0].Value, "Output should latch Data on rising edge.");
            
            // --- Step 3: Clock is high, Data goes low. Output should not change.
            brain.Evaluate(new[] { 1.0f, 0.0f });
            Assert.AreEqual(1.0f, brain.OutputMap[0].Value, "Output should hold state when clock is high.");
            
            // --- Step 4: Clock falls. Data is low. Output should not change.
            brain.Evaluate(new[] { 0.0f, 0.0f });
            Assert.AreEqual(1.0f, brain.OutputMap[0].Value, "Output should hold state on falling edge.");
            
            // --- Step 5: Clock rises again. Data is low. Output should latch to low.
            brain.Evaluate(new[] { 1.0f, 0.0f });
            Assert.AreEqual(0.0f, brain.OutputMap[0].Value, "Output should latch new Data on next rising edge.");
        }

        #endregion
    }
}
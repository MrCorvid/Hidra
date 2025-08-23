// Hidra.Tests/Core/Brain/BrainLogicGateTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using Hidra.Core.Brain;
using System.Reflection;

namespace Hidra.Tests.Core.Brain
{
    [TestClass]
    public class LogicGateBrainTests : BaseTestClass
    {
        private LogicGateBrain _brain = null!;

        [TestInitialize]
        public override void BaseInit()
        {
            base.BaseInit();
            _brain = new LogicGateBrain();
        }
        
        #region Reflection Helpers

        private void SetInternalState(LogicGateBrain brain, float state, float previousClock)
        {
            var stateField = typeof(LogicGateBrain).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            var clockField = typeof(LogicGateBrain).GetField("_previousClock", BindingFlags.NonPublic | BindingFlags.Instance);
            stateField!.SetValue(brain, state);
            clockField!.SetValue(brain, previousClock);
        }

        #endregion

        #region Initialization and State Management

        [TestMethod]
        public void Constructor_InitializesWithDefaultState()
        {
            // --- ARRANGE & ACT ---
            var newBrain = new LogicGateBrain();
            
            // --- ASSERT ---
            Assert.AreEqual(LogicGateType.AND, newBrain.GateType, "Default gate type should be AND.");
            Assert.IsNull(newBrain.FlipFlop, "FlipFlop should be null by default.");
            AreClose(0.5f, newBrain.Threshold, message: "Default threshold should be 0.5f.");
            Assert.AreEqual(1, newBrain.InputMap.Count, "Should start with one default input.");
            Assert.AreEqual(InputSourceType.ActivationPotential, newBrain.InputMap[0].SourceType);
            Assert.AreEqual(1, newBrain.OutputMap.Count, "Should have one output.");
            IsZero(newBrain.OutputMap[0].Value, message: "Initial output value should be 0.");
        }

        [TestMethod]
        public void AddInputAndClearInputs_CorrectlyModifyInputMap()
        {
            // --- ARRANGE ---
            Assert.AreEqual(1, _brain.InputMap.Count);

            // --- ACT 1: Add ---
            _brain.AddInput(InputSourceType.Health, 5);

            // --- ASSERT 1 ---
            Assert.AreEqual(2, _brain.InputMap.Count, "Input map should now have two entries.");
            Assert.AreEqual(InputSourceType.Health, _brain.InputMap[1].SourceType);

            // --- ACT 2: Clear ---
            _brain.ClearInputs();

            // --- ASSERT 2 ---
            Assert.AreEqual(0, _brain.InputMap.Count, "Input map should be empty after clearing.");
        }

        [TestMethod]
        public void Reset_SetsStateAndClockToZero()
        {
            // --- ARRANGE ---
            SetInternalState(_brain, 1.0f, 1.0f); // Set non-default internal state

            // --- ACT ---
            _brain.Reset();

            // --- ASSERT ---
            var stateField = typeof(LogicGateBrain).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            var clockField = typeof(LogicGateBrain).GetField("_previousClock", BindingFlags.NonPublic | BindingFlags.Instance);
            IsZero((float)stateField!.GetValue(_brain)!);
            IsZero((float)clockField!.GetValue(_brain)!);
        }

        #endregion

        #region Combinational Logic Tests

        [DataTestMethod]
        [DataRow(new float[] { 1f, 1f }, 1f, DisplayName = "AND: 1,1 -> 1")]
        [DataRow(new float[] { 1f, 0f }, 0f, DisplayName = "AND: 1,0 -> 0")]
        [DataRow(new float[] { 0.6f, 0.7f }, 1f, DisplayName = "AND: Above Threshold -> 1")]
        [DataRow(new float[] { 0.6f, 0.4f }, 0f, DisplayName = "AND: One Below Threshold -> 0")]
        public void Evaluate_AND_ReturnsCorrectResult(float[] inputs, float expected)
        {
            _brain.GateType = LogicGateType.AND;
            _brain.Evaluate(inputs);
            AreClose(expected, _brain.OutputMap[0].Value);
        }

        [DataTestMethod]
        [DataRow(new float[] { 1f, 0f }, 1f, DisplayName = "OR: 1,0 -> 1")]
        [DataRow(new float[] { 0f, 0f }, 0f, DisplayName = "OR: 0,0 -> 0")]
        public void Evaluate_OR_ReturnsCorrectResult(float[] inputs, float expected)
        {
            _brain.GateType = LogicGateType.OR;
            _brain.Evaluate(inputs);
            AreClose(expected, _brain.OutputMap[0].Value);
        }

        [DataTestMethod]
        [DataRow(new float[] { 1f, 1f }, 0f, DisplayName = "XOR: 1,1 -> 0")]
        [DataRow(new float[] { 1f, 0f, 1f }, 0f, DisplayName = "XOR: 1,0,1 -> 0 (even count)")]
        [DataRow(new float[] { 1f, 0f, 0f }, 1f, DisplayName = "XOR: 1,0,0 -> 1 (odd count)")]
        public void Evaluate_XOR_ReturnsCorrectResult(float[] inputs, float expected)
        {
            _brain.GateType = LogicGateType.XOR;
            _brain.Evaluate(inputs);
            AreClose(expected, _brain.OutputMap[0].Value);
        }

        [DataTestMethod]
        [DataRow(new float[] { 1f }, 0f, DisplayName = "NOT: 1 -> 0")]
        [DataRow(new float[] { 0f }, 1f, DisplayName = "NOT: 0 -> 1")]
        public void Evaluate_NOT_WithSingleInput_ReturnsCorrectResult(float[] inputs, float expected)
        {
            _brain.GateType = LogicGateType.NOT;
            _brain.Evaluate(inputs);
            AreClose(expected, _brain.OutputMap[0].Value);
        }

        #endregion

        #region Flip-Flop (Sequential) Logic Tests

        [TestMethod]
        public void Evaluate_D_FlipFlop_LatchesOnRisingEdgeOnly()
        {
            // --- ARRANGE ---
            _brain.FlipFlop = FlipFlopType.D_FlipFlop;
            _brain.ClearInputs();
            _brain.AddInput(InputSourceType.ConstantOne, 0); // Input 0: Clock
            _brain.AddInput(InputSourceType.ConstantOne, 1); // Input 1: D
            _brain.Reset();

            // --- ACT 1: Initial state ---
            _brain.Evaluate(new float[] { 0f, 1f }); // Clock low, D high
            // --- ASSERT 1 ---
            IsZero(_brain.OutputMap[0].Value, message: "State should not change with clock low.");
            
            // --- ACT 2: Rising Edge ---
            _brain.Evaluate(new float[] { 1f, 1f }); // Clock high, D high
            // --- ASSERT 2 ---
            AreClose(1f, _brain.OutputMap[0].Value, message: "State should latch D's value on rising edge.");
            
            // --- ACT 3: Clock still high ---
            _brain.Evaluate(new float[] { 1f, 0f }); // Clock high, D low
            // --- ASSERT 3 ---
            AreClose(1f, _brain.OutputMap[0].Value, message: "State should not change while clock is high.");
            
            // --- ACT 4: Falling Edge ---
            _brain.Evaluate(new float[] { 0f, 0f }); // Clock low, D low
            // --- ASSERT 4 ---
            AreClose(1f, _brain.OutputMap[0].Value, message: "State should not change on falling edge.");
        }
        
        [TestMethod]
        public void Evaluate_T_FlipFlop_TogglesOnRisingEdge()
        {
            _brain.FlipFlop = FlipFlopType.T_FlipFlop;
            _brain.ClearInputs();
            _brain.AddInput(InputSourceType.ConstantOne, 0); // Input 0: Clock
            _brain.AddInput(InputSourceType.ConstantOne, 1); // Input 1: T (Toggle)
            _brain.Reset();
            
            // Rising edge with T=1, state should toggle from 0 to 1
            _brain.Evaluate(new float[] { 0f, 1f }); // Prime clock low
            _brain.Evaluate(new float[] { 1f, 1f });
            AreClose(1f, _brain.OutputMap[0].Value, message: "State should toggle to 1.");

            // Rising edge with T=0, state should hold
            _brain.Evaluate(new float[] { 0f, 0f }); // Prime clock low
            _brain.Evaluate(new float[] { 1f, 0f });
            AreClose(1f, _brain.OutputMap[0].Value, message: "State should hold at 1.");

            // Rising edge with T=1, state should toggle from 1 to 0
            _brain.Evaluate(new float[] { 0f, 1f }); // Prime clock low
            _brain.Evaluate(new float[] { 1f, 1f });
            IsZero(_brain.OutputMap[0].Value, message: "State should toggle to 0.");
        }
        
        [TestMethod]
        public void Evaluate_JK_FlipFlop_FollowsTruthTableOnRisingEdge()
        {
            _brain.FlipFlop = FlipFlopType.JK_FlipFlop;
            _brain.ClearInputs();
            _brain.AddInput(InputSourceType.ConstantOne, 0); // Input 0: Clock
            _brain.AddInput(InputSourceType.ConstantOne, 1); // Input 1: J
            _brain.AddInput(InputSourceType.ConstantOne, 2); // Input 2: K
            _brain.Reset();

            // Case 1: J=0, K=0 -> Hold state (0)
            _brain.Evaluate(new float[] { 0f, 0f, 0f });
            _brain.Evaluate(new float[] { 1f, 0f, 0f });
            IsZero(_brain.OutputMap[0].Value, message: "JK=00 should hold state 0.");
            
            // Case 2: J=1, K=0 -> Set state to 1
            _brain.Evaluate(new float[] { 0f, 1f, 0f });
            _brain.Evaluate(new float[] { 1f, 1f, 0f });
            AreClose(1f, _brain.OutputMap[0].Value, message: "JK=10 should set state to 1.");
            
            // Case 3: J=0, K=0 -> Hold state (1)
            _brain.Evaluate(new float[] { 0f, 0f, 0f });
            _brain.Evaluate(new float[] { 1f, 0f, 0f });
            AreClose(1f, _brain.OutputMap[0].Value, message: "JK=00 should hold state 1.");
            
            // Case 4: J=0, K=1 -> Reset state to 0
            _brain.Evaluate(new float[] { 0f, 0f, 1f });
            _brain.Evaluate(new float[] { 1f, 0f, 1f });
            IsZero(_brain.OutputMap[0].Value, message: "JK=01 should reset state to 0.");

            // Case 5: J=1, K=1 -> Toggle state from 0 to 1
            _brain.Evaluate(new float[] { 0f, 1f, 1f });
            _brain.Evaluate(new float[] { 1f, 1f, 1f });
            AreClose(1f, _brain.OutputMap[0].Value, message: "JK=11 should toggle state to 1.");
            
            // Case 6: J=1, K=1 -> Toggle state from 1 to 0
            _brain.Evaluate(new float[] { 0f, 1f, 1f });
            _brain.Evaluate(new float[] { 1f, 1f, 1f });
            IsZero(_brain.OutputMap[0].Value, message: "JK=11 should toggle state to 0.");
        }

        #endregion
    }
}
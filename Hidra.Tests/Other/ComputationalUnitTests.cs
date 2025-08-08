// Hidra.Tests/Other/ComputationalUnitTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using Hidra.Core.Logging;
using System.Numerics;

namespace Hidra.Tests.Other
{
    [TestClass]
    public class ComputationalUnitTests : BaseTestClass
    {
        [TestMethod]
        public void HybridSignalCircuit_WhenConfiguredAsSRLatch_FunctionsCorrectly()
        {
            // --- ARRANGE ---
            Logger.Log("TEST_RUNNER", LogLevel.Info, "--- SR LATCH TEST: ARRANGING HYBRID SIGNAL CIRCUIT ---");

            var config = new HidraConfig { DefaultFiringThreshold = 1.0f, DefaultDecayRate = 0.9f };
            var world = new HidraWorld(config, "GN0000000000");

            const ulong SET_INPUT_ID = 1, RESET_INPUT_ID = 2, Q_OUTPUT_ID = 100;
            world.AddInputNode(SET_INPUT_ID);
            world.AddInputNode(RESET_INPUT_ID);
            world.AddOutputNode(Q_OUTPUT_ID);

            var setInput = world.GetInputNodeById(SET_INPUT_ID)!;
            var resetInput = world.GetInputNodeById(RESET_INPUT_ID)!;
            var qOutput = world.GetOutputNodeById(Q_OUTPUT_ID)!;

            var neuronQ = world.AddNeuron(new Vector3(1, 0, 0));
            var neuronNotQ = world.AddNeuron(new Vector3(-1, 0, 0));
            
            // Input signals must be strong enough to override the opposing feedback loop's state.
            const float INPUT_EXCITE = 2.6f;
            const float INPUT_INHIBIT = -2.5f;
            // Feedback signals must be strong enough to maintain state.
            const float FEEDBACK_EXCITE = 1.1f;
            const float FEEDBACK_INHIBIT = -1.5f;

            // CONTROL SIGNALS
            world.AddSynapse(SET_INPUT_ID, neuronQ.Id, SignalType.Immediate, INPUT_EXCITE, 0f);
            world.AddSynapse(SET_INPUT_ID, neuronNotQ.Id, SignalType.Immediate, INPUT_INHIBIT, 0f);
            world.AddSynapse(RESET_INPUT_ID, neuronNotQ.Id, SignalType.Immediate, INPUT_EXCITE, 0f);
            world.AddSynapse(RESET_INPUT_ID, neuronQ.Id, SignalType.Immediate, INPUT_INHIBIT, 0f);

            // CORE LATCH (STATEFUL)
            world.AddSynapse(neuronQ.Id, neuronQ.Id, SignalType.Delayed, FEEDBACK_EXCITE, 0f);
            world.AddSynapse(neuronNotQ.Id, neuronNotQ.Id, SignalType.Delayed, FEEDBACK_EXCITE, 0f);
            world.AddSynapse(neuronQ.Id, neuronNotQ.Id, SignalType.Delayed, FEEDBACK_INHIBIT, 0f);
            world.AddSynapse(neuronNotQ.Id, neuronQ.Id, SignalType.Delayed, FEEDBACK_INHIBIT, 0f);

            // STATE READOUT (CONTINUOUS PROBE)
            // The parameter (0.9f) provides smoothing to the output node.
            world.AddSynapse(neuronQ.Id, Q_OUTPUT_ID, SignalType.Immediate, 1.0f, 0.9f);

            // --- ACT & ASSERT ---
            
            Logger.Log("TEST_RUNNER", LogLevel.Info, "--- SR LATCH TEST: Initializing to LOW state ---");
            resetInput.Value = 1.0f;
            world.Step();
            world.Step();
            resetInput.Value = 0.0f;
            for (int i = 0; i < 5; i++) world.Step();
            
            Assert.IsTrue(qOutput.Value < 0.1f, $"Initial state should be LOW, but was {qOutput.Value}.");

            Logger.Log("TEST_RUNNER", LogLevel.Info, "--- SR LATCH TEST: Setting Latch HIGH ---");
            setInput.Value = 1.0f;
            world.Step();
            world.Step();
            setInput.Value = 0.0f;
            for (int i = 0; i < 5; i++) world.Step();
            
            Assert.IsTrue(qOutput.Value > 0.2f, $"Output should be HIGH after SET, but was {qOutput.Value}.");

            Logger.Log("TEST_RUNNER", LogLevel.Info, "--- SR LATCH TEST: Holding HIGH state ---");
            float heldValue = qOutput.Value;
            for (int i = 0; i < 3; i++) world.Step();
            Assert.IsTrue(qOutput.Value > 0.1f, $"Output should HOLD the HIGH state, but was {qOutput.Value}.");
            Assert.IsTrue(qOutput.Value > heldValue * 0.5f, "Output should not decay too quickly while holding state.");

            Logger.Log("TEST_RUNNER", LogLevel.Info, "--- SR LATCH TEST: Setting Latch LOW ---");
            resetInput.Value = 1.0f;
            world.Step();
            world.Step();
            resetInput.Value = 0.0f;
            for (int i = 0; i < 5; i++) world.Step();
            
            Assert.IsTrue(qOutput.Value < 0.1f, $"Output should be LOW after RESET, but was {qOutput.Value}.");

            Logger.Log("TEST_RUNNER", LogLevel.Info, "--- SR LATCH TEST: Holding LOW state ---");
            for (int i = 0; i < 3; i++) world.Step();
            Assert.IsTrue(qOutput.Value < 0.1f, $"Output should HOLD the LOW state, but was {qOutput.Value}.");
        }
    }
}
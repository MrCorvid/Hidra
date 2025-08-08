// Hidra.Tests/Core/HidraEventSystemTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using System;
using System.Collections.Generic;

namespace Hidra.Tests.Core
{
    /// <summary>
    /// Contains tests for the Hidra eventing system, including the EventQueue's
    /// deterministic processing and the ExecuteGene pipeline.
    /// This replaces the old IntegrationTests file.
    /// </summary>
    [TestClass]
    public class HidraEventSystemTests : BaseTestClass
    {
        #region EventQueue Tests

        /// <summary>
        /// Verifies that the EventQueue processes events scheduled for the same tick
        /// in a deterministic order based on their unique, incrementing ID.
        /// </summary>
        [TestMethod]
        public void EventQueue_ProcessDueEvents_ProcessesInDeterministicOrder()
        {
            // Arrange
            var queue = new EventQueue();
            var processedOrder = new List<ulong>();
            Action<Event> processAction = e => processedOrder.Add(e.Id);

            // Push events for the same tick but in a random ID order.
            queue.Push(new Event { Id = 3, ExecutionTick = 1, Type = EventType.Activate });
            queue.Push(new Event { Id = 1, ExecutionTick = 1, Type = EventType.Activate });
            queue.Push(new Event { Id = 2, ExecutionTick = 1, Type = EventType.Activate });
            queue.Push(new Event { Id = 4, ExecutionTick = 2, Type = EventType.Activate }); // Should not be processed yet.

            // Act
            queue.ProcessDueEvents(currentTick: 1, processAction);

            // Assert
            Assert.AreEqual(3, processedOrder.Count, "Should only process 3 events due on tick 1.");
            Assert.AreEqual(1ul, processedOrder[0], "First event processed should be the one with the lowest ID.");
            Assert.AreEqual(2ul, processedOrder[1], "Second event processed should have the second lowest ID.");
            Assert.AreEqual(3ul, processedOrder[2], "Third event processed should have the third lowest ID.");
        }

        #endregion

        #region Gene Execution Pipeline Tests

        /// <summary>
        /// Verifies the full, end-to-end event pipeline:
        /// 1. A neuron fires, queueing an 'Activate' event.
        /// 2. The 'Activate' event is processed, the brain is evaluated, and an 'ExecuteGene' event is queued.
        /// 3. The 'ExecuteGene' event is processed, running the HGL script.
        /// </summary>
        [TestMethod]
        public void FullCycle_NeuronActivationToGeneExecution_FollowsCorrectEventSequence()
        {
            // --- ARRANGE ---

            // 1. Define HGL bytecode for a gene that calls StoreLVar(10, 42).
            // The HGL interpreter requires an explicit POP to finalize an expression
            // (even a void function call) into an executable statement.
            string gene4Hgl = string.Concat(
                // PUSH the LVar index (10) first.
                HGLOpcodes.PUSH_BYTE.ToString("X2"),
                10.ToString("X2"),
                // PUSH the value (42) second.
                HGLOpcodes.PUSH_BYTE.ToString("X2"),
                42.ToString("X2"),
                // Call the API function. This places the function call expression on the stack.
                HGLOpcodes.StoreLVar.ToString("X2"),
                // POP the result to finalize the expression and execute it as a statement.
                HGLOpcodes.POP.ToString("X2")
            );

            // 2. Construct the genome string. Gene IDs are implicit and positional.
            string nopBytecode = HGLOpcodes.NOP.ToString("X2"); // "00"

            string testGenome = "GN" + nopBytecode +   // Gene ID 1
                                  "GN" + nopBytecode +   // Gene ID 2
                                  "GN" + nopBytecode +   // Gene ID 3
                                  "GN" + gene4Hgl;       // Gene ID 4

            // 3. Create the world and get the neuron.
            var config = new HidraConfig { DefaultFiringThreshold = 1.0f, DefaultDecayRate = 1.0f };
            var world = new HidraWorld(config, testGenome);
            var neuron = world.Neurons.Values.First();
            neuron.LocalVariables[10] = 0f; // Ensure target LVar is initially 0.

            // 4. Configure a mock brain to trigger gene 4 execution.
            var mockBrain = new MockBrain
            {
                OutputMap = new List<BrainOutput>
                {
                    new BrainOutput { ActionType = OutputActionType.ExecuteGene, Value = 4.0f }
                }
            };
            neuron.Brain = mockBrain;

            // --- ACT & ASSERT ---
            
            // Set potential to trigger firing.
            neuron.LocalVariables[(int)LVarIndex.SomaPotential] = 1.5f;

            // TICK 1: Neuron fires.
            // - Potential (1.5) > Threshold (1.0).
            // - 'Activate' event is queued for Tick 2.
            world.Step();
            Assert.AreEqual(0f, neuron.LocalVariables[10], "LVar should not be modified on the firing tick.");
            Assert.AreEqual(0, mockBrain.EvaluateCallCount, "Brain should not be evaluated on firing tick.");

            // TICK 2: Brain evaluation.
            // - 'Activate' event is processed.
            // - 'ExecuteGene' event for gene 4 is queued for Tick 3.
            world.Step();
            Assert.AreEqual(0f, neuron.LocalVariables[10], "LVar should not be modified on brain evaluation tick.");
            Assert.AreEqual(1, mockBrain.EvaluateCallCount, "Brain should be evaluated on the activation tick.");

            // TICK 3: Gene execution.
            // - 'ExecuteGene' event is processed, running the HGL script.
            // - LVar[10] should now be 42.
            world.Step();
            Assert.AreEqual(42.0f, neuron.LocalVariables[10], "LVar should be set to the value from the executed gene.");
        }

        #endregion
    }
}
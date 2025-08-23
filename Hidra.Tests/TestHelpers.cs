// Hidra.Tests/TestHelpers.cs
// Shared scaffolding for all Hidra unit tests.
// Provide deterministic setup, teardown, helper mocks, and fluent helpers.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using Hidra.Core.Brain;
using Hidra.Core.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Hidra.Tests
{
    /// <summary>
    /// Centralized defaults & constants for tests.
    /// </summary>
    public static class TestDefaults
    {
        /// <summary>
        /// Minimal, valid genome for booting a world with required system genes only.
        /// </summary>
        public const string MinimalGenome = "GNGNGNGN";

        /// <summary>
        /// Deterministic baseline config: no decay, no metabolic tax.
        /// Override fields explicitly in tests that need other behavior.
        /// </summary>
        public static HidraConfig DeterministicConfig() => new HidraConfig
        {
            // Determinism-first: make “nothing happens” the baseline unless a test opts in.
            DefaultDecayRate = 1.0f,
            MetabolicTaxPerTick = 0.0f,
            DefaultFiringThreshold = 1.0f,
            DefaultRefractoryPeriod = 1.0f,
            MetricsEnabled = false // Disable by default to speed up tests
        };
    }

    /// <summary>
    /// Minimal, test-side mirror for neuron local variable indices.
    /// Keep it in sync with the production enum/order.
    /// </summary>
    public enum LVarIndex
    {
        FiringThreshold = 0, 
        DecayRate = 1,
        RefractoryPeriod = 2, 
        ThresholdAdaptationFactor = 3, 
        ThresholdRecoveryRate = 4,

        RefractoryTimeLeft = 239, 
        FiringRate = 240, 
        DendriticPotential = 241,
        SomaPotential = 242, 
        Health = 243, 
        Age = 244, 
        AdaptiveThreshold = 245
    }

    #region Test Helper Mocks

    /// <summary>
    /// Dumb condition mock for gating synapses without bringing in external frameworks.
    /// </summary>
    internal sealed class MockCondition : ICondition
    {
        public bool ConditionResult { get; set; } = true;
        public bool Evaluate(ConditionContext context) => ConditionResult;
    }

    /// <summary>
    /// Dumb brain mock to assert exactly when/if Evaluate is called and what inputs were seen.
    /// </summary>
    internal sealed class MockBrain : IBrain
    {
        public IReadOnlyList<BrainInput> InputMap { get; set; } = Array.Empty<BrainInput>();
        public IReadOnlyList<BrainOutput> OutputMap { get; set; } = Array.Empty<BrainOutput>();
        public bool CanLearn => false;

        public int EvaluateCallCount { get; private set; }
        public float[]? LastInputs { get; private set; }
        
        public void Evaluate(float[] inputs, Action<string, LogLevel, string>? logAction = null)
        {
            EvaluateCallCount++;
            LastInputs = inputs;
        }

        public void Reset()
        {
            EvaluateCallCount = 0;
            LastInputs = null;
        }

        public void Mutate(float rate) { /* no-op for tests */ }
        
        public void SetPrng(IPrng prng) { /* no-op for mock */ }

        public void InitializeFromLoad() { /* no-op for mock */ }
    }

    #endregion

    /// <summary>
    /// Shared base for all test classes. Handles logging, world lifecycle, and convenience helpers.
    /// </summary>
    public abstract class BaseTestClass
    {
        private readonly Dictionary<HidraWorld, List<string>> _worldLogs = new();
        protected readonly List<HidraWorld> _ownedWorlds = new();
        
        public TestContext TestContext { get; set; } = null!;

        [TestInitialize]
        public virtual void BaseInit()
        {
            Logger.Init(); 
            Logger.Log("TEST_RUNNER", LogLevel.Info, $"--- BEGIN {TestContext.TestName} ---");
        }

        [TestCleanup]
        public virtual void BaseCleanup()
        {
            if (TestContext.CurrentTestOutcome == UnitTestOutcome.Failed)
            {
                DumpCoreLogsOnFailure();
            }
            
            _ownedWorlds.Clear();
            _worldLogs.Clear();
            Logger.Log("TEST_RUNNER", LogLevel.Info, $"--- END {TestContext.TestName} ---");
        }

        #region World Builders

        protected HidraWorld CreateWorld() =>
            Track(new HidraWorld(TestDefaults.DeterministicConfig(), TestDefaults.MinimalGenome));

        protected HidraWorld CreateWorld(HidraConfig cfg) =>
            Track(new HidraWorld(cfg, TestDefaults.MinimalGenome));

        protected HidraWorld CreateWorld(HidraConfig cfg, string genome) =>
            Track(new HidraWorld(cfg, genome));

        private HidraWorld Track(HidraWorld w)
        {
            _ownedWorlds.Add(w);

            var logs = new List<string>();
            _worldLogs[w] = logs;
            w.SetLogAction((tag, level, message) =>
            {
                logs.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] [{level,-5}] [{tag}] {message}");
            });

            return w;
        }

        #endregion

        #region Tick & Timing Helpers

        protected static void StepNTicks(HidraWorld world, int n)
        {
            for (int i = 0; i < n; i++) world.Step();
        }

        protected static ulong Step(HidraWorld world)
        {
            world.Step();
            return world.CurrentTick;
        }

        #endregion

        #region Node & Synapse Helpers

        protected (InputNode input, Neuron target) WireImmediateInputToFirstNeuron(HidraWorld world, ulong inputId, float weight)
        {
            world.AddInputNode(inputId);
            var input = world.GetInputNodeById(inputId)!;
            var neuron = world.Neurons.First().Value;

            world.AddSynapse(inputId, neuron.Id, SignalType.Immediate, weight, parameter: 0f);
            return (input, neuron);
        }

        protected (InputNode input, Neuron target, Synapse synapse) WireConditionalImmediate(
            HidraWorld world, ulong inputId, float weight, bool initialCondition = true)
        {
            world.AddInputNode(inputId);
            var input = world.GetInputNodeById(inputId)!;
            var neuron = world.Neurons.First().Value;

            var syn = world.AddSynapse(inputId, neuron.Id, SignalType.Immediate, weight, parameter: 0f)!;
            var cond = new MockCondition { ConditionResult = initialCondition };
            syn.Condition = cond;
            return (input, neuron, syn);
        }

        protected static void Pulse(InputNode input, float value) => input.Value = value;

        #endregion

        #region Neuron Helpers

        protected static Neuron FirstNeuron(HidraWorld world) => world.Neurons.First().Value;

        protected static void SetLVar(Neuron n, LVarIndex idx, float value) =>
            n.LocalVariables[(int)idx] = value;


        protected static float GetLVar(Neuron n, LVarIndex idx) =>
            n.LocalVariables[(int)idx];

        #endregion

        #region Assert Helpers

        protected static void AreClose(float expected, float actual, float tol = 1e-6f, string? message = null)
        {
            if (float.IsNaN(expected) || float.IsNaN(actual))
                Assert.Fail(message ?? $"NaN compare: expected={expected}, actual={actual}");
            Assert.IsTrue(Math.Abs(expected - actual) <= tol,
                message ?? $"Expected {expected} ± {tol} but was {actual}");
        }

        protected static void AreClose(ReadOnlySpan<float> expected, ReadOnlySpan<float> actual, float tol = 1e-6f)
        {
            Assert.AreEqual(expected.Length, actual.Length, "Length mismatch.");
            for (int i = 0; i < expected.Length; i++)
                AreClose(expected[i], actual[i], tol, $"Mismatch at index {i}");
        }

        protected static void IsZero(float actual, float tol = 1e-6f, string? message = null) =>
            AreClose(0f, actual, tol, message);

        protected static void DidNotChange(float before, float after, float tol = 1e-6f, string? what = null) =>
            AreClose(before, after, tol, what ?? "Value unexpectedly changed");

        #endregion

        #region Concurrency Helpers

        protected static async Task RunConcurrent(int tasks, Func<Task> body)
        {
            var list = Enumerable.Range(0, tasks).Select(_ => Task.Run(body));
            await Task.WhenAll(list);
        }

        #endregion
        
        private void DumpCoreLogsOnFailure()
        {
            if (_ownedWorlds.Count == 0)
            {
                TestContext.WriteLine("\n\n--- FAILED TEST LOG DUMP: SKIPPED (No HidraWorld instances were created) ---\n");
                return;
            }

            TestContext.WriteLine($"\n\n--- FAILED TEST LOG DUMP: {TestContext.TestName} ---");

            foreach (var world in _ownedWorlds)
            {
                TestContext.WriteLine($"--- Logs for World (Experiment ID: '{world.ExperimentId}') ---");
                if (_worldLogs.TryGetValue(world, out var logs) && logs.Count > 0)
                {
                    foreach (var log in logs)
                    {
                        TestContext.WriteLine(log);
                    }
                }
                else
                {
                    TestContext.WriteLine("[No logs were captured for this world instance]");
                }
            }
            
            TestContext.WriteLine("--- END OF LOG DUMP ---\n");
        }
    }
}
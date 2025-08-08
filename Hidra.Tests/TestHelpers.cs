// Hidra.Tests/TestHelpers.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core.Logging;
using Hidra.Core;
using System.Diagnostics.CodeAnalysis;

namespace Hidra.Tests
{
    /// <summary>
    /// Defines a test-side mirror of the engine's internal `LVarIndex` enum.
    /// This provides strong typing and improved readability for tests that need to
    /// interact with a neuron's local variable array, preventing the use of "magic numbers".
    /// </summary>
    internal enum LVarIndex
    {
        /// <summary>The potential at which a neuron will fire.</summary>
        FiringThreshold = 0,
        /// <summary>The multiplier (e.g., 0.99) applied to soma potential each tick.</summary>
        DecayRate = 1,
        // Note: Indices 2-238 are available for general-purpose use by genes.
        /// <summary>The number of ticks remaining in the neuron's refractory period.</summary>
        RefractoryTimeLeft = 239,
        /// <summary>A moving average of the neuron's firing rate.</summary>
        FiringRate = 240,
        /// <summary>Potential driven by continuous, graded synapses. Reset each tick.</summary>
        DendriticPotential = 241,
        /// <summary>Potential accumulated from discrete, pulsed synapses. Decays over time.</summary>
        SomaPotential = 242,
        /// <summary>A measure of the neuron's viability, affecting its functions.</summary>
        Health = 243,
        /// <summary>The neuron's age in simulation ticks.</summary>
        Age = 244,
        /// <summary>The adaptive component added to the firing threshold after firing.</summary>
        AdaptiveThreshold = 245
    }

    /// <summary>
    /// Provides a foundational abstract class for all test classes within the project.
    /// It standardizes test lifecycle logging, ensuring a clear and consistent
    /// record of test execution start, end, and outcome in the test runner's output.
    /// </summary>
    [TestClass]
    public abstract class BaseTestClass
    {
        /// <summary>
        /// Gets or sets the TestContext, which is injected by the MSTest framework.
        /// It provides information about and functionality for the current test run.
        /// </summary>
        [DisallowNull]
        public TestContext TestContext { get; set; } = null!;

        /// <summary>
        /// Establishes a consistent entry point for each test by logging its start.
        /// This method is automatically invoked by the test framework before each test method execution.
        /// </summary>
        [TestInitialize]
        public void BaseTestInitialize()
        {
            Logger.Init();
            // The TestName is guaranteed non-null within the context of a running test.
            Logger.Log("TEST_RUNNER", LogLevel.Info, $"--- TEST START: {TestContext.TestName!} ---");
        }

        /// <summary>
        /// Ensures every test's completion and outcome are logged, providing a clear
        /// pass/fail record in the test output. This method is automatically invoked
        /// by the test framework after each test method execution.
        /// </summary>
        [TestCleanup]
        public void BaseTestCleanup()
        {
            string testName = TestContext.TestName!;
            string outcome = TestContext.CurrentTestOutcome.ToString().ToUpper();
            
            // Determine the appropriate log level based on the test result.
            LogLevel level = TestContext.CurrentTestOutcome switch
            {
                UnitTestOutcome.Passed    => LogLevel.Info,
                UnitTestOutcome.Failed    => LogLevel.Error,
                _                         => LogLevel.Warning // Covers Inconclusive, Skipped, etc.
            };

            Logger.Log("TEST_RUNNER", level, $"--- TEST END: {testName} | Outcome: {outcome} ---");

            HidraWorld.Shutdown();
        }
    }
}
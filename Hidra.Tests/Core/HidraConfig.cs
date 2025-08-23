// Hidra.Tests/Core/HidraConfigTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using System.Linq;

namespace Hidra.Tests.Core
{
    [TestClass]
    public class HidraConfigTests : BaseTestClass
    {
        // No special initialization is needed for this simple data class.

        #region Core Tests

        [TestMethod]
        public void Constructor_SetsAllPropertiesToCorrectDefaultValues()
        {
            // --- ARRANGE & ACT ---
            // Create a new config instance to inspect its default state.
            var config = new HidraConfig();

            // --- ASSERT ---
            // This test exhaustively checks every property's default value to ensure
            // the baseline simulation configuration is stable and predictable.

            // Core Simulation Parameters
            AreClose(0.01f, config.MetabolicTaxPerTick);
            AreClose(100.0f, config.InitialNeuronHealth);
            IsZero(config.InitialPotential);
            AreClose(0.1f, config.DefaultDecayRate);
            AreClose(1.0f, config.DefaultFiringThreshold);

            // Oscillation Control
            AreClose(5.0f, config.DefaultRefractoryPeriod);
            AreClose(0.1f, config.DefaultThresholdAdaptationFactor);
            AreClose(0.05f, config.DefaultThresholdRecoveryRate);
            AreClose(0.95f, config.FiringRateMAWeight);

            // World & Genome Parameters
            AreClose(5.0f, config.CompetitionRadius);
            AreClose(0.5f, config.CrowdingFactor);
            Assert.AreEqual(4u, config.SystemGeneCount);

            // Determinism & Seed Configuration
            Assert.IsTrue(config.Deterministic);
            Assert.AreEqual(0x12345678UL, config.Seed0);
            Assert.AreEqual(0x9ABCDEF0UL, config.Seed1);
            Assert.IsFalse(config.AutoReseedPerRun);

            // Metrics Configuration
            Assert.IsTrue(config.MetricsEnabled);
            Assert.AreEqual(1, config.MetricsCollectionInterval);
            Assert.AreEqual(2048, config.MetricsRingCapacity);
            AreClose(1.0f, config.MetricsNeuronSampleRate);
            Assert.IsFalse(config.MetricsIncludeSynapses);
            Assert.IsTrue(config.MetricsIncludeIO);
            
            // Special check for the default LVar indices array
            Assert.IsNotNull(config.MetricsLVarIndices);
            var expectedIndices = new[] { 240, 241, 242, 243, 244, 245 };
            CollectionAssert.AreEqual(expectedIndices, config.MetricsLVarIndices, "Default MetricsLVarIndices do not match.");
        }

        [TestMethod]
        public void Properties_CanBeSetAndRetrieved()
        {
            // --- ARRANGE ---
            // This test acts as a simple "smoke test" to ensure the properties are mutable.
            var config = new HidraConfig();
            var newIndices = new[] { 1, 2, 3 };

            // --- ACT ---
            config.MetabolicTaxPerTick = 0.5f;
            config.Deterministic = false;
            config.SystemGeneCount = 8;
            config.MetricsLVarIndices = newIndices;
            config.CompetitionRadius = 99.9f;

            // --- ASSERT ---
            AreClose(0.5f, config.MetabolicTaxPerTick);
            Assert.IsFalse(config.Deterministic);
            Assert.AreEqual(8u, config.SystemGeneCount);
            Assert.AreSame(newIndices, config.MetricsLVarIndices);
            AreClose(99.9f, config.CompetitionRadius);
        }

        #endregion
    }
}
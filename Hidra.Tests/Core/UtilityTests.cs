// Hidra.Tests/Core/UtilityTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using System;

namespace Hidra.Tests.Core
{
    [TestClass]
    public class UtilityTests : BaseTestClass
    {
        #region KahanAccumulator Tests

        [TestMethod]
        public void KahanAccumulator_Add_WithSimpleValues_ProducesCorrectSum()
        {
            // --- ARRANGE ---
            var accumulator = new KahanAccumulator();

            // --- ACT ---
            accumulator.Add(10.5f);
            accumulator.Add(20.0f);
            accumulator.Add(-5.5f);

            // --- ASSERT ---
            AreClose(25.0f, accumulator.Sum);
        }

        [TestMethod]
        public void KahanAccumulator_Add_WithSmallValuesToLargeSum_MaintainsPrecision()
        {
            // --- ARRANGE ---
            var kahan = new KahanAccumulator();
            float naiveSum = 1e7f;
            float smallValue = 0.1f;

            // --- ACT ---
            kahan.Add(1e7f);
            for (int i = 0; i < 10; i++)
            {
                kahan.Add(smallValue);
                naiveSum += smallValue; // This summation will lose precision
            }

            // --- ASSERT ---
            AreClose(10000001.0f, kahan.Sum);
            Assert.AreNotEqual(kahan.Sum, naiveSum, "Naive sum should have lost precision compared to Kahan sum.");
        }

        #endregion

        #region XorShift128PlusPrng Tests

        [TestMethod]
        public void XorShift128PlusPrng_Constructor_WithZeroSeeds_UsesDefaultFallbackValues()
        {
            // --- ARRANGE ---
            var rng1 = new XorShift128PlusPrng(0, 0);
            rng1.GetState(out ulong s0, out ulong s1);
            
            // --- ASSERT ---
            Assert.AreNotEqual(0UL, s0, "Seed 0 should have a fallback value if provided as zero.");
            Assert.AreNotEqual(0UL, s1, "Seed 1 should have a fallback value if provided as zero.");
        }

        [TestMethod]
        public void XorShift128PlusPrng_NextMethods_ProduceDeterministicSequence()
        {
            // --- ARRANGE ---
            var rng1 = new XorShift128PlusPrng(123, 456);
            var rng2 = new XorShift128PlusPrng(123, 456);

            // --- ACT & ASSERT ---
            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(rng1.NextULong(), rng2.NextULong());
                AreClose(rng1.NextFloat(), rng2.NextFloat());
                Assert.AreEqual(rng1.NextInt(0, 1000), rng2.NextInt(0, 1000));
            }
        }

        [TestMethod]
        public void XorShift128PlusPrng_GetAndSetState_AllowsResumingSequence()
        {
            // --- ARRANGE ---
            var rng1 = new XorShift128PlusPrng(777, 888);
            for (int i = 0; i < 10; i++) { rng1.NextULong(); }
            
            // --- ACT 1: Save state ---
            rng1.GetState(out ulong s0, out ulong s1);
            var nextValAfterSave = rng1.NextULong();

            // --- ACT 2: Restore state in new RNG ---
            var rng2 = new XorShift128PlusPrng(1, 1); // Different initial seed
            rng2.SetState(s0, s1);
            var nextValAfterRestore = rng2.NextULong();

            // --- ASSERT ---
            Assert.AreEqual(nextValAfterSave, nextValAfterRestore, "RNG should produce the same value after state is restored.");
        }

        #endregion
    }
}
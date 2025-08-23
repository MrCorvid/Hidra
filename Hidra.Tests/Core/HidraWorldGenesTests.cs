// Hidra.Tests/Core/HidraWorldGenesTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using System.Reflection;
using System.Collections.Generic;
// FIX: The real types are available through project references, so stubs are not needed.
// We just need the using directive for the real library namespace.
using ProgrammingLanguageNr1;

namespace Hidra.Tests.Core
{
    [TestClass]
    public class HidraWorldGenesTests : BaseTestClass
    {
        private HidraConfig _config = null!;
        private HidraWorld _world = null!;

        // Constants matching those in HidraWorld for testing the private method.
        private const uint SYS_GENE_GENESIS = 0;
        private const uint SYS_GENE_GESTATION = 1;
        private const uint SYS_GENE_MITOSIS = 2;
        private const uint SYS_GENE_APOPTOSIS = 3;

        [TestInitialize]
        public override void BaseInit()
        {
            base.BaseInit();
            _config = TestDefaults.DeterministicConfig();
            // This creates a world with a valid, compiled Genesis gene (ID 0)
            _world = CreateWorld(_config, TestDefaults.MinimalGenome);
        }

        #region GetInitialContextForGene Tests

        private Hidra.Core.ExecutionContext InvokeGetInitialContextForGene(uint geneId)
        {
            var methodInfo = typeof(HidraWorld).GetMethod("GetInitialContextForGene", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(methodInfo, "Private method 'GetInitialContextForGene' not found.");
            return (Hidra.Core.ExecutionContext)methodInfo.Invoke(_world, new object[] { geneId })!;
        }

        [TestMethod]
        public void GetInitialContextForGene_WithSystemGeneId_ReturnsSystemContext()
        {
            // --- ARRANGE ---
            var context = InvokeGetInitialContextForGene(SYS_GENE_GENESIS);

            // --- ASSERT ---
            Assert.AreEqual(Hidra.Core.ExecutionContext.System, context, "Gene 0 (Genesis) should have System context.");
        }

        [TestMethod]
        public void GetInitialContextForGene_WithProtectedGeneIds_ReturnsProtectedContext()
        {
            // --- ARRANGE ---
            var protectedGeneIds = new[] { SYS_GENE_GESTATION, SYS_GENE_MITOSIS, SYS_GENE_APOPTOSIS };

            // --- ACT & ASSERT ---
            foreach (var geneId in protectedGeneIds)
            {
                var context = InvokeGetInitialContextForGene(geneId);
                Assert.AreEqual(Hidra.Core.ExecutionContext.Protected, context, $"Gene {geneId} should have Protected context.");
            }
        }

        [TestMethod]
        public void GetInitialContextForGene_WithGeneralGeneId_ReturnsGeneralContext()
        {
            // --- ARRANGE ---
            const uint generalGeneId = 100;
            var context = InvokeGetInitialContextForGene(generalGeneId);

            // --- ASSERT ---
            Assert.AreEqual(Hidra.Core.ExecutionContext.General, context, "A non-system, non-protected gene should have General context.");
        }

        #endregion

        #region ExecuteGene Tests

        [TestMethod]
        public void ExecuteGene_WhenGeneDoesNotExist_ReturnsGracefullyWithoutAction()
        {
            // --- ARRANGE ---
            const uint nonExistentGeneId = 999;
            // The world starts with no neurons. Add one to serve as the execution context.
            _world.AddNeuron(System.Numerics.Vector3.Zero);
            var neuron = FirstNeuron(_world);
            
            // --- ACT & ASSERT ---
            try
            {
                // The primary assertion is that this call does not throw an exception.
                _world.ExecuteGene(nonExistentGeneId, neuron, Hidra.Core.ExecutionContext.General);
            }
            catch (System.Exception ex)
            {
                Assert.Fail($"Expected no exception, but got: {ex}. ExecuteGene should handle non-existent genes gracefully.");
            }
        }
        
        [TestMethod]
        public void ExecuteGene_WithValidGene_ExecutesWithoutCrashing()
        {
            // --- ARRANGE ---
            // The minimal genome contains a valid Genesis gene (ID 0).
            // The world starts with no neurons. Add one to serve as the execution context.
            _world.AddNeuron(System.Numerics.Vector3.Zero);
            var neuron = FirstNeuron(_world);

            // --- ACT & ASSERT ---
            try
            {
                // The test validates that the entire interpreter pipeline can be invoked without error.
                _world.ExecuteGene(SYS_GENE_GENESIS, neuron, Hidra.Core.ExecutionContext.System);
            }
            catch(System.Exception ex)
            {
                Assert.Fail($"Executing a valid gene should not throw an exception. Got: {ex}");
            }
        }

        [TestMethod]
        public void ExecuteGene_WithNullNeuronForSystemGene_ExecutesWithoutCrashing()
        {
            // --- ARRANGE ---
            // System-level genes like Genesis are allowed to be called with a null 'self' context.
            
            // --- ACT & ASSERT ---
            try
            {
                _world.ExecuteGene(SYS_GENE_GENESIS, null, Hidra.Core.ExecutionContext.System);
            }
            catch (System.Exception ex)
            {
                Assert.Fail($"Executing a system gene with a null neuron context should be allowed. Got: {ex}");
            }
        }

        #endregion
    }
}
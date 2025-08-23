// Hidra.Tests/Api/Genome/SynapseBridgeApiTests.cs

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Tests.Api.TestHelpers;

namespace Hidra.Tests.Api
{
    [TestClass]
    public class SynapseBridgeApiTests : BaseApiTestClass
    {
        /// <summary>
        /// Creates three neurons (ids 1, 2, 3) at (0,0,0), sets source to neuron 2,
        /// adds a synapse 2->3 with (signal=0, weight=1, param=0), stores synId in LVar[100] of neuron 2,
        /// then verifies via the API.
        /// </summary>
        [TestMethod]
        public async Task GeneExecution_AddSynapse_FromNeuronToNeuron()
        {
            string hgl = @"
        # Create neuron id=1 at (0,0,0)
        PUSH_CONST 0 0 0
        CreateNeuron

        # Create neuron id=2 at (0,0,0)
        PUSH_CONST 0 0 0
        CreateNeuron

        # Create neuron id=3 at (0,0,0)
        PUSH_CONST 0 0 0
        CreateNeuron

        # Set source/system target to neuron 2
        PUSH_CONST 2
        SetSystemTarget

        # Prepare to store the synapse id into LVar[100] of neuron 2
        PUSH_CONST 100

        # Add synapse: targetType=0 (Neuron), targetId=3, signal=0, weight=1, parameter=0
        PUSH_CONST 0 3 0 1 0
        AddSynapse

        # Store synId into LVar[100] on current target neuron (2)
        StoreLVar

        GN
        ";
            var hex = await AssembleGenomeAsync(hgl);

            var req = new Hidra.API.DTOs.CreateExperimentRequestDto
            {
                Name = "add_syn_2_to_3_with_lvar",
                HGLGenome = hex,
                Config = Hidra.Tests.Api.TestDefaults.GetDeterministicConfig(),
                IOConfig = new Hidra.API.DTOs.IOConfigDto()
            };

            var expId = await CreateExperimentAsync(req);
            await Client.PostAsync($"/api/experiments/{expId}/step", null);

            try
            {
                // Validate synapse fields
                var synResp = await Client.GetAsync($"/api/experiments/{expId}/query/synapses/1");
                synResp.EnsureSuccessStatusCode();
                var syn = await synResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(JsonOpts);

                Assert.AreEqual(1ul, syn.GetProperty("id").GetUInt64(), "First synapse should have id=1.");
                Assert.AreEqual(2ul, syn.GetProperty("sourceId").GetUInt64(), "Synapse source should be neuron 2.");
                Assert.AreEqual(3ul, syn.GetProperty("targetId").GetUInt64(), "Synapse target should be neuron 3.");

                // Validate LVar capture on neuron 2
                var neuron2Resp = await Client.GetAsync($"/api/experiments/{expId}/query/neurons/2");
                neuron2Resp.EnsureSuccessStatusCode();
                var neuron2 = await neuron2Resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(JsonOpts);

                // FIX: LocalVariables is a JSON array and must be accessed by index.
                var lvarsArray = neuron2.GetProperty("localVariables");
                var reportedId = (ulong)lvarsArray[100].GetSingle();

                Assert.AreEqual(1ul, reportedId, "LVar[100] on neuron 2 should contain the created synapse id (1).");
            }
            catch
            {
                await DumpWorldAsync(nameof(GeneExecution_AddSynapse_FromNeuronToNeuron), expId);
                throw;
            }
        }

        /// <summary>
        /// Creates one neuron (id 1), adds an autapse 1->1, stores synId in LVar[100] of neuron 1,
        /// and verifies the synapse + LVar via the API.
        /// </summary>
        [TestMethod]
        public async Task GeneExecution_AddAutapse_ToSelf()
        {
            string hgl = @"
# Create neuron id=1 at (0,0,0) — system target becomes 1
PUSH_CONST 0 0 0
CreateNeuron

# Prepare to store the synapse id into LVar[100] of neuron 1
PUSH_CONST 100

# Add autapse: targetType=0 (Neuron), targetId=0 (self), signal=0, weight=1, parameter=0
PUSH_CONST 0 0 0 1 0
AddSynapse

# Store synId into LVar[100] on neuron 1 (current target)
StoreLVar

GN
";
            var hex = await AssembleGenomeAsync(hgl);

            var req = new Hidra.API.DTOs.CreateExperimentRequestDto
            {
                Name = "add_autapse_1_to_1_with_lvar",
                HGLGenome = hex,
                Config = Hidra.Tests.Api.TestDefaults.GetDeterministicConfig(),
                IOConfig = new Hidra.API.DTOs.IOConfigDto()
            };

            var expId = await CreateExperimentAsync(req);
            await Client.PostAsync($"/api/experiments/{expId}/step", null);

            try
            {
                var synResp = await Client.GetAsync($"/api/experiments/{expId}/query/synapses/1");
                synResp.EnsureSuccessStatusCode();
                var syn = await synResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);

                Assert.AreEqual(1ul, syn.GetProperty("id").GetUInt64(), "First synapse should have id=1.");
                Assert.AreEqual(1ul, syn.GetProperty("sourceId").GetUInt64(), "Autapse source should be neuron 1.");
                Assert.AreEqual(1ul, syn.GetProperty("targetId").GetUInt64(), "Autapse target should be neuron 1.");

                var neuron1Resp = await Client.GetAsync($"/api/experiments/{expId}/query/neurons/1");
                neuron1Resp.EnsureSuccessStatusCode();
                var neuron1 = await neuron1Resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);

                // FIX: LocalVariables is a JSON array and must be accessed by index.
                var lvarsArray = neuron1.GetProperty("localVariables");
                var reportedId = (ulong)lvarsArray[100].GetSingle();
                
                Assert.AreEqual(1ul, reportedId, "LVar[100] on neuron 1 should contain the created synapse id (1).");
            }
            catch
            {
                await DumpWorldAsync(nameof(GeneExecution_AddAutapse_ToSelf), expId);
                throw;
            }
        }

        /// <summary>
        /// Verifies that a gene can create a synapse, store its ID in a neuron's local variable,
        /// and subsequently modify that synapse's weight property using its local index.
        /// This test consolidates the functionality previously covered by two redundant tests.
        /// </summary>
        [TestMethod]
        public async Task Gene_CanCreateAndModifySynapse_AndStoreIdInLVar()
        {
            // --- ARRANGE ---
            
            // Define the HGL script for the test case
            string hgl = @"
                # Create neurons 1 and 2
                PUSH_CONST 0 0 0
                CreateNeuron
                PUSH_CONST 0 0 0
                CreateNeuron

                # Set the 'system target' for subsequent operations to neuron 1
                PUSH_CONST 1
                SetSystemTarget

                # Push the LVar index where the synapse ID will be stored
                PUSH_CONST 100

                # Add a synapse from neuron 1 (the system target) to neuron 2
                PUSH_CONST 0 2 0 1 0
                AddSynapse

                # Store the returned synapse ID (from AddSynapse) into LVar[100] of neuron 1
                StoreLVar

                # Modify the weight (property 0) of the first synapse created by this neuron (index 0) to 123
                PUSH_CONST 0 0 123
                SetSynapseSimpleProperty
                GN";
                
            // Assemble the HGL into bytecode
            var hex = await AssembleGenomeAsync(hgl);

            // Create the experiment request using your existing patterns
            var req = new Hidra.API.DTOs.CreateExperimentRequestDto
            {
                Name = "gene_create_modify_store_synapse",
                HGLGenome = hex,
                Config = Hidra.Tests.Api.TestDefaults.GetDeterministicConfig(),
                IOConfig = new Hidra.API.DTOs.IOConfigDto()
            };
            
            // Create the experiment and get its ID
            var expId = await CreateExperimentAsync(req);

            // --- ACT ---

            // Advance the simulation by one tick to execute the gene
            await Client.PostAsync($"/api/experiments/{expId}/step", null);

            // --- ASSERT ---
            
            try
            {
                // 1. Verify the synapse was modified correctly
                var synResp = await Client.GetAsync($"/api/experiments/{expId}/query/synapses/1");
                synResp.EnsureSuccessStatusCode();
                var syn = await synResp.Content.ReadFromJsonAsync<JsonElement>();

                var weight = syn.GetProperty("weight").GetSingle();
                Assert.AreEqual(123f, weight, 1e-6f, "Gene did not correctly update the synapse weight.");

                // 2. Verify the synapse ID was stored in the correct neuron's LVar
                var neuron1Resp = await Client.GetAsync($"/api/experiments/{expId}/query/neurons/1");
                neuron1Resp.EnsureSuccessStatusCode();
                var neuron1 = await neuron1Resp.Content.ReadFromJsonAsync<JsonElement>();

                var lvarsArray = neuron1.GetProperty("localVariables");
                var storedSynapseId = (ulong)lvarsArray[100].GetSingle();
                
                Assert.AreEqual(1ul, storedSynapseId, "Gene did not correctly store the synapse ID in the neuron's LVar.");
            }
            catch
            {
                await DumpWorldAsync(nameof(Gene_CanCreateAndModifySynapse_AndStoreIdInLVar), expId);
                throw;
            }
        }
    }
}
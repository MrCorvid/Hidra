// Hidra.Tests/Api/DeterminismApiTests.cs

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Hidra.API.DTOs;
using Hidra.Tests.Api.TestHelpers;

namespace Hidra.Tests.Api
{
    [TestClass]
    public class DeterminismApiTests : BaseApiTestClass
    {
        [TestMethod]
        public async Task CreateExperiment_WithSameSeed_ProducesIdenticalWorlds()
        {
            // --- ARRANGE ---
            var hex = await AssembleGenomeAsync("GN\nAPI_CreateNeuron 1 1 1\nAPI_CreateNeuron 2 2 2\nAPI_CreateNeuron 3 3 3");
            var request = new CreateExperimentRequestDto
            {
                Name = "deterministic-test",
                HGLGenome = hex,
                Config = TestDefaults.GetDeterministicConfig(),
                Seed = 1337
            };

            // --- ACT ---
            var expId1 = await CreateExperimentAsync(request);
            await StepAsync(expId1); // Step to execute genesis gene

            var expId2 = await CreateExperimentAsync(request);
            await StepAsync(expId2); // Step to execute genesis gene

            // --- ASSERT ---
            var neurons1 = await GetCollectionAsync<JsonElement>($"/api/experiments/{expId1}/query/neurons?pageSize=10");
            var neurons2 = await GetCollectionAsync<JsonElement>($"/api/experiments/{expId2}/query/neurons?pageSize=10");

            Assert.IsNotNull(neurons1);
            Assert.IsNotNull(neurons2);
            var ids1 = neurons1.Select(n => n.GetProperty("id").GetUInt64()).OrderBy(id => id).ToList();
            var ids2 = neurons2.Select(n => n.GetProperty("id").GetUInt64()).OrderBy(id => id).ToList();

            CollectionAssert.AreEqual(ids1, ids2, "Neuron IDs should be identical when using the same seed.");
        }

        [TestMethod]
        public async Task RestoreExperiment_PreservesPrngStateAndContinuesSequence()
        {
            // --- ARRANGE ---
            var initialConfig = TestDefaults.GetDeterministicConfig();
            initialConfig.Seed0 = 42;
            var expId1 = await CreateExperimentAsync(new() { Name = "prng-test", HGLGenome = "GN", Config = initialConfig });

            var createN1Resp = await Client.PostAsJsonAsync($"/api/experiments/{expId1}/manipulate/neurons", new CreateNeuronRequestDto { Position = new(1, 1, 1) }, JsonOpts);
            createN1Resp.EnsureSuccessStatusCode();

            var saveResp = await Client.PostAsJsonAsync($"/api/experiments/{expId1}/save", new SaveRequestDto(), JsonOpts);
            saveResp.EnsureSuccessStatusCode();
            var saveBody = await saveResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            var worldJson = saveBody.GetProperty("worldJson").GetString();
            Assert.IsNotNull(worldJson);

            // --- ACT ---
            var restoreReq = new RestoreExperimentRequestDto 
            { 
                Name = "restored-prng-test",
                SnapshotJson = worldJson, 
                HGLGenome = "GN",
                Config = initialConfig,      // FIX: Added required Config property
                IOConfig = new IOConfigDto() // FIX: Added required IOConfig property
            };
            var restoreResp = await Client.PostAsJsonAsync("/api/experiments/restore", restoreReq, JsonOpts);
            restoreResp.EnsureSuccessStatusCode();
            var restoreBody = await restoreResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            var expId2 = restoreBody.GetProperty("id").GetString();
            Assert.IsNotNull(expId2);

            var createN2_Original_Resp = await Client.PostAsJsonAsync($"/api/experiments/{expId1}/manipulate/neurons", new CreateNeuronRequestDto { Position = new(2, 2, 2) }, JsonOpts);
            var createN2_Restored_Resp = await Client.PostAsJsonAsync($"/api/experiments/{expId2}/manipulate/neurons", new CreateNeuronRequestDto { Position = new(2, 2, 2) }, JsonOpts);
            createN2_Original_Resp.EnsureSuccessStatusCode();
            createN2_Restored_Resp.EnsureSuccessStatusCode();
            
            var n2_Original = await createN2_Original_Resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            var n2_Restored = await createN2_Restored_Resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);

            // --- ASSERT ---
            var id_Original = n2_Original.GetProperty("id").GetUInt64();
            var id_Restored = n2_Restored.GetProperty("id").GetUInt64();
            
            Assert.AreNotEqual(0UL, id_Original, "Generated ID should be non-zero.");
            Assert.AreEqual(id_Original, id_Restored, "The next random ID generated after restoring must match the original sequence.");
        }

        // Helper to simplify getting collections from API
        private async Task<List<T>?> GetCollectionAsync<T>(string url)
        {
            var response = await Client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<T>>(JsonOpts);
        }

        // Helper to step an experiment
        private async Task StepAsync(string expId)
        {
            var response = await Client.PostAsync($"/api/experiments/{expId}/step", null);
            response.EnsureSuccessStatusCode();
        }
    }
}
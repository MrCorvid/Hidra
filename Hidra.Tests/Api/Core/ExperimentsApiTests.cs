// Hidra.Tests/Api/ExperimentsApiTests.cs

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Hidra.API.DTOs;
using Hidra.Tests.Api.TestHelpers;

namespace Hidra.Tests.Api
{
    [TestClass]
    public class ExperimentsApiTests : BaseApiTestClass
    {
        #region Create & Restore

        [TestMethod]
        public async Task CreateExperiment_ValidRequest_Returns201AndCorrectState()
        {
            // --- ARRANGE ---
            var hex = await AssembleGenomeAsync("GN\n");

            var request = new CreateExperimentRequestDto
            {
                Name = "My-First-API-Experiment",
                HGLGenome = hex,
                Config = TestDefaults.GetDeterministicConfig(),
                Seed = 12345
            };

            // --- ACT ---
            var expId = await CreateExperimentAsync(request);

            var status = await GetQueryStatusAsync(expId);

            // --- ASSERT ---
            Assert.IsNotNull(expId);
            
            var expName = status.TryGetProperty("experimentName", out var en)
                          ? en.GetString()
                          : status.GetProperty("name").GetString();
            Assert.AreEqual("My-First-API-Experiment", expName);
            Assert.AreEqual(0UL, status.GetProperty("currentTick").GetUInt64());
        }

        [TestMethod]
        public async Task RestoreExperiment_RoundTrip_SucceedsAndPreservesState()
        {
            // --- ARRANGE ---
            var hex = await AssembleGenomeAsync("GN\n");
            var initialConfig = TestDefaults.GetDeterministicConfig();
            var initialIoConfig = new IOConfigDto();

            var expId = await CreateExperimentAsync(new CreateExperimentRequestDto
            {
                Name = "ExperimentsApiTests_RestoreExperiment_RoundTrip_SucceedsAndPreservesState",
                HGLGenome = hex,
                Config = initialConfig,
                IOConfig = initialIoConfig
            });

            // Tick twice
            await Client.PostAsync($"/api/experiments/{expId}/step", null);
            await Client.PostAsync($"/api/experiments/{expId}/step", null);

            var status1 = await GetQueryStatusAsync(expId);
            Assert.AreEqual(2UL, status1.GetProperty("currentTick").GetUInt64());

            // Save
            var saveRequest = new SaveRequestDto { ExperimentName = "api-save-test" };
            var saveResponse = await Client.PostAsJsonAsync($"/api/experiments/{expId}/save", saveRequest, JsonOpts);
            saveResponse.EnsureSuccessStatusCode();
            var saveBody = await saveResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            Assert.IsNotNull(saveBody);
            var worldJson = saveBody.GetProperty("worldJson").GetString();
            Assert.IsNotNull(worldJson);

            // --- ACT ---
            var restoreRequest = new RestoreExperimentRequestDto
            {
                Name = "restored-exp",
                SnapshotJson = worldJson!,
                HGLGenome = hex,
                Config = initialConfig,      // FIX: Added required Config property
                IOConfig = initialIoConfig   // FIX: Added required IOConfig property
            };
            var restoreResponse = await Client.PostAsJsonAsync("/api/experiments/restore", restoreRequest, JsonOpts);
            Assert.AreEqual(HttpStatusCode.Created, restoreResponse.StatusCode);
            var restoreBody = await restoreResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            Assert.IsNotNull(restoreBody);
            var expId2 = restoreBody.GetProperty("id").GetString();
            Assert.IsNotNull(expId2);

            // --- ASSERT ---
            var status2 = await GetQueryStatusAsync(expId2!);
            var restoredName = status2.TryGetProperty("experimentName", out var rn)
                               ? rn.GetString()
                               : status2.GetProperty("name").GetString();
            Assert.AreEqual("restored-exp", restoredName);
            Assert.AreEqual(2UL, status2.GetProperty("currentTick").GetUInt64(), "Restored world should keep the tick count.");
        }

        #endregion

        #region List & Get

        [TestMethod]
        public async Task ListExperiments_ReturnsAllAndFiltersByState()
        {
            // --- ARRANGE ---
            var hex = await AssembleGenomeAsync("GN\n");

            var expId_Idle = await CreateExperimentAsync(new CreateExperimentRequestDto { Name = "IdleExp", HGLGenome = hex });
            var expId_Running = await CreateExperimentAsync(new CreateExperimentRequestDto { Name = "RunningExp", HGLGenome = hex });
            var expId_Paused = await CreateExperimentAsync(new CreateExperimentRequestDto { Name = "PausedExp", HGLGenome = hex });

            await Client.PostAsync($"/api/experiments/{expId_Running}/start", null);
            await Client.PostAsync($"/api/experiments/{expId_Paused}/start", null);
            await Client.PostAsync($"/api/experiments/{expId_Paused}/pause", null);

            // --- ACT & ASSERT ---
            var allResponse = await Client.GetAsync("/api/experiments");
            allResponse.EnsureSuccessStatusCode();
            var allList = await allResponse.Content.ReadFromJsonAsync<List<JsonElement>>(JsonOpts);
            Assert.IsNotNull(allList);
            Assert.IsTrue(allList.Count >= 3, "There should be at least 3 experiments.");

            var runningResponse = await Client.GetAsync("/api/experiments?state=running");
            runningResponse.EnsureSuccessStatusCode();
            var runningList = await runningResponse.Content.ReadFromJsonAsync<List<JsonElement>>(JsonOpts);
            Assert.IsNotNull(runningList);
            Assert.AreEqual(1, runningList.Count);
            Assert.AreEqual("RunningExp", runningList.First().GetProperty("name").GetString());

            var pausedResponse = await Client.GetAsync("/api/experiments?state=Paused");
            pausedResponse.EnsureSuccessStatusCode();
            var pausedList = await pausedResponse.Content.ReadFromJsonAsync<List<JsonElement>>(JsonOpts);
            Assert.IsNotNull(pausedList);
            Assert.AreEqual(1, pausedList.Count);
            Assert.AreEqual("PausedExp", pausedList.First().GetProperty("name").GetString());
        }

        [TestMethod]
        public async Task GetExperiment_NotFound_Returns404()
        {
            // --- ACT ---
            var response = await Client.GetAsync("/api/experiments/exp_invalid-id-123");

            // --- ASSERT ---
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }

        #endregion

        #region Delete

        [TestMethod]
        public async Task DeleteExperiment_RemovesFromManagerAndReturns204()
        {
            // --- ARRANGE ---
            var hex = await AssembleGenomeAsync("GN\n");

            var expId = await CreateExperimentAsync(new CreateExperimentRequestDto
            {
                Name = $"{GetType().Name}_{TestContext?.TestName ?? "UnknownTest"}",
                HGLGenome = hex,
                Config = TestDefaults.GetDeterministicConfig(),
                IOConfig = new IOConfigDto()
            });

            var getResponse1 = await Client.GetAsync($"/api/experiments/{expId}");
            Assert.AreEqual(HttpStatusCode.OK, getResponse1.StatusCode);

            // --- ACT ---
            var deleteResponse = await Client.DeleteAsync($"/api/experiments/{expId}");

            // --- ASSERT ---
            Assert.AreEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            var getResponse2 = await Client.GetAsync($"/api/experiments/{expId}");
            Assert.AreEqual(HttpStatusCode.NotFound, getResponse2.StatusCode);
        }

        #endregion

        // NOTE: The gene execution tests were removed from this file as they are not directly related to the core
        // experiments API (Create, Restore, List, Get, Delete). They belong in more specific test files.
    }
}
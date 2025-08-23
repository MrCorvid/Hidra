// Hidra.Tests/Api/ManipulationApiTests.cs

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Hidra.API.DTOs;
using Hidra.Core;
using Hidra.Tests.Api.TestHelpers;
using Newtonsoft.Json.Linq; // Added for JObject access
using System;

namespace Hidra.Tests.Api
{
    [TestClass]
    public class ManipulationApiTests : BaseApiTestClass
    {
        private async Task<string> CreateDefaultExperimentAsync(string? name = null)
        {
            var hex = await AssembleGenomeAsync("GN");
            return await CreateExperimentAsync(name ?? $"{GetType().Name}_{TestContext?.TestName ?? "Unnamed"}", hex);
        }

        #region Neuron Manipulation

        [TestMethod]
        public async Task CreateAndDeleteNeuron_Lifecycle_Succeeds()
        {
            // --- ARRANGE ---
            var expId = await CreateDefaultExperimentAsync();

            // --- ACT 1: CREATE ---
            var neuronId = await CreateNeuronAsync(expId, 1, 2, 3);

            // --- ASSERT 1: EXISTS ---
            var neuronData = await GetNeuronAsync(expId, neuronId);
            Assert.AreEqual(1f, neuronData.GetProperty("position").GetProperty("x").GetSingle());

            var status1 = await GetQueryStatusAsync(expId);
            Assert.AreEqual(1, status1.GetProperty("neuronCount").GetInt32());

            // --- ACT 2: DELETE ---
            var deleteResponse = await Client.DeleteAsync($"/api/experiments/{expId}/manipulate/neurons/{neuronId}");
            Assert.AreEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            // --- ASSERT 2: NOT FOUND ---
            var getResponse2 = await Client.GetAsync($"/api/experiments/{expId}/query/neurons/{neuronId}");
            Assert.AreEqual(HttpStatusCode.NotFound, getResponse2.StatusCode);

            var status2 = await GetQueryStatusAsync(expId);
            Assert.AreEqual(0, status2.GetProperty("neuronCount").GetInt32());
        }

        [TestMethod]
        public async Task PatchLVars_UpdatesValuesCorrectly()
        {
            // --- ARRANGE ---
            var expId = await CreateDefaultExperimentAsync();
            var neuronId = await CreateNeuronAsync(expId, 0, 0, 0);
            var lvarWrites = new Dictionary<int, float> { { 5, 99.9f } };

            // --- ACT ---
            await PatchNeuronLVarsAsync(expId, neuronId, lvarWrites);

            // --- ASSERT ---
            var neuronData = await GetNeuronAsync(expId, neuronId);
            var lvars = neuronData.GetProperty("localVariables");
            // FIX: Use the array indexer for localVariables
            Assert.AreEqual(99.9f, lvars[5].GetSingle(), 1e-6f);
        }

        #endregion

        #region Synapse Manipulation

        [TestMethod]
        public async Task CreateAndModifySynapse_Succeeds()
        {
            // --- ARRANGE ---
            var expId = await CreateDefaultExperimentAsync();
            var n1Id = await CreateNeuronAsync(expId, 0, 0, 0);
            var n2Id = await CreateNeuronAsync(expId, 1, 1, 1);

            // --- ACT 1: CREATE ---
            var synapseJson = await CreateSynapseAsync(expId, n1Id, n2Id, (int)SignalType.Delayed, 0.5f, 1.0f);
            var synapseId = synapseJson.GetProperty("id").GetUInt64();
            Assert.AreEqual(0.5f, synapseJson.GetProperty("weight").GetSingle());

            // --- ACT 2: MODIFY ---
            var patchPayload = new { Weight = -0.75f, SignalType = SignalType.Immediate };
            await PatchSynapseAsync(expId, synapseId, patchPayload);

            // --- ASSERT ---
            var modifiedSynapse = await GetSynapseAsync(expId, synapseId);
            Assert.AreEqual(-0.75f, modifiedSynapse.GetProperty("weight").GetSingle());
            // FIX: Assert against the string representation of the enum.
            Assert.AreEqual(SignalType.Immediate.ToString(), modifiedSynapse.GetProperty("signalType").GetString(), ignoreCase: true);
        }

        #endregion

        #region Concurrency Tests

        [TestMethod]
        public async Task CreateNeuron_IsThreadSafe()
        {
            // --- ARRANGE ---
            var expId = await CreateDefaultExperimentAsync();
            const int taskCount = 50;
            var status1 = await GetQueryStatusAsync(expId);
            var initialCount = status1.GetProperty("neuronCount").GetInt32();

            // --- ACT ---
            var tasks = Enumerable.Range(0, taskCount)
                .Select(_ => CreateNeuronAsync(expId, 0, 0, 0));
            await Task.WhenAll(tasks);

            // --- ASSERT ---
            var status2 = await GetQueryStatusAsync(expId);
            var finalCount = status2.GetProperty("neuronCount").GetInt32();
            Assert.AreEqual(initialCount + taskCount, finalCount,
                "The final neuron count should reflect all concurrent additions.");
        }

        [TestMethod]
        public async Task DeleteNeuron_IsThreadSafe()
        {
            // --- ARRANGE ---
            var expId = await CreateDefaultExperimentAsync();
            const int neuronCount = 50;
            var creationTasks = Enumerable.Range(0, neuronCount)
                .Select(_ => CreateNeuronAsync(expId, 0, 0, 0));
            var neuronIds = await Task.WhenAll(creationTasks);

            var status1 = await GetQueryStatusAsync(expId);
            Assert.AreEqual(neuronCount, status1.GetProperty("neuronCount").GetInt32());

            // --- ACT ---
            var deleteTasks = neuronIds
                .Select(id => Client.DeleteAsync($"/api/experiments/{expId}/manipulate/neurons/{id}"))
                .ToList();
            await Task.WhenAll(deleteTasks);

            // --- ASSERT ---
            var status2 = await GetQueryStatusAsync(expId);
            var finalCount = status2.GetProperty("neuronCount").GetInt32();
            Assert.AreEqual(0, finalCount, "All concurrently removed neurons should be gone.");
        }

        #endregion
    }
}
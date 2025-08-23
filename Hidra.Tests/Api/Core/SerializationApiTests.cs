// Hidra.Tests/Api/SerializationApiTests.cs

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Hidra.API.DTOs;
using Hidra.Tests.Api.TestHelpers;
using Newtonsoft.Json.Linq;

namespace Hidra.Tests.Api
{
    [TestClass]
    public class SerializationApiTests : BaseApiTestClass
    {
        [TestMethod]
        public async Task SaveState_ReturnsJsonWithCorrectCoreProperties()
        {
            // --- ARRANGE ---
            var hgl = @"
                PUSH_CONST 1 1 1 
                CreateNeuron
                POP
                GN
            ";
            var hex = await AssembleGenomeAsync(hgl);

            var expId = await CreateExperimentAsync(new CreateExperimentRequestDto
            {
                Name = nameof(SaveState_ReturnsJsonWithCorrectCoreProperties),
                HGLGenome = hex,
                Config = TestDefaults.GetDeterministicConfig(),
                IOConfig = new IOConfigDto()
            });
            
            await StepAsync(expId);

            var status = await GetQueryStatusAsync(expId);
            Assert.AreEqual(1, status.GetProperty("neuronCount").GetInt32(), "PRE-CONDITION: World should have 1 neuron.");

            // --- ACT ---
            var saveRequest = new SaveRequestDto { ExperimentName = "api-save-test" };
            var response = await Client.PostAsJsonAsync($"/api/experiments/{expId}/save", saveRequest);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var worldJsonString = body.GetProperty("worldJson").GetString();
            Assert.IsFalse(string.IsNullOrEmpty(worldJsonString));

            // --- ASSERT ---
            using var jsonDoc = JsonDocument.Parse(worldJsonString);
            var root = jsonDoc.RootElement;

            Assert.IsTrue(root.TryGetProperty("_neurons", out var neuronsElement));
            var neuronCountInJson = neuronsElement.EnumerateObject().Count(p => p.Name != "$id");
            Assert.AreEqual(1, neuronCountInJson, "The serialized neuron dictionary should contain exactly 1 entry.");
        }

        [TestMethod]
        public async Task SaveAndRestore_RoundTrip_PreservesWorldState()
        {
            // --- ARRANGE ---
            var hgl = @"
                PUSH_CONST 10 20 30
                CreateNeuron
                POP
                GN
            ";
            var hex = await AssembleGenomeAsync(hgl);

            var initialConfig = TestDefaults.GetDeterministicConfig();
            var initialIoConfig = new IOConfigDto();
            var expId = await CreateExperimentAsync(new CreateExperimentRequestDto
            {
                Name = nameof(SaveAndRestore_RoundTrip_PreservesWorldState),
                HGLGenome = hex,
                Config = initialConfig,
                IOConfig = initialIoConfig
            });

            const ulong addedNeuronId = 1;

            await StepAsync(expId); // Tick 1: Neuron created
            await StepAsync(expId); // Tick 2
            await StepAsync(expId); // Tick 3

            var saveRequest = new SaveRequestDto { ExperimentName = "save-for-restore-test" };
            var saveResponse = await Client.PostAsJsonAsync($"/api/experiments/{expId}/save", saveRequest);
            saveResponse.EnsureSuccessStatusCode();

            var saveBody = await saveResponse.Content.ReadFromJsonAsync<JsonElement>();
            var worldJson = saveBody.GetProperty("worldJson").GetString();
            Assert.IsNotNull(worldJson);

            // --- ACT ---
            var restoreRequest = new RestoreExperimentRequestDto
            {
                Name = "restored-from-api",
                SnapshotJson = worldJson,
                HGLGenome = hex,
                Config = initialConfig,
                IOConfig = initialIoConfig
            };
            var restoreResponse = await Client.PostAsJsonAsync("/api/experiments/restore", restoreRequest);
            if (restoreResponse.StatusCode != HttpStatusCode.Created)
            {
                var errorBody = await restoreResponse.Content.ReadAsStringAsync();
                Assert.Fail($"Restore operation failed with status {restoreResponse.StatusCode}. Body: {errorBody}");
            }
            
            var restoreBody = await restoreResponse.Content.ReadFromJsonAsync<JsonElement>();
            var expId2 = restoreBody.GetProperty("id").GetString();
            Assert.IsNotNull(expId2);

            // --- ASSERT ---
            var status1 = await GetQueryStatusAsync(expId);
            var status2 = await GetQueryStatusAsync(expId2);

            Assert.AreEqual(3ul, status2.GetProperty("currentTick").GetUInt64());
            Assert.AreEqual(1, status2.GetProperty("neuronCount").GetInt32());
            Assert.AreEqual(status1.GetProperty("currentTick").GetUInt64(), status2.GetProperty("currentTick").GetUInt64());
            Assert.AreEqual(status1.GetProperty("neuronCount").GetInt32(), status2.GetProperty("neuronCount").GetInt32());

            var neuron1 = await GetNeuronAsync(expId, addedNeuronId);
            var neuron2 = await GetNeuronAsync(expId2, addedNeuronId);

            var pos1 = neuron1.GetProperty("position");
            var pos2 = neuron2.GetProperty("position");

            Assert.AreEqual(10.0f, pos2.GetProperty("x").GetSingle(), "Restored neuron X position should be 10.0");
            Assert.AreEqual(20.0f, pos2.GetProperty("y").GetSingle(), "Restored neuron Y position should be 20.0");
            Assert.AreEqual(30.0f, pos2.GetProperty("z").GetSingle(), "Restored neuron Z position should be 30.0");

            Assert.AreEqual(pos1.GetProperty("x").GetSingle(), pos2.GetProperty("x").GetSingle(), 1e-6f);
            Assert.AreEqual(pos1.GetProperty("y").GetSingle(), pos2.GetProperty("y").GetSingle(), 1e-6f);
            Assert.AreEqual(pos1.GetProperty("z").GetSingle(), pos2.GetProperty("z").GetSingle(), 1e-6f);
        }
    }
}
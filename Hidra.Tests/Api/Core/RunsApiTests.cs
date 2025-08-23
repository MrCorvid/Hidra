// Hidra.Tests/Api/RunsApiTests.cs

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Hidra.API.DTOs;
using Hidra.Tests.Api.TestHelpers;
using Hidra.Core; // Added for HidraConfig

namespace Hidra.Tests.Api
{
    [TestClass]
    public class RunsApiTests : BaseApiTestClass
    {
        private async Task<JsonElement> PostAndAwaitRunCompletion(string expId, CreateRunRequestDto request)
        {
            var response = await Client.PostAsJsonAsync($"/api/experiments/{expId}/runs", request, JsonOpts);
            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Assert.Fail($"Failed to create run. Expected 202 Accepted but got {response.StatusCode}. Body: {errorBody}");
            }
            
            var run = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            var runId = run.GetProperty("id").GetString();
            Assert.IsNotNull(runId);

            for (int i = 0; i < 100; i++)
            {
                var runStatusResp = await Client.GetAsync($"/api/experiments/{expId}/runs/{runId}");
                runStatusResp.EnsureSuccessStatusCode();
                var runStatus = await runStatusResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
                var statusString = runStatus.GetProperty("status").GetString();
                if (statusString != "Running" && statusString != "Pending")
                {
                    return runStatus;
                }
                await Task.Delay(50);
            }
            Assert.Fail("Run did not complete in time.");
            return default;
        }

        [TestMethod]
        public async Task CreateRun_RunFor_StepsCorrectNumberOfTicks()
        {
            var expId = await CreateExperimentAsync(new CreateExperimentRequestDto
            {
                Name = $"{nameof(RunsApiTests)}_{nameof(CreateRun_RunFor_StepsCorrectNumberOfTicks)}",
                HGLGenome = TestDefaults.MinimalGenome,
                Config = TestDefaults.GetDeterministicConfig(),
                IOConfig = new IOConfigDto()
            });

            var runRequest = new CreateRunRequestDto
            {
                Type = "runFor",
                Parameters = new RunParametersDto { Ticks = 7 }
            };

            var completedRun = await PostAndAwaitRunCompletion(expId, runRequest);
            var status = await GetQueryStatusAsync(expId);

            Assert.AreEqual("Completed", completedRun.GetProperty("status").GetString());
            Assert.AreEqual(7UL, completedRun.GetProperty("endTick").GetUInt64());
            Assert.AreEqual(7UL, status.GetProperty("currentTick").GetUInt64());
        }

        [TestMethod]
        public async Task CreateRun_RunUntil_StopsWhenPredicateIsMet()
        {
            // --- ARRANGE ---
            // Gene 0 creates a neuron at (0,0,0). No brain is set -> DummyBrain.
            // Gene 1 wires: (a) Input node 100 -> neuron (immediate, weight 1)
            //               (b) Neuron -> World OUTPUT node 200 (synapse type 1)
            var hgl = @"
                # Gene 0: Genesis
                PUSH_CONST 0 0 0
                CreateNeuron
                GN

                # Gene 1: Wiring (no brain configured -> DummyBrain)
                # Input(100) -> Neuron (immediate potential pulse)
                PUSH_CONST 2 100 0 1 1
                AddSynapse

                # Neuron -> WORLD OUTPUT(200) (type=1 means 'to world output')
                PUSH_CONST 1 200 0 1 1
                AddSynapse
                GN
                GN
                GN
                GN
            ";
            var hex = await AssembleGenomeAsync(hgl);

            var ioConfig = new IOConfigDto
            {
                InputNodeIds = new List<ulong> { 100 },
                OutputNodeIds = new List<ulong> { 200 }
            };

            var config = TestDefaults.GetDeterministicConfig();

            var expId = await CreateExperimentAsync(new CreateExperimentRequestDto
            {
                Name = $"{nameof(RunsApiTests)}_{nameof(CreateRun_RunUntil_StopsWhenPredicateIsMet)}",
                HGLGenome = hex,
                Config = config,
                IOConfig = ioConfig
            });

            // Feed input=1 at node 100 (this raises the neuron's potential to 1 on tick 2+)
            await SetInputValuesAsync(expId, new Dictionary<ulong, float> { { 100, 1.0f } });

            var runRequest = new CreateRunRequestDto
            {
                Type = "runUntil",
                Parameters = new RunParametersDto
                {
                    MaxTicks = 10,
                    // Stop when WORLD output[200] >= 0.5 (DummyBrain maps activation potential to output=1)
                    Predicate = new PredicateDto
                    {
                        Type = "outputAbove",
                        OutputId = 200,
                        Value = 0.5f
                    }
                }
            };

            // --- ACT ---
            var completedRun = await PostAndAwaitRunCompletion(expId, runRequest);
            var status = await GetQueryStatusAsync(expId);

            // --- ASSERT ---
            Assert.AreEqual("Completed", completedRun.GetProperty("status").GetString(),
                "Run should complete because the world-output predicate was met.");
            Assert.AreEqual("Predicate met.", completedRun.GetProperty("completionReason").GetString());

            // Expect early stop (first activation after inputs start flowing)
            var endTick = completedRun.GetProperty("endTick").GetUInt64();
            Assert.IsTrue(endTick <= 5UL, $"Run should stop early (by tick 5), but stopped at tick {endTick}");

            var currentTick = status.GetProperty("currentTick").GetUInt64();
            Assert.AreEqual(endTick, currentTick, "Experiment's final tick should match the run's end tick.");
        }
    }
}
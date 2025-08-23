// Hidra.Tests/Api/Core/SynapseConditionApiTests.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Hidra.API.DTOs;
using Hidra.Core;
using Hidra.Tests.Api.TestHelpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Hidra.Tests.Api.Core
{
    [TestClass]
    public class SynapseConditionApiTests : BaseApiTestClass
    {
        // ---- Local helpers (only where BaseApiTestClass has no equivalent) ----

        private async Task<float[]> GetHormonesAsync(string expId)
        {
            var resp = await Client.GetAsync($"/api/experiments/{expId}/query/hormones");
            if (!resp.IsSuccessStatusCode)
            {
                TestContext.WriteLine($"[GET hormones] {resp.StatusCode}:\n{await resp.Content.ReadAsStringAsync()}");
            }
            resp.EnsureSuccessStatusCode();
            // Use the base class JsonOpts to stay consistent with its serializer config
            return (await resp.Content.ReadFromJsonAsync<float[]>(JsonOpts)) ?? Array.Empty<float>();
        }

        private static bool IsTypeAndTarget(JsonElement ev, string typeName, ulong targetId)
        {
            if (!ev.TryGetProperty("type", out var typeProp)) return false;

            bool typeMatches = false;
            if (typeProp.ValueKind == JsonValueKind.String)
            {
                typeMatches = string.Equals(typeProp.GetString(), typeName, StringComparison.OrdinalIgnoreCase);
            }
            else if (typeProp.ValueKind == JsonValueKind.Number)
            {
                if (Enum.TryParse<EventType>(typeName, true, out var expectedType))
                {
                    typeMatches = typeProp.GetInt32() == (int)expectedType;
                }
            }
            if (!typeMatches) return false;

            return ev.TryGetProperty("targetId", out var idProp) && idProp.GetUInt64() == targetId;
        }

        [TestMethod]
        public async Task SynapseConditions_FireAsExpected_ViaHGL()
        {
            // --- ARRANGE ---
            var hglSource = @"
                # Neuron 1 (Immediate)
                PUSH_CONST 0 0 0
                CreateNeuron

                PUSH_CONST 2 100 0 2 0
                AddSynapse
                # Neuron 2 (Delayed)
                PUSH_CONST 1 0 0
                CreateNeuron
                PUSH_CONST 2 100 1 2 2
                AddSynapse

                # Neuron 3 (Conditional: Temporal RisingEdge threshold=3)
                PUSH_CONST 2 0 0
                CreateNeuron
                PUSH_CONST 2 100 0 2 0
                AddSynapse
                PUSH_CONST 0 2 0 3 0
                SetSynapseCondition
                GN
            ";

            var genomeHex = await AssembleGenomeAsync(hglSource);
            var createRequest = new CreateExperimentRequestDto
            {
                Name = nameof(SynapseConditions_FireAsExpected_ViaHGL),
                HGLGenome = genomeHex,
                Config = TestDefaults.GetDeterministicConfig(),
                IOConfig = new IOConfigDto { InputNodeIds = new List<ulong> { 100 } }
            };
            var expId = await CreateExperimentAsync(createRequest);

            // Tick 0 (genesis) -> Step to 1
            await StepAsync(expId); // -> Tick 1

            const ulong INPUT_ID = 100;
            const ulong N1 = 1, N2 = 2, N3 = 3;

            // --- ACT: quiet tick then inject input and step ---
            await StepAsync(expId); // -> Tick 2 (quiet before input)
            await SetInputValuesAsync(expId, new Dictionary<ulong, float> { { INPUT_ID, 1.5f } });
            await StepAsync(expId); // processes Tick 2 with the injected input

            // --- ASSERT: Tick 2 ---
            // Neuron 1 should activate immediately on Tick 2
            var t2_events = await GetEventsAsync(expId, 2);
            Assert.IsTrue(t2_events.Any(e => IsTypeAndTarget(e, "Activate", N1)), "Neuron 1 should activate on Tick 2.");

            // Delayed synapse should schedule its PotentialPulse for Tick 4
            var t4_sched = await GetEventsAsync(expId, 4);
            Assert.IsTrue(t4_sched.Any(e => IsTypeAndTarget(e, "PotentialPulse", N2)), "Expected PotentialPulse for Neuron 2 scheduled at Tick 4.");

            // --- Advance and verify Neuron 2 on Tick 4 ---
            await SetInputValuesAsync(expId, new Dictionary<ulong, float> { { INPUT_ID, 0f } });
            await StepAsync(expId); // -> Tick 3
            await StepAsync(expId); // -> Tick 4 (delayed pulse lands)

            var t4_events = await GetEventsAsync(expId, 4);
            Assert.IsTrue(t4_events.Any(e => IsTypeAndTarget(e, "Activate", N2)), "Neuron 2 should activate on Tick 4.");

            // --- PHASE 3a: Below-threshold (should NOT trigger N3) ---
            await SetInputValuesAsync(expId, new Dictionary<ulong, float> { { INPUT_ID, 1.49f } });
            await StepAsync(expId); // -> Tick 5 processed

            var t5_events = await GetEventsAsync(expId, 5);
            Assert.IsFalse(t5_events.Any(e => IsTypeAndTarget(e, "Activate", N3)), "Neuron 3 should NOT activate on Tick 5 (below threshold).");

            // --- PHASE 3b: Rising edge over threshold -> should trigger N3 ---
            await SetInputValuesAsync(expId, new Dictionary<ulong, float> { { INPUT_ID, 0f } });
            await StepAsync(expId); // -> Tick 6
            await SetInputValuesAsync(expId, new Dictionary<ulong, float> { { INPUT_ID, 3.05f } });
            await StepAsync(expId); // -> Tick 7

            var t7_events = await GetEventsAsync(expId, 7);
            Assert.IsTrue(t7_events.Any(e => IsTypeAndTarget(e, "Activate", N3)), "Neuron 3 SHOULD activate on Tick 7 after rising-edge over threshold.");
        }

        [TestMethod]
        public async Task Condition_can_be_toggled_by_hormone()
        {
            // --- ARRANGE ---
            var hglSource = @"PUSH_CONST 7 1
                StoreGVar
                GN
            ";

            var genomeHex = await AssembleGenomeAsync(hglSource);
            var expId = await CreateExperimentAsync(nameof(Condition_can_be_toggled_by_hormone), genomeHex);

            // --- ACT & ASSERT 1 ---
            await StepAsync(expId);

            // Use a local helper that honors BaseApiTestClass's serializer config
            var hormones1 = await GetHormonesAsync(expId);
            Assert.IsNotNull(hormones1);
            Assert.IsTrue(hormones1.Length > 7, "Hormone array should contain index 7.");
            Assert.AreEqual(1.0f, hormones1[7], 1e-6f, "Hormone[7] should be 1.0 after HGL setup.");

            // --- ACT & ASSERT 2 ---
            await PatchHormonesAsync(expId, new Dictionary<int, float> { { 7, 0.0f } });

            var hormones2 = await GetHormonesAsync(expId);
            Assert.IsNotNull(hormones2);
            Assert.IsTrue(hormones2.Length > 7, "Hormone array should contain index 7.");
            Assert.AreEqual(0.0f, hormones2[7], 1e-6f, "Hormone[7] should be 0.0 after runtime patch.");
        }

        [TestMethod]
        public async Task Runtime_patch_of_synapse_applies_immediately()
        {
            // --- ARRANGE ---
            var hglSource = @"
                PUSH_CONST 0 0 0
                CreateNeuron
                PUSH_CONST 1 0 0
                CreateNeuron
                PUSH_CONST 1
                SetSystemTarget
                PUSH_CONST 0 2 1 1 0
                AddSynapse
                GN
            ";
            var genomeHex = await AssembleGenomeAsync(hglSource);
            var expId = await CreateExperimentAsync(nameof(Runtime_patch_of_synapse_applies_immediately), genomeHex);

            await StepAsync(expId);

            const ulong synId = 1;

            // --- ACT ---
            var patch = new { Weight = 2.0f, SignalType = SignalType.Immediate };
            await PatchSynapseAsync(expId, synId, patch);

            // --- ASSERT ---
            var synJson = await GetSynapseAsync(expId, synId);
            Assert.AreEqual(2.0f, synJson.GetProperty("weight").GetSingle(), 1e-6f);
            
            // --- FIX: ---
            // The API sends enums as strings ("Immediate"), but the test was trying to parse a number (GetInt32).
            // This now gets the string value and parses it into the SignalType enum for a correct comparison.
            var actualSignalType = Enum.Parse<SignalType>(synJson.GetProperty("signalType").GetString()!, true);
            Assert.AreEqual(SignalType.Immediate, actualSignalType);
        }

        [TestMethod]
        public async Task Condition_can_reference_target_variable()
        {
            // --- ARRANGE ---
            var hglSource = @"
                PUSH_CONST 0 0 0
                CreateNeuron
                PUSH_CONST 123 1
                StoreLVar
                GN
            ";
            var genomeHex = await AssembleGenomeAsync(hglSource);
            var expId = await CreateExperimentAsync(nameof(Condition_can_reference_target_variable), genomeHex);

            await StepAsync(expId); // realize genome

            const ulong neuronId = 1;
            const int lvarIndex = 123;

            // --- ACT ---
            await PatchNeuronLVarsAsync(expId, neuronId, new Dictionary<int, float> { { lvarIndex, 0.75f } });

            // --- ASSERT ---
            // Fetch raw as JObject (BaseApiTestClass helper) but handle the array shape correctly.
            var neuronJson = await GetNeuronAsJObjectAsync(expId, neuronId);
            TestContext.WriteLine($"Neuron JSON Response: {neuronJson}");

            var lvarsToken = neuronJson["LocalVariables"] ?? neuronJson["localVariables"];
            Assert.IsNotNull(lvarsToken, "Expected 'LocalVariables' array in neuron JSON response.");

            // It is an array, not an object; index with int
            var lvarsArray = lvarsToken as JArray;
            Assert.IsNotNull(lvarsArray, "'LocalVariables' should be a JSON array.");

            Assert.IsTrue(lvarsArray.Count > lvarIndex, $"LocalVariables length {lvarsArray.Count} does not include index {lvarIndex}.");
            var val = (float)lvarsArray[lvarIndex];
            Assert.AreEqual(0.75f, val, 1e-6f, $"LVar[{lvarIndex}] should equal 0.75 after patch.");
        }
    }
}
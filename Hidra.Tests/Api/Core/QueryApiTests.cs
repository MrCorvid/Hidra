// Hidra.Tests/Api/QueryApiTests.cs

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
using System;

namespace Hidra.Tests.Api
{
    [TestClass]
    public class QueryApiTests : BaseApiTestClass
    {
        #region Neighbor Query Tests

        [TestMethod]
        public async Task GetNeighbors_WithPopulatedWorld_ReturnsCorrectNeurons()
        {
            // --- ARRANGE ---
            var hgl = @"
                PUSH_CONST 10 10 10
                CreateNeuron
                POP

                PUSH_CONST 11 10 10
                CreateNeuron
                POP

                PUSH_CONST 8 10 10
                CreateNeuron
                POP

                PUSH_CONST 10
                PUSH_CONST 125
                PUSH_CONST 10
                DIV
                PUSH_CONST 10
                CreateNeuron
                POP

                PUSH_CONST 10 10 13
                CreateNeuron
                POP
                
                PUSH_CONST 10 0 10
                CreateNeuron
                POP
                GN
            ";
            var hex = await AssembleGenomeAsync(hgl);
            var expId = await CreateExperimentAsync(new CreateExperimentRequestDto
            {
                Name = $"{nameof(QueryApiTests)}_{nameof(GetNeighbors_WithPopulatedWorld_ReturnsCorrectNeurons)}",
                HGLGenome = hex,
                Config = TestDefaults.GetDeterministicConfig()
            });
            await Client.PostAsync($"/api/experiments/{expId}/step", null);

            const ulong centerId = 1, n1Id = 2, n2Id = 3, n3Id = 4;

            // --- ACT ---
            var response  = await Client.GetAsync($"/api/experiments/{expId}/query/neighbors?centerId={centerId}&radius=2.5");
            response.EnsureSuccessStatusCode();
            var neighbors = await response.Content.ReadFromJsonAsync<List<JsonElement>>(JsonOpts);

            // --- ASSERT ---
            Assert.IsNotNull(neighbors);
            Assert.AreEqual(3, neighbors.Count);
            var neighborIds = new HashSet<ulong>(neighbors.Select(n => n.GetProperty("id").GetUInt64()));
            Assert.IsTrue(neighborIds.Contains(n1Id) && neighborIds.Contains(n2Id) && neighborIds.Contains(n3Id));
        }

        [TestMethod]
        public async Task GetNeighbors_WithZeroRadius_ReturnsEmptyList()
        {
            var hgl = "PUSH_CONST 0 0 0\nCreateNeuron\nPOP\nGN";
            var hex = await AssembleGenomeAsync(hgl);
            var expId = await CreateExperimentAsync(new CreateExperimentRequestDto { Name = "neighbor-zero-radius-test", HGLGenome = hex });
            await Client.PostAsync($"/api/experiments/{expId}/step", null);

            const ulong centerId = 1;

            var response  = await Client.GetAsync($"/api/experiments/{expId}/query/neighbors?centerId={centerId}&radius=0");
            response.EnsureSuccessStatusCode();
            var neighbors = await response.Content.ReadFromJsonAsync<List<JsonElement>>(JsonOpts);

            Assert.IsNotNull(neighbors);
            Assert.AreEqual(0, neighbors.Count);
        }

        [TestMethod]
        public async Task GetNeighbors_WithInvalidCenterId_ReturnsNotFound()
        {
            var expId = await CreateExperimentAsync("neighbor-invalid-id-test", TestDefaults.MinimalGenome);
            var response = await Client.GetAsync($"/api/experiments/{expId}/query/neighbors?centerId=999999&radius=5");
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }
        
         #endregion

        #region Event Query Tests

        [TestMethod]
        public async Task GetEventsForTick_ForDelayedSignal_ShowsEventOnCorrectFutureTick()
        {
            // --- ARRANGE ---
            var hgl = @"
                # Genesis Gene
                PUSH_CONST 0 0 0
                CreateNeuron
                
                PUSH_CONST 1 0 0
                CreateNeuron
                
                GN
                # Gestation Gene
                GetSelfId
                PUSH_CONST 1
                EQ
                JZ END_GESTATION
                PUSH_CONST 0 2 1 1 1
                AddSynapse
                PUSH_CONST 2 100 1
                PUSH_CONST 11 10
                DIV
                PUSH_CONST 1
                AddSynapse
            END_GESTATION:
            ";
            var hex = await AssembleGenomeAsync(hgl);

            var createRequest = new CreateExperimentRequestDto
            {
                Name = "event-delayed-signal-test",
                HGLGenome = hex,
                Config = TestDefaults.GetDeterministicConfig(),
                IOConfig = new IOConfigDto { InputNodeIds = new List<ulong> { 100 } }
            };
            var expId = await CreateExperimentAsync(createRequest);

            // Ticks 0, 1: Genesis and Gestation
            await StepAsync(expId);
            await StepAsync(expId);

            const ulong inputId = 100;
            const ulong n2Id = 2;

            // --- ACT ---

            // Tick 2: Turn the input ON. The input node fires, queues event for n1 for Tick 3.
            await SetInputValuesAsync(expId, new Dictionary<ulong, float> { { inputId, 1.0f } });
            await StepAsync(expId);

            // Tick 3: Turn the input OFF. Event from input arrives at n1. n1 fires, queues event for n2 for Tick 5.
            await SetInputValuesAsync(expId, new Dictionary<ulong, float> { { inputId, 0.0f } });
            await StepAsync(expId);
            
            // Tick 4: Quiet tick.
            await StepAsync(expId);

            // Tick 5: Event from n1 arrives at n2, which causes it to activate in the same tick.
            await StepAsync(expId);

            var eventsTick5 = await GetEventsAsync(expId, tick: 5);

            // --- ASSERT ---
            Assert.IsNotNull(eventsTick5);
            // FIX: Expect TWO events on tick 5 (the incoming pulse and the resulting activation).
            Assert.AreEqual(2, eventsTick5.Count, "Expected exactly two events on tick 5.");

            // Verify the PotentialPulse event
            var pulseEvent = eventsTick5.FirstOrDefault(e => e.GetProperty("type").GetString()!.Equals(EventType.PotentialPulse.ToString(), StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(pulseEvent, "A PotentialPulse event should have occurred on tick 5.");
            Assert.AreEqual(n2Id, pulseEvent.GetProperty("targetId").GetUInt64(), "The target of the pulse should be neuron 2.");
            
            // Verify the Activate event
            var activateEvent = eventsTick5.FirstOrDefault(e => e.GetProperty("type").GetString()!.Equals(EventType.Activate.ToString(), StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(activateEvent, "An Activate event should have occurred on tick 5.");
            Assert.AreEqual(n2Id, activateEvent.GetProperty("targetId").GetUInt64(), "The target of the activation should be neuron 2.");
        }

        [TestMethod]
        public async Task GetEventsForTick_WithMultipleEvents_ReturnsAllEvents()
        {
            // --- ARRANGE ---
            var hgl = @"
                PUSH_CONST 0 0 0
                CreateNeuron
                POP
                
                PUSH_CONST 1 0 0
                CreateNeuron
                POP
                
                PUSH_CONST 2 0 0
                CreateNeuron
                POP
                GN
            ";
            var hex = await AssembleGenomeAsync(hgl);
            var expId = await CreateExperimentAsync(new CreateExperimentRequestDto { Name = "event-multiple-test", HGLGenome = hex });
            
            // --- ACT ---
            await StepAsync(expId);
            var events = await GetEventsAsync(expId, tick: 1);

            // --- ASSERT ---
            Assert.AreEqual(3, events.Count, "Should be exactly 3 'ExecuteGene' events queued for tick 1.");
            
            var eventTypes = events.Select(e => e.GetProperty("type").GetString()!).ToList();
            Assert.IsTrue(eventTypes.All(t => t.Equals(EventType.ExecuteGene.ToString(), StringComparison.OrdinalIgnoreCase)));

            var eventTargetIds = events.Select(e => e.GetProperty("targetId").GetUInt64()).ToHashSet();
            var expectedTargetIds = new HashSet<ulong> { 1, 2, 3 };
            Assert.IsTrue(expectedTargetIds.SetEquals(eventTargetIds));
        }

        #endregion
    }
}
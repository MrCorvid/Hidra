// Hidra.Tests/Api/MetricsApiTests.cs

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Hidra.API.DTOs;
using Hidra.Core;
using Hidra.Tests.Api.TestHelpers;

namespace Hidra.Tests.Api
{
    [TestClass]
    public class MetricsApiTests : BaseApiTestClass
    {
        [TestMethod]
        public async Task GetLatestMetrics_AfterNeuronActivity_ReturnsCorrectAverages()
        {
            // --- ARRANGE ---
            // 1. Provide a COMPLETE genome with a Genesis (GN separator) and Gestation part.
            var hgl = @"
                # GENESIS: Create two neurons
                PUSH_CONST 0 0 0
                CreateNeuron
                POP
                PUSH_CONST 1 1 1
                CreateNeuron
                POP
                GN
                # GESTATION: The rest of the logic
                GetSelfId
                PUSH_CONST 1
                EQ
                JZ END_GESTATION
                
                # If self is neuron 1, create synapse from input 100 to self.
                PUSH_CONST 2 100 1
                PUSH_CONST 15 10
                DIV
                PUSH_CONST 1
                AddSynapse
                POP

            END_GESTATION:
            ";
            var hex = await AssembleGenomeAsync(hgl);
            
            var expId = await CreateExperimentAsync(new() {
                Name = "MetricsApiTest_Final",
                HGLGenome = hex,
                Config = new HidraConfig { MetricsEnabled = true },
                IOConfig = new IOConfigDto { InputNodeIds = new List<ulong> { 100 } }
            });

            // 2. Step to Tick 1 to execute the Genesis gene and create the neurons.
            await StepAsync(expId);
            const ulong n2Id = 2;
            
            // 3. Deactivate neuron 2 to test the activeNeuronCount metric.
            await Client.PostAsync($"/api/experiments/{expId}/manipulate/neurons/{n2Id}:deactivate", null);

            // 4. Step to Tick 2 to execute the Gestation gene.
            await StepAsync(expId);

            // --- ACT ---
            // 5. Stimulate neuron 1 with a single pulse to make it fire.
            await SetInputValuesAsync(expId, new Dictionary<ulong, float> { { 100, 1.0f } });
            await StepAsync(expId); // Tick 3: Input pulse is sent.
            await SetInputValuesAsync(expId, new Dictionary<ulong, float> { { 100, 0.0f } });
            await StepAsync(expId); // Tick 4: Pulse arrives, Neuron 1 fires.
            
            // 6. Step one more time for the metrics to be sampled *after* the firing.
            await StepAsync(expId); // Tick 5

            // 7. Retrieve the latest metrics report.
            var metricsResp = await Client.GetAsync($"/api/experiments/{expId}/metrics/latest");
            metricsResp.EnsureSuccessStatusCode();
            var metrics = await metricsResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            
            // --- ASSERT ---
            Assert.IsNotNull(metrics);

            // Verify counts
            Assert.AreEqual(2, metrics.GetProperty("neuronCount").GetInt32(), "Neuron count should include all neurons.");
            Assert.AreEqual(1, metrics.GetProperty("activeNeuronCount").GetInt32(), "Active neuron count should exclude the deactivated neuron.");
            
            // Verify that firing activity is correctly reflected in the metrics.
            Assert.IsTrue(metrics.GetProperty("meanFiringRate").GetSingle() > 0, 
                "Mean firing rate should be greater than zero after a neuron has fired.");
        }
    }
}
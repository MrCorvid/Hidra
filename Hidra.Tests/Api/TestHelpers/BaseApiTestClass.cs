// Hidra.Tests/Api/TestHelpers/BaseApiTestClass.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Hidra.Core.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using Hidra.API.Services;
using Hidra.Tests.Api;
using Hidra.Core.Brain; // <-- FIX: Added the missing using directive for brain types
using Newtonsoft.Json.Linq; // <-- FIX: Added missing using directive for JObject

namespace Hidra.Tests.Api.TestHelpers
{
    [TestClass]
    public abstract class BaseApiTestClass
    {
        protected class TestApiFactory : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment("Development");
            }
        }

        public TestContext TestContext { get; set; } = null!;
        protected TestApiFactory Factory = null!;
        protected HttpClient     Client  = null!;

        protected string? _currentTestExpId;

        protected static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        [TestInitialize]
        public void _Init()
        {
            Logger.Shutdown();
            Logger.Init();
            
            _currentTestExpId = null;

            Factory = new TestApiFactory();
            Client  = Factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("http://localhost")
            });
            Client.DefaultRequestHeaders.Accept.Clear();
            Client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        [TestCleanup]
        public async Task _Cleanup()
        {
            if (TestContext.CurrentTestOutcome == UnitTestOutcome.Failed)
            {
                await DumpApiLogsOnFailure();
            }

            if (Factory != null)
            {
                using (var scope = Factory.Services.CreateScope())
                {
                    var manager = scope.ServiceProvider.GetRequiredService<ExperimentManager>();
                    manager.Dispose(); 
                }
            }

            Client?.Dispose();
            Factory?.Dispose();
        }

        protected static HttpContent AsJson(object value, JsonSerializerOptions? opts = null)
            => new StringContent(JsonSerializer.Serialize(value, opts ?? JsonOpts), Encoding.UTF8, "application/json");

        protected static Task<HttpResponseMessage> PatchAsJsonAsync(HttpClient client, string url, object value)
            => client.SendAsync(new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = AsJson(value) });

        // ---------- High-level helpers ----------
        
        protected async Task AddInputNodeAsync(string expId, ulong nodeId, float initialValue = 0f)
        {
            var res = await Client.PostAsJsonAsync($"/api/experiments/{expId}/manipulate/inputs",
                new { Id = nodeId, InitialValue = initialValue });
            res.EnsureSuccessStatusCode();
        }

        protected async Task SetInputValuesAsync(string expId, Dictionary<ulong, float> values)
        {
            var res = await Client.PutAsJsonAsync($"/api/experiments/{expId}/manipulate/inputs", values);
            res.EnsureSuccessStatusCode();
        }

        protected async Task<string> AssembleGenomeAsync(string hgl)
        {
            var res = await Client.PostAsJsonAsync("/api/assembler/assemble", new { SourceCode = hgl });
            if (!res.IsSuccessStatusCode)
            {
                TestContext.WriteLine($"[Assembler/assemble] {res.StatusCode}:\n{await res.Content.ReadAsStringAsync()}");
            }
            res.EnsureSuccessStatusCode();
            
            var json = await res.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            var hexBytecode = json.GetProperty("hexBytecode").GetString()!;

            TestContext.WriteLine("--- HGL Assembly Trace ---");
            TestContext.WriteLine("Source HGL:");
            TestContext.WriteLine(hgl);
            TestContext.WriteLine($"Compiled Bytecode: {hexBytecode}");
            TestContext.WriteLine("--------------------------");

            return hexBytecode;
        }

        protected async Task<string> CreateExperimentAsync(Hidra.API.DTOs.CreateExperimentRequestDto req)
        {
            var res = await Client.PostAsJsonAsync("/api/experiments", req);
            res.EnsureSuccessStatusCode();
            var payload = await res.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            var id = payload.GetProperty("id").GetString()!;
            
            _currentTestExpId = id;

            return id;
        }

        protected Task<string> CreateExperimentAsync(string name, string? hex = null)
            => CreateExperimentAsync(new Hidra.API.DTOs.CreateExperimentRequestDto
               { Name = string.IsNullOrWhiteSpace(name) ? $"exp_{Guid.NewGuid():N}" : name, HGLGenome = hex ?? TestDefaults.MinimalGenome });

        protected async Task<ulong> CreateNeuronAsync(string expId, float x = 0, float y = 0, float z = 0)
        {
            var res = await Client.PostAsJsonAsync($"/api/experiments/{expId}/manipulate/neurons",
                                                   new { Position = new { X = x, Y = y, Z = z } });
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            return json.GetProperty("id").GetUInt64();
        }

        protected async Task PatchNeuronLVarsAsync(string expId, ulong neuronId, IDictionary<int, float> writes)
        {
            var res = await PatchAsJsonAsync(Client, $"/api/experiments/{expId}/manipulate/neurons/{neuronId}/lvars",
                                             new { LocalVariables = writes });
            if (!res.IsSuccessStatusCode)
            {
                TestContext.WriteLine($"[Patch LVars] {res.StatusCode}:\n{await res.Content.ReadAsStringAsync()}");
            }
            res.EnsureSuccessStatusCode();
        }

        protected async Task PatchHormonesAsync(string expId, IDictionary<int, float> values)
        {
            var res = await PatchAsJsonAsync(Client, $"/api/experiments/{expId}/manipulate/hormones", values);
            if (!res.IsSuccessStatusCode) Console.WriteLine($"[Patch Hormones] {res.StatusCode}:\n{await res.Content.ReadAsStringAsync()}");
            res.EnsureSuccessStatusCode();
        }

        protected async Task<JsonElement> CreateSynapseAsync(string expId, ulong sourceId, ulong targetId, int signalType, float weight, float parameter)
        {
            var res = await Client.PostAsJsonAsync($"/api/experiments/{expId}/manipulate/synapses",
                                                   new { SourceId = sourceId, TargetId = targetId, SignalType = signalType, Weight = weight, Parameter = parameter });
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        }

        protected async Task PatchSynapseAsync(string expId, ulong synId, object patch)
        {
            var res = await PatchAsJsonAsync(Client, $"/api/experiments/{expId}/manipulate/synapses/{synId}", patch);
            res.EnsureSuccessStatusCode();
        }

        protected async Task<JsonElement> GetNeuronAsync(string expId, ulong id)
        {
            var res = await Client.GetAsync($"/api/experiments/{expId}/query/neurons/{id}");
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        }

        protected async Task<JObject> GetNeuronAsJObjectAsync(string expId, ulong id)
        {
            var res = await Client.GetAsync($"/api/experiments/{expId}/query/neurons/{id}");
            res.EnsureSuccessStatusCode();
            
            // Use Newtonsoft.Json to parse the response, which understands $type
            var jsonString = await res.Content.ReadAsStringAsync();
            return JObject.Parse(jsonString);
        }

        protected async Task<JsonElement> GetSynapseAsync(string expId, ulong id)
        {
            var res = await Client.GetAsync($"/api/experiments/{expId}/query/synapses/{id}");
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        }

        protected async Task<JsonElement> GetQueryStatusAsync(string expId)
        {
            var res = await Client.GetAsync($"/api/experiments/{expId}/query/status");
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        }

        protected async Task DumpWorldAsync(string expId, string label)
        {
            try
            {
                Console.WriteLine($"\n=== DUMP WORLD: {label} / {expId} ===");
                var neuronsRes = await Client.GetAsync($"/api/experiments/{expId}/query/neurons?page=1&pageSize=10000");
                var synsRes    = await Client.GetAsync($"/api/experiments/{expId}/query/synapses?page=1&pageSize=10000");
                Console.WriteLine($"[Neurons {neuronsRes.StatusCode}]");
                Console.WriteLine(await neuronsRes.Content.ReadAsStringAsync());
                Console.WriteLine($"[Synapses {synsRes.StatusCode}]");
                Console.WriteLine(await synsRes.Content.ReadAsStringAsync());
                Console.WriteLine("=== END DUMP ===\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DumpWorldAsync] failed: {ex}");
            }
        }

        protected async Task StepAsync(string expId)
        {
            var response = await Client.PostAsync($"/api/experiments/{expId}/step", null);
            response.EnsureSuccessStatusCode();
        }
        
        protected async Task<List<JsonElement>> GetEventsAsync(string expId, ulong tick)
        {
            var response = await Client.GetAsync($"/api/experiments/{expId}/query/events?tick={tick}");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<JsonElement>>(JsonOpts) ?? new List<JsonElement>();
        }

        // --- Brain-specific Helpers ---

        protected async Task SetBrainTypeAsync(string expId, ulong neuronId, object payload)
        {
            var res = await Client.PostAsJsonAsync($"/api/experiments/{expId}/neurons/{neuronId}/brain/type", payload);
            res.EnsureSuccessStatusCode();
        }

        protected async Task<JsonElement> AddBrainNodeAsync(string expId, ulong neuronId, NNNodeType nodeType)
        {
            var res = await Client.PostAsJsonAsync($"/api/experiments/{expId}/neurons/{neuronId}/brain/nodes", new { NodeType = nodeType });
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<JsonElement>();
        }

        protected async Task AddBrainConnectionAsync(string expId, ulong neuronId, int from, int to, float weight = 1.0f)
        {
            var res = await Client.PostAsJsonAsync($"/api/experiments/{expId}/neurons/{neuronId}/brain/connections", new { FromNodeId = from, ToNodeId = to, Weight = weight });
            res.EnsureSuccessStatusCode();
        }
        
        private async Task DumpApiLogsOnFailure()
        {
            if (string.IsNullOrEmpty(_currentTestExpId))
            {
                Console.WriteLine("\n\n--- FAILED TEST LOG DUMP: SKIPPED (No experiment ID was captured) ---\n");
                return;
            }

            Console.WriteLine($"\n\n--- FAILED TEST LOG DUMP: {TestContext.TestName} ---");
            Console.WriteLine($"--- Experiment: '{_currentTestExpId}' ---");

            try
            {
                string url = $"/api/experiments/{_currentTestExpId}/query/logs/text";
                
                var response = await Client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine(await response.Content.ReadAsStringAsync());
                }
                else
                {
                    Console.WriteLine($"[ERROR] Failed to retrieve logs from API. Status: {response.StatusCode}");
                    Console.WriteLine(await response.Content.ReadAsStringAsync());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] An exception occurred while trying to dump API logs: {ex.Message}");
            }
            finally
            {
                Console.WriteLine("--- END OF LOG DUMP ---\n");
            }
        }
    }
}
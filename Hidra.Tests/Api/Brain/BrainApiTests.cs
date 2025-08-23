// Hidra.Tests/Api/BrainApiTests.cs

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Hidra.API.DTOs;
using Hidra.Core;
using Hidra.Core.Brain;
using Hidra.Tests.Api.TestHelpers;
using Newtonsoft.Json.Linq;
using System;

namespace Hidra.Tests.Api
{
    [TestClass]
    public class BrainApiTests : BaseApiTestClass
    {
        private async Task<(string expId, ulong neuronId)> SetupExperimentWithNeuron(bool makeNeuralNetwork = false)
        {
            var hex = await AssembleGenomeAsync("GN");
            var expId = await CreateExperimentAsync($"{GetType().Name}_{TestContext?.TestName ?? "UnknownTest"}", hex);
            var neuronId = await CreateNeuronAsync(expId, 0, 0, 0);

            if (makeNeuralNetwork)
            {
                var response = await Client.PostAsJsonAsync($"/api/experiments/{expId}/neurons/{neuronId}/brain/type", 
                    new SetBrainTypeRequestDto { Type = "NeuralNetwork" }, JsonOpts);
                response.EnsureSuccessStatusCode();
            }
            
            return (expId, neuronId);
        }

        #region Brain Structure API Tests

        [TestMethod]
        public async Task AddAndDeleteBrainNodeAndConnection_Lifecycle_Succeeds()
        {
            // --- ARRANGE ---
            var (expId, neuronId) = await SetupExperimentWithNeuron(makeNeuralNetwork: true);

            // --- ACT 1: ADD NODES ---
            var addN0Resp = await Client.PostAsJsonAsync($"/api/experiments/{expId}/neurons/{neuronId}/brain/nodes", new AddBrainNodeRequestDto { NodeType = NNNodeType.Input }, JsonOpts);
            var addN1Resp = await Client.PostAsJsonAsync($"/api/experiments/{expId}/neurons/{neuronId}/brain/nodes", new AddBrainNodeRequestDto { NodeType = NNNodeType.Output }, JsonOpts);
            addN0Resp.EnsureSuccessStatusCode();
            addN1Resp.EnsureSuccessStatusCode();
            var n0 = await addN0Resp.Content.ReadFromJsonAsync<JsonElement>();
            var n1 = await addN1Resp.Content.ReadFromJsonAsync<JsonElement>();
            int n0Id = n0.GetProperty("id").GetInt32();
            int n1Id = n1.GetProperty("id").GetInt32();

            // --- ACT 2: ADD CONNECTION ---
            var addConnReq = new AddBrainConnectionRequestDto { FromNodeId = n0Id, ToNodeId = n1Id, Weight = 0.5f };
            var addConnResp = await Client.PostAsJsonAsync($"/api/experiments/{expId}/neurons/{neuronId}/brain/connections", addConnReq, JsonOpts);
            addConnResp.EnsureSuccessStatusCode();

            // --- ASSERT 1: STRUCTURE EXISTS ---
            var neuronJson1 = await GetNeuronAsJObjectAsync(expId, neuronId);
            // FIX: The JSON key matches the private field name, "_neuralNetwork".
            var network1 = neuronJson1["brain"]?["_neuralNetwork"];
            // FIX: The JSON keys inside the network also match the private field names.
            Assert.AreEqual(2, ((JObject)network1?["_nodes"])?.Properties()?.Count() ?? 0);
            Assert.AreEqual(1, ((JArray)network1?["_connections"])?.Count ?? 0);

            // --- ACT 3: DELETE CONNECTION ---
            var delConnResp = await Client.DeleteAsync($"/api/experiments/{expId}/neurons/{neuronId}/brain/connections?fromNodeId={n0Id}&toNodeId={n1Id}");
            delConnResp.EnsureSuccessStatusCode();
            
            // --- ACT 4: DELETE NODE ---
            var delNodeResp = await Client.DeleteAsync($"/api/experiments/{expId}/neurons/{neuronId}/brain/nodes/{n1Id}");
            delNodeResp.EnsureSuccessStatusCode();

            // --- ASSERT 2: STRUCTURE IS GONE ---
            var neuronJson2 = await GetNeuronAsJObjectAsync(expId, neuronId);
            // FIX: The JSON key matches the private field name, "_neuralNetwork".
            var network2 = neuronJson2["brain"]?["_neuralNetwork"];
            // FIX: The JSON keys inside the network also match the private field names.
            Assert.AreEqual(1, ((JObject)network2?["_nodes"])?.Properties()?.Count() ?? 0);
            Assert.AreEqual(0, ((JArray)network2?["_connections"])?.Count ?? 0);
        }

        [TestMethod]
        public async Task AddConnection_WithCycle_ReturnsConflict()
        {
            // --- ARRANGE ---
            var (expId, neuronId) = await SetupExperimentWithNeuron(makeNeuralNetwork: true);
            await AddBrainNodeAsync(expId, neuronId, NNNodeType.Hidden); // Node 0
            await AddBrainNodeAsync(expId, neuronId, NNNodeType.Hidden); // Node 1
            await AddBrainConnectionAsync(expId, neuronId, 0, 1, 1f);

            // --- ACT: ATTEMPT TO ADD 1 -> 0 to create a cycle ---
            var conn2Req = new AddBrainConnectionRequestDto { FromNodeId = 1, ToNodeId = 0, Weight = 1f };
            var conn2Resp = await Client.PostAsJsonAsync($"/api/experiments/{expId}/neurons/{neuronId}/brain/connections", conn2Req, JsonOpts);

            // --- ASSERT ---
            Assert.AreEqual(HttpStatusCode.Conflict, conn2Resp.StatusCode);
        }

        #endregion

        #region Brain Node & Connection Properties

        [TestMethod]
        public async Task CreateBrainNode_ViaApi_AppliesCorrectDefaults()
        {
            // --- ARRANGE ---
            var (expId, neuronId) = await SetupExperimentWithNeuron(makeNeuralNetwork: true);

            // --- ACT ---
            var newNode = await AddBrainNodeAsync(expId, neuronId, NNNodeType.Hidden);

            // --- ASSERT ---
            Assert.AreEqual(NNNodeType.Hidden.ToString(), newNode.GetProperty("nodeType").GetString(), ignoreCase: true);
            Assert.AreEqual(0f, newNode.GetProperty("bias").GetSingle(), 1e-6f);
            Assert.AreEqual(ActivationFunctionType.Tanh.ToString(), newNode.GetProperty("activationFunction").GetString(), ignoreCase: true);
            Assert.AreEqual(OutputActionType.SetOutputValue.ToString(), newNode.GetProperty("actionType").GetString(), ignoreCase: true);
            Assert.AreEqual(InputSourceType.ActivationPotential.ToString(), newNode.GetProperty("inputSource").GetString(), ignoreCase: true);
        }

        [TestMethod]
        public async Task ConfigureBrainNode_ViaApi_SetsPropertiesCorrectly()
        {
            // --- ARRANGE ---
            var (expId, neuronId) = await SetupExperimentWithNeuron(makeNeuralNetwork: true);
            var node = await AddBrainNodeAsync(expId, neuronId, NNNodeType.Output);
            var nodeId = node.GetProperty("id").GetInt32();

            var configureReq = new ConfigureBrainNodeRequestDto
            {
                Bias = -0.5f, ActionType = OutputActionType.ExecuteGene,
                InputSource = InputSourceType.Health, SourceIndex = 5,
                ActivationFunction = ActivationFunctionType.ReLU
            };

            // --- ACT ---
            var patchResp = await PatchAsJsonAsync(Client, $"/api/experiments/{expId}/neurons/{neuronId}/brain/nodes/{nodeId}", configureReq);
            patchResp.EnsureSuccessStatusCode();
            var patchedNode = await patchResp.Content.ReadFromJsonAsync<JsonElement>();

            // --- ASSERT ---
            Assert.AreEqual(-0.5f, patchedNode.GetProperty("bias").GetSingle(), 1e-6f);
            Assert.AreEqual(OutputActionType.ExecuteGene.ToString(), patchedNode.GetProperty("actionType").GetString(), ignoreCase: true);
            Assert.AreEqual(InputSourceType.Health.ToString(), patchedNode.GetProperty("inputSource").GetString(), ignoreCase: true);
            Assert.AreEqual(5, patchedNode.GetProperty("sourceIndex").GetInt32());
            Assert.AreEqual(ActivationFunctionType.ReLU.ToString(), patchedNode.GetProperty("activationFunction").GetString(), ignoreCase: true);
        }

        #endregion

        #region Brain Constructor & Type API Tests

        [TestMethod]
        public async Task ConstructBrain_ViaApi_SimpleFeedForward_BuildsCorrectTopology()
        {
            // --- ARRANGE ---
            var (expId, neuronId) = await SetupExperimentWithNeuron();
            int numInputs = 2, numOutputs = 1, numHiddenLayers = 1, nodesPerLayer = 3;
            int expectedNodes = numInputs + nodesPerLayer + numOutputs;
            int expectedConnections = (numInputs * nodesPerLayer) + (nodesPerLayer * numOutputs);
            var constructReq = new ConstructBrainRequestDto
            {
                Type = "SimpleFeedForward", NumInputs = numInputs, NumOutputs = numOutputs,
                NumHiddenLayers = numHiddenLayers, NodesPerLayer = nodesPerLayer
            };

            // --- ACT ---
            var response = await Client.PostAsJsonAsync($"/api/experiments/{expId}/neurons/{neuronId}/brain/construct", constructReq, JsonOpts);
            response.EnsureSuccessStatusCode();

            // --- ASSERT ---
            var neuron = await GetNeuronAsJObjectAsync(expId, neuronId);
            // FIX: The JSON key matches the private field name, "_neuralNetwork".
            var network = neuron["brain"]?["_neuralNetwork"];
            // FIX: The JSON keys inside the network also match the private field names.
            Assert.AreEqual(expectedNodes, ((JObject)network?["_nodes"])?.Properties()?.Count() ?? 0);
            Assert.AreEqual(expectedConnections, ((JArray)network?["_connections"])?.Count() ?? 0);
        }

        [TestMethod]
        public async Task SetBrainType_ViaApi_CanSwitchToLogicGate()
        {
            // --- ARRANGE ---
            var (expId, neuronId) = await SetupExperimentWithNeuron();
            
            var neuron1 = await GetNeuronAsJObjectAsync(expId, neuronId);
            Assert.IsNull(neuron1["brain"]?["gateType"], "Default DummyBrain should not have a 'gateType' property.");

            var setTypeReq = new SetBrainTypeRequestDto
            {
                Type = "LogicGate",
                GateType = LogicGateType.NAND,
                Threshold = 0.8f
            };

            // --- ACT ---
            await SetBrainTypeAsync(expId, neuronId, setTypeReq);

            // --- ASSERT ---
            var neuron2 = await GetNeuronAsJObjectAsync(expId, neuronId);
            var logicBrain = neuron2["brain"];

            Assert.IsNotNull(logicBrain?["gateType"], "Switched brain should have a 'gateType' property.");
            Assert.AreEqual(LogicGateType.NAND.ToString(), logicBrain?["gateType"]?.ToString(), ignoreCase: true);
            Assert.AreEqual(0.8f, (float?)logicBrain?["threshold"] ?? 0f, 1e-6f);
        }

        #endregion
    }
}
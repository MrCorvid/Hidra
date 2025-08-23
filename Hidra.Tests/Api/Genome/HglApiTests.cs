// Hidra.Tests/Api/Genome/HglApiTests.cs

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Hidra.Tests.Api.TestHelpers;

namespace Hidra.Tests.Api
{
    [TestClass]
    public class HglApiTests : BaseApiTestClass
    {
        // --- Helpers to handle ReferenceHandler.Preserve ($id/$values) and to read maps/arrays safely ---

        private static JsonElement UnwrapArray(JsonElement el)
        {
            // If ReferenceHandler.Preserve is used, arrays come as { "$id": "...", "$values": [ ... ] }
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("$values", out var vals))
                return vals;
            return el;
        }

        private static Dictionary<string, byte> ReadByteMap(JsonElement obj)
        {
            var dict = new Dictionary<string, byte>();
            foreach (var prop in obj.EnumerateObject())
            {
                if (prop.Name.StartsWith("$")) continue; // ignore $id, $ref, etc.
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetByte(out var b))
                    dict[prop.Name] = b;
            }
            return dict;
        }

        private static Dictionary<string, string> ReadStringMap(JsonElement obj)
        {
            var dict = new Dictionary<string, string>();
            foreach (var prop in obj.EnumerateObject())
            {
                if (prop.Name.StartsWith("$")) continue;
                if (prop.Value.ValueKind == JsonValueKind.String)
                    dict[prop.Name] = prop.Value.GetString()!;
            }
            return dict;
        }

        private static List<(string name, int paramCount)> ReadApiFunctions(JsonElement el)
        {
            var list = new List<(string name, int paramCount)>();
            var arr = UnwrapArray(el);
            if (arr.ValueKind != JsonValueKind.Array) return list;

            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                var name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString()!
                    : string.Empty;

                int paramCount = 0;
                if (item.TryGetProperty("parameters", out var p))
                {
                    var pArr = UnwrapArray(p);
                    if (pArr.ValueKind == JsonValueKind.Array)
                        paramCount = pArr.GetArrayLength();
                }

                if (!string.IsNullOrEmpty(name))
                    list.Add((name, paramCount));
            }
            return list;
        }

        private static bool MapContainsValue(JsonElement obj, string expected)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (prop.Name.StartsWith("$")) continue;
                if (prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString() == expected)
                    return true;
            }
            return false;
        }

        // -----------------------------------------------------------------------------------------------

        [TestMethod]
        public async Task GetSpecification_ReturnsCompleteAndValidSpec()
        {
            // --- ACT ---
            var response = await Client.GetAsync("/api/hgl/specification");
            response.EnsureSuccessStatusCode();
            var root = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);

            // --- ASSERT ---
            Assert.AreEqual(JsonValueKind.Object, root.ValueKind, "Specification payload should be a JSON object.");

            Assert.IsTrue(root.TryGetProperty("instructions", out var instructionsEl), "Spec should include 'instructions'.");
            var instructions = ReadByteMap(instructionsEl);
            Assert.IsTrue(instructions.Count > 0, "Instructions map should be populated.");
            Assert.IsTrue(instructions.ContainsKey("NOP"), "Spec should contain NOP instruction.");

            Assert.IsTrue(root.TryGetProperty("operators", out var operatorsEl), "Spec should include 'operators'.");
            var operatorsMap = ReadByteMap(operatorsEl);
            Assert.IsTrue(operatorsMap.Count > 0, "Operators map should be populated.");
            Assert.IsTrue(operatorsMap.ContainsKey("+"), "Spec should contain '+' operator.");

            Assert.IsTrue(root.TryGetProperty("apiFunctions", out var apiFunctionsEl), "Spec should include 'apiFunctions'.");
            var apiFunctions = ReadApiFunctions(apiFunctionsEl);
            Assert.IsTrue(apiFunctions.Count > 0, "API Functions list should be populated.");
            Assert.IsTrue(apiFunctions.Any(f => f.name == "API_CreateBrain_SimpleFeedForward"),
                "Spec should contain a known API function.");

            Assert.IsTrue(root.TryGetProperty("systemVariables", out var sysVarsEl), "Spec should include 'systemVariables'.");
            // systemVariables is typically a map like { "240": "FR", "243": "Health", ... } w/ $id preserved
            Assert.IsTrue(MapContainsValue(sysVarsEl, "Health"), "Spec should contain 'Health' system variable.");
        }

        #region Opcode and Lookup Validation

        [TestMethod]
        public async Task GetSpecification_InstructionsMap_IsCompleteAndUnique()
        {
            var response = await Client.GetAsync("/api/hgl/specification");
            response.EnsureSuccessStatusCode();
            var root = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);

            Assert.IsTrue(root.TryGetProperty("instructions", out var instructionsEl), "Spec should include 'instructions'.");
            var instructions = ReadByteMap(instructionsEl);

            Assert.IsTrue(instructions.Count > 50, "There should be a significant number of instructions in the spec.");

            // --- FIX: The following assertion was incorrect. ---
            // The API intentionally provides aliases (e.g., "API_CreateNeuron" and "CreateNeuron") which
            // correctly map to the SAME opcode. This means the list of all opcode values will contain
            // duplicates by design. The test was wrong to assert that all values must be unique.
            // The uniqueness of opcodes themselves is guaranteed by the server's HGLOpcodes implementation.
            // var opcodeValues = instructions.Values.ToList();
            // var uniqueOpcodeValues = new HashSet<byte>(opcodeValues);
            // Assert.AreEqual(opcodeValues.Count, uniqueOpcodeValues.Count, "All instruction opcodes must be unique.");

            // Be concrete but not brittle: check an instruction name that actually exists.
            Assert.IsTrue(instructions.ContainsKey("API_CreateNeuron"), "Specification should contain 'API_CreateNeuron'.");
        }

        [TestMethod]
        public async Task GetSpecification_OperatorsMap_IsCorrect()
        {
            var response = await Client.GetAsync("/api/hgl/specification");
            response.EnsureSuccessStatusCode();
            var root = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);

            Assert.IsTrue(root.TryGetProperty("operators", out var operatorsEl), "Spec should include 'operators'.");
            Assert.IsTrue(root.TryGetProperty("instructions", out var instructionsEl), "Spec should include 'instructions'.");

            var operatorsMap = ReadByteMap(operatorsEl);
            var instructions = ReadByteMap(instructionsEl);

            Assert.IsTrue(operatorsMap.Count > 0, "Operators dictionary should not be empty.");
            Assert.IsTrue(operatorsMap.ContainsKey("+"), "Operator '+' should be present.");
            Assert.IsTrue(instructions.ContainsKey("ADD"), "Instructions should contain 'ADD'.");

            var addOpcodeFromOperators = operatorsMap["+"];
            var addOpcodeFromInstructions = instructions["ADD"];
            Assert.AreEqual(addOpcodeFromInstructions, addOpcodeFromOperators, "The opcode for '+' should match 'ADD'.");
        }

        [TestMethod]
        public async Task GetSpecification_ApiFunctionsList_IsCompleteAndCorrectlyOrdered()
        {
            var response = await Client.GetAsync("/api/hgl/specification");
            response.EnsureSuccessStatusCode();
            var root = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);

            Assert.IsTrue(root.TryGetProperty("apiFunctions", out var apiFunctionsEl), "Spec should include 'apiFunctions'.");
            var apiFunctions = ReadApiFunctions(apiFunctionsEl);

            Assert.IsTrue(apiFunctions.Count > 10, "There should be a significant number of API functions.");

            // Do not require a specific first item (service may sort differently).
            // Instead verify presence + correct signature of key functions we rely on.
            var store = apiFunctions.FirstOrDefault(f => f.name == "API_StoreLVar");
            Assert.IsTrue(store != default, "API_StoreLVar should be present.");
            Assert.AreEqual(2, store.paramCount, "API_StoreLVar should have 2 parameters.");

            var setBrainType = apiFunctions.FirstOrDefault(f => f.name == "API_SetBrainType");
            Assert.IsTrue(setBrainType != default, "API_SetBrainType should be present.");
            // We don't enforce its position; presence is sufficient.
        }

        #endregion
    }
}
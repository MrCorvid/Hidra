// Hidra.Tests/Genome/HGLParser/HGLParserTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ProgrammingLanguageNr1;

namespace Hidra.Tests.Core
{
    [TestClass]
    public class HGLParserTests : BaseTestClass
    {
        private HGLParser _parser = null!;

        [TestInitialize]
        public override void BaseInit()
        {
            base.BaseInit();
            _parser = new HGLParser();
        }

        #region Constructor Logic Tests

        [TestMethod]
        public void Constructor_DiscoversAndOrdersApiFunctionsCorrectly()
        {
            var apiMethodsField = typeof(HGLParser).GetField("_apiMethodsByName", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(apiMethodsField, "Private field '_apiMethodsByName' not found in HGLParser.");

            var discoveredMethods = (Dictionary<string, MethodInfo>)apiMethodsField!.GetValue(_parser)!;
            Assert.IsNotNull(discoveredMethods, "The _apiMethodsByName dictionary should not be null.");

            // The parser discovers methods from HidraSprakBridge with the [SprakAPI] attribute.
            // Let's count them directly to get the expected number.
            int expectedApiCount = typeof(HidraSprakBridge)
                .GetMethods()
                .Count(m => m.GetCustomAttribute<SprakAPI>() != null);
            
            Assert.AreEqual(expectedApiCount, discoveredMethods.Count, "The number of discovered API functions should match the count from HidraSprakBridge.");

            // Dictionaries do not guarantee order, so we can't test for it.
            // Instead, we verify that a known, important API function is present in the dictionary.
            Assert.IsTrue(discoveredMethods.ContainsKey("API_CreateNeuron"), "The dictionary must contain the key 'API_CreateNeuron'.");
        }

        #endregion

        #region HexStringToByteArray Tests (Private Method)

        private static byte[] InvokeHexStringToByteArray(string hex)
        {
            var methodInfo = typeof(HGLParser).GetMethod("HexStringToByteArray", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(methodInfo, "Private static method 'HexStringToByteArray' not found.");
            return (byte[])methodInfo!.Invoke(null, new object[] { hex })!;
        }

        [TestMethod]
        public void HexStringToByteArray_WithValidEvenLengthHex_ReturnsCorrectByteArray()
        {
            var expected = new byte[] { 0x01, 0x0A, 0x1B, 0xFF };
            var result = InvokeHexStringToByteArray("010A1BFF");
            CollectionAssert.AreEqual(expected, result);
        }

        [TestMethod]
        public void HexStringToByteArray_WithOddLength_PadsWithZeroAndSucceeds()
        {
            var expected = new byte[] { 0x0A, 0xBC };
            var result = InvokeHexStringToByteArray("ABC");
            CollectionAssert.AreEqual(expected, result);
        }

        #endregion

        #region ParseGenome Logic Tests

        [TestMethod]
        public void ParseGenome_WithMultipleGenes_ParsesAll()
        {
            string genome = "GN0102GN0304";
            var result = _parser.ParseGenome(genome, 0);
            Assert.IsTrue(result.Count >= 2, "Should parse at least two gene segments.");
            Assert.IsTrue(result.Values.All(ast => ast != null), "All parsed ASTs must be non-null.");
        }

        [TestMethod]
        public void ParseGenome_WhenSystemGeneIsEmpty_IncludesItAsEmptyGene()
        {
            string genome = "GNGN01";
            uint systemGeneCount = 1;

            var result = _parser.ParseGenome(genome, systemGeneCount);

            Assert.IsTrue(result.ContainsKey(0), "System gene 0 should be present even if empty.");

            var emptyAst = result[0];
            var token = emptyAst.getToken();
            Assert.AreEqual(Token.TokenType.STATEMENT_LIST, token.getTokenType(), "Empty gene should be represented as a STATEMENT_LIST.");
            Assert.AreEqual("<EMPTY_GENE>", token.getTokenString(), "Empty gene tag should be <EMPTY_GENE>.");
        }

        [TestMethod]
        public void ParseGenome_WhenNonSystemGeneIsEmpty_ItIsIgnored()
        {
            string genome = "GN0102GN"; // Gene 0 is empty (non-system), Gene 1 is "0102"
            uint systemGeneCount = 0;

            var result = _parser.ParseGenome(genome, systemGeneCount);

            Assert.AreEqual(1, result.Count, "Empty non-system genes must be dropped entirely.");
            
            // Verify that the remaining gene is the correct one.
            Assert.IsTrue(result.ContainsKey(1), "The single remaining gene should have ID 1.");

            foreach (var ast in result.Values)
            {
                var t = ast.getToken();
                var tag = t.getTokenString();
                Assert.AreNotEqual("<EMPTY_GENE>", tag, "Empty non-system genes must not be represented as <EMPTY_GENE>.");
                
                if (tag == "<GENE>")
                {
                    Assert.IsTrue(ast.getChildren().Count > 0, "Empty non-system genes must NOT be kept as empty <GENE> blocks.");
                }
            }
        }
        
        [TestMethod]
        public void ParseGenome_WhenGenomeEndsWithGN_IgnoresFinalEmptyString()
        {
            string genome = "GN0102GN";
            uint systemGeneCount = 0;

            var result = _parser.ParseGenome(genome, systemGeneCount);
            
            Assert.AreEqual(1, result.Count, "Should ignore the empty string artifact after the final 'GN'.");
            Assert.IsTrue(result.ContainsKey(1), "The parsed gene should have ID 1.");
        }

        #endregion
    }
}
// Hidra.Tests/Genome/HGLParser/HGLOpcodesTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Hidra.Tests.Genome
{
    [TestClass]
    public class HGLOpcodesTests : BaseTestClass
    {
        #region Static Initialization and Field Assignment

        [TestMethod]
        public void StaticConstructor_AssignsAllOpcodeFieldsWithUniqueValues()
        {
            // --- ARRANGE ---
            // Get all public static byte fields from the HGLOpcodes class.
            var opcodeFields = typeof(HGLOpcodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(f => f.IsInitOnly && f.FieldType == typeof(byte))
                .ToList();

            // --- ACT ---
            var assignedValues = opcodeFields.Select(f => (byte)f.GetValue(null)!).ToList();

            var expectedFieldNames = HGLOpcodes.MasterInstructionOrder
                .Select(name => name.StartsWith("API_") ? name.Substring(4) : name)
                .ToHashSet();
            
            var actualFieldNames = opcodeFields.Select(f => f.Name).ToHashSet();

            // --- ASSERT ---
            var missingInActual = expectedFieldNames.Except(actualFieldNames).ToList();
            var extraInActual = actualFieldNames.Except(expectedFieldNames).ToList();

            string errorMessage = "";
            if (missingInActual.Any())
            {
                errorMessage += $"Fields in MasterInstructionOrder but not declared in class: {string.Join(", ", missingInActual)}. ";
            }
            if (extraInActual.Any())
            {
                errorMessage += $"Fields declared in class but not in MasterInstructionOrder: {string.Join(", ", extraInActual)}.";
            }

            Assert.IsTrue(missingInActual.Count == 0 && extraInActual.Count == 0, errorMessage.Trim());
            
            var distinctValues = new HashSet<byte>(assignedValues);
            Assert.AreEqual(assignedValues.Count, distinctValues.Count, "All assigned opcode byte values must be unique.");
        }

        [TestMethod]
        public void StaticConstructor_FieldValuesMatchMasterListOrder()
        {
            // --- ARRANGE & ACT & ASSERT ---
            for (int i = 0; i < HGLOpcodes.MasterInstructionOrder.Count; i++)
            {
                string instructionName = HGLOpcodes.MasterInstructionOrder[i];
                string fieldName = instructionName.StartsWith("API_") ? instructionName.Substring(4) : instructionName;
                
                var field = typeof(HGLOpcodes).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
                Assert.IsNotNull(field, $"Field '{fieldName}' for instruction '{instructionName}' should exist.");

                var value = (byte)field.GetValue(null)!;
                
                Assert.AreEqual((byte)i, value, $"Opcode for '{instructionName}' (field '{fieldName}') should have value {i} based on its position.");
            }
        }
        
        #endregion

        #region Lookup Table Validation

        [TestMethod]
        public void OpcodeLookup_ContainsAllInstructionsAndCorrectValues()
        {
            // --- ARRANGE ---
            var masterList = HGLOpcodes.MasterInstructionOrder;
            var lookup = HGLOpcodes.OpcodeLookup;

            // --- ACT & ASSERT ---
            // FIX: The lookup table correctly contains more entries than the master list because of aliases
            // (e.g., "API_CreateNeuron" and "CreateNeuron").
            // We should not assert that the counts are equal.
            // Assert.AreEqual(masterList.Count, lookup.Count);

            // Instead, we verify that every instruction from the master list exists in the lookup with the correct value.
            for (int i = 0; i < masterList.Count; i++)
            {
                string instructionName = masterList[i];
                Assert.IsTrue(lookup.ContainsKey(instructionName), $"Lookup should contain key '{instructionName}'.");
                Assert.AreEqual((byte)i, lookup[instructionName], $"Value for '{instructionName}' should be {i}.");
            }
        }

        [TestMethod]
        public void OperatorLookup_MapsSymbolsToCorrectOpcodes()
        {
            // --- ARRANGE ---
            var lookup = HGLOpcodes.OperatorLookup;

            // --- ACT & ASSERT ---
            Assert.AreEqual(11, lookup.Count);
            Assert.AreEqual(HGLOpcodes.ADD, lookup["+"]);
            Assert.AreEqual(HGLOpcodes.SUB, lookup["-"]);
            Assert.AreEqual(HGLOpcodes.MUL, lookup["*"]);
            Assert.AreEqual(HGLOpcodes.EQ, lookup["=="]);
            Assert.AreEqual(HGLOpcodes.LTE, lookup["<="]);
        }

        #endregion

        #region Metadata Validation

        [TestMethod]
        public void InstructionCount_IsCorrect()
        {
            Assert.AreEqual(HGLOpcodes.MasterInstructionOrder.Count, HGLOpcodes.InstructionCount);
        }

        [TestMethod]
        public void ApiOpcodeStart_IsCorrectlyAssigned()
        {
            // --- ARRANGE ---
            // Per the spec, the first instruction is API_NOP at index 0.
            byte expectedValue = HGLOpcodes.OpcodeLookup["API_NOP"];
            
            // --- ACT ---
            var apiStartField = typeof(HGLOpcodes).GetField("ApiOpcodeStart", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(apiStartField, "Could not find internal field ApiOpcodeStart.");
            byte actualValue = (byte)apiStartField.GetValue(null)!;

            // --- ASSERT ---
            Assert.AreEqual(expectedValue, actualValue, "ApiOpcodeStart must match the value of 'API_NOP'.");
        }

        [TestMethod]
        public void OperatorOpcodeStart_IsCorrectlyAssigned()
        {
            // --- ARRANGE ---
            byte expectedValue = HGLOpcodes.OpcodeLookup["ADD"];

            // --- ACT ---
            var operatorStartField = typeof(HGLOpcodes).GetField("OperatorOpcodeStart", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(operatorStartField, "Could not find internal field OperatorOpcodeStart.");
            byte actualValue = (byte)operatorStartField.GetValue(null)!;
            
            // --- ASSERT ---
            Assert.AreEqual(expectedValue, actualValue, "OperatorOpcodeStart must match the value of 'ADD'.");
        }

        #endregion
    }
}
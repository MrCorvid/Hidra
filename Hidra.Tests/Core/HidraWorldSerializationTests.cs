// Hidra.Tests/Core/HidraWorldSerializationTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using Hidra.Core.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Collections.Generic;
using Hidra.Core.Brain;

namespace Hidra.Tests.Core
{
    [TestClass]
    public class HidraWorldSerializationTests
    {
        private HidraWorld _world = null!;

        [TestInitialize]
        public void Init()
        {
            var config = new HidraConfig { SystemGeneCount = 4 };
            _world = new HidraWorld(config, "GN\nGN\nGN\nGN\n");
        }

        [TestMethod]
        public void SaveStateToJson_Parameterless_SerializesCoreProperties()
        {
            // ARRANGE
            // The minimal test genome does not create a neuron. To test serialization,
            // we must manually add a neuron and step the world to create a known state.
            _world.AddNeuron(Vector3.Zero);
            _world.Step(); // This advances the tick from 0 to 1.
            
            // ACT
            string json = _world.SaveStateToJson();
            dynamic deserialized = JsonConvert.DeserializeObject(json)!;

            // ASSERT
            var neuronsObj = (Newtonsoft.Json.Linq.JObject)deserialized._neurons;
            int neuronCount = neuronsObj.Properties().Count(p => !p.Name.StartsWith("$"));
            Assert.AreEqual(1, neuronCount, "Serialized neuron dictionary should contain one entry.");
            Assert.AreEqual(1UL, (ulong)deserialized.CurrentTick, "Tick count should be 1 after one step.");
        }

        [TestMethod]
        public void SaveAndLoadState_RoundTrip_PreservesWorldState()
        {
            // --- ARRANGE ---
            // Step to advance time. Note: the minimal genome doesn't create a neuron.
            _world.Step(); 
            var addedNeuron = _world.AddNeuron(new Vector3(10, 20, 30));

            // --- ACT ---
            string jsonState = _world.SaveStateToJson();
            
            // FIX: Call the new, correct overload with all required parameters.
            var loadedWorld = HidraWorld.LoadStateFromJson(
                jsonState, 
                "GN\nGN\nGN\nGN\n",
                new HidraConfig(),
                Enumerable.Empty<ulong>(),
                Enumerable.Empty<ulong>()
            );

            // --- ASSERT ---
            Assert.AreEqual(_world.Neurons.Count, loadedWorld.Neurons.Count);
            var loadedNeuron = loadedWorld.GetNeuronById(addedNeuron.Id);
            Assert.IsNotNull(loadedNeuron);
            Assert.AreEqual(addedNeuron.Position, loadedNeuron.Position);
            Assert.AreEqual(_world.CurrentTick, loadedWorld.CurrentTick);
        }
    }
}
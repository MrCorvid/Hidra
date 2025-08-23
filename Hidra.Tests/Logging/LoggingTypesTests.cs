// Hidra.Tests/Logging/LoggingTypesTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Hidra.Tests.Logging
{
    [TestClass]
    public class LoggingTypesTests : BaseTestClass
    {
        #region Default Value Tests

        [TestMethod]
        public void LoggerConfig_Constructor_InitializesCollections()
        {
            // --- ARRANGE & ACT ---
            var config = new LoggerConfig();

            // --- ASSERT ---
            Assert.IsNotNull(config.ExperimentSerialization);
            Assert.IsNotNull(config.LogTargets);
            Assert.IsNotNull(config.LogTags);
            Assert.AreEqual(0, config.LogTargets.Count);
            Assert.AreEqual(0, config.LogTags.Count);
        }

        [TestMethod]
        public void ExperimentSerializationConfig_Constructor_SetsDefaultValues()
        {
            var config = new ExperimentSerializationConfig();

            Assert.AreEqual("Experiments", config.BaseOutputDirectory);
            Assert.AreEqual("{name}_{count}_{date}", config.NameTemplate);
            Assert.AreEqual("run_counter.txt", config.RunCounterFile);
        }

        [TestMethod]
        public void LogTargetConfig_Constructor_SetsDefaultValues()
        {
            var config = new LogTargetConfig();

            Assert.AreEqual("file", config.Type);
            Assert.IsNull(config.Path);
        }
        
        [TestMethod]
        public void TagConfig_Constructor_SetsDefaultValues()
        {
            var config = new TagConfig();

            Assert.AreEqual(LogLevel.Info, config.Level);
            Assert.AreEqual("default", config.Target);
            Assert.AreEqual(ConsoleColor.Gray, config.Color);
        }

        #endregion

        #region JSON Serialization/Deserialization Tests

        [TestMethod]
        public void LoggerConfig_JsonRoundTrip_PreservesAllValues()
        {
            // --- ARRANGE ---
            var originalConfig = new LoggerConfig
            {
                ExperimentSerialization = new ExperimentSerializationConfig { BaseOutputDirectory = "MyOutput" },
                LogTargets = new Dictionary<string, LogTargetConfig>
                {
                    ["console"] = new LogTargetConfig { Type = "console" }
                },
                LogTags = new Dictionary<string, TagConfig>
                {
                    ["SIM_CORE"] = new TagConfig { Level = LogLevel.Warning, Target = "console", Color = ConsoleColor.Cyan }
                }
            };

            // --- ACT ---
            string json = JsonConvert.SerializeObject(originalConfig, Formatting.Indented);
            var deserializedConfig = JsonConvert.DeserializeObject<LoggerConfig>(json);

            // --- ASSERT ---
            Assert.IsNotNull(deserializedConfig);
            Assert.AreEqual("MyOutput", deserializedConfig.ExperimentSerialization.BaseOutputDirectory);
            
            Assert.AreEqual(1, deserializedConfig.LogTargets.Count);
            Assert.AreEqual("console", deserializedConfig.LogTargets["console"].Type);
            
            Assert.AreEqual(1, deserializedConfig.LogTags.Count);
            var simCoreTag = deserializedConfig.LogTags["SIM_CORE"];
            Assert.AreEqual(LogLevel.Warning, simCoreTag.Level);
            Assert.AreEqual("console", simCoreTag.Target);
            Assert.AreEqual(ConsoleColor.Cyan, simCoreTag.Color);
        }

        [TestMethod]
        public void TagConfig_JsonEnumConverters_WorkCorrectly()
        {
            // --- ARRANGE ---
            // This JSON uses string representations for the enums.
            string json = @"
            {
                'level': 'Error',
                'target': 'test_target',
                'color': 'Red'
            }";

            // --- ACT ---
            var tagConfig = JsonConvert.DeserializeObject<TagConfig>(json);

            // --- ASSERT ---
            Assert.IsNotNull(tagConfig);
            Assert.AreEqual(LogLevel.Error, tagConfig.Level);
            Assert.AreEqual(ConsoleColor.Red, tagConfig.Color);
        }

        [TestMethod]
        public void LogLevelEnum_HasCorrectUnderlyingValues()
        {
            // This test is a simple contract verification to ensure the order/values of LogLevel
            // do not change unexpectedly, which could break configurations.
            Assert.AreEqual(0, (int)LogLevel.Debug);
            Assert.AreEqual(1, (int)LogLevel.Info);
            Assert.AreEqual(2, (int)LogLevel.Warning);
            Assert.AreEqual(3, (int)LogLevel.Error);
            Assert.AreEqual(4, (int)LogLevel.Fatal);
        }

        #endregion
    }
}
// Hidra.Tests/Logging/LoggerTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core.Logging;
using System.IO;
using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace Hidra.Tests.Logging
{
    [TestClass]
    public class LoggerTests : BaseTestClass
    {
        private string _tempTestDirectory = null!;
        private string _tempConfigPath = null!;
        private StringWriter _consoleOutput = null!;
        private TextWriter _originalConsoleOut = null!;

        [TestInitialize]
        public override void BaseInit()
        {
            // DO NOT call base.BaseInit().
            // The base method calls Logger.Log(), which prematurely initializes the static Logger
            // before this test class can set up its temporary config path. By omitting the base call,
            // we ensure that the Logger remains uninitialized until the test itself makes the first call.
            
            _tempTestDirectory = Path.Combine(Path.GetTempPath(), "HidraLoggerTest_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempTestDirectory);
            _tempConfigPath = Path.Combine(_tempTestDirectory, "logging_config.json");

            _consoleOutput = new StringWriter();
            _originalConsoleOut = Console.Out;
            Console.SetOut(_consoleOutput);
        }

        [TestCleanup]
        public override void BaseCleanup()
        {
            // The base cleanup does not interact with the logger, but we will call it last
            // to maintain its functionality. First, we must manage our own resources.
            Logger.Shutdown();
            
            Console.SetOut(_originalConsoleOut);
            _consoleOutput.Dispose();
            if (Directory.Exists(_tempTestDirectory))
            {
                Directory.Delete(_tempTestDirectory, true);
            }
            
            // We can call the base cleanup now. It only logs an "END" message, which is fine
            // after we have shut down our test-specific logger instance.
            base.BaseCleanup();
        }

        #region Reflection Helpers

        private bool IsLoggerInitialized()
        {
            var field = typeof(Logger).GetField("_isInitialized", BindingFlags.NonPublic | BindingFlags.Static);
            return (bool)field!.GetValue(null)!;
        }

        #endregion

        #region Initialization and Shutdown

        [TestMethod]
        public void Init_WhenConfigFileDoesNotExist_CreatesDefaultAndInitializes()
        {
            // --- ARRANGE ---
            Assert.IsFalse(File.Exists(_tempConfigPath), "Config file should not exist before test.");
            
            // --- ACT ---
            Logger.Init(_tempConfigPath);
            
            // --- ASSERT ---
            Assert.IsTrue(IsLoggerInitialized(), "Logger should be marked as initialized.");
            Assert.IsTrue(File.Exists(_tempConfigPath), "A default config file should have been created.");
            var config = Logger.GetConfig();
            Assert.IsTrue(config.LogTargets.ContainsKey("default"), "Default config should contain a 'default' target.");
        }

        [TestMethod]
        public void Init_WhenConfigFileExists_LoadsConfiguration()
        {
            // --- ARRANGE ---
            string jsonConfig = @"
            {
                ""log_targets"": { ""test_file"": { ""type"": ""file"", ""path"": """ + Path.Combine(_tempTestDirectory, "test.log").Replace("\\", "\\\\") + @""" } },
                ""log_tags"": { ""TEST"": { ""level"": ""Error"", ""target"": ""test_file"" } }
            }";
            File.WriteAllText(_tempConfigPath, jsonConfig);

            // --- ACT ---
            Logger.Init(_tempConfigPath);

            // --- ASSERT ---
            var config = Logger.GetConfig();
            Assert.AreEqual(LogLevel.Error, config.LogTags["TEST"].Level);
            Assert.AreEqual("test_file", config.LogTags["TEST"].Target);
        }

        [TestMethod]
        public void Init_WithMalformedJson_FallsBackToConsoleLogger()
        {
            // --- ARRANGE ---
            File.WriteAllText(_tempConfigPath, "{ not valid json }");

            // --- ACT ---
            Logger.Init(_tempConfigPath);
            Logger.Log("ANY_TAG", LogLevel.Error, "Fallback message");

            // --- ASSERT ---
            string consoleText = _consoleOutput.ToString();
            Assert.IsTrue(consoleText.Contains("[FATAL] Logger initialization failed"), "Fallback warning should be printed to console.");
            Assert.IsTrue(consoleText.Contains("Fallback message"), "Messages after fallback should go to console.");
        }

        [TestMethod]
        public void Init_IsIdempotent()
        {
            // --- ARRANGE ---
            Logger.Init(_tempConfigPath); // First call
            var config1 = Logger.GetConfig();
            
            // Delete the config file to prove the second Init call does nothing.
            File.Delete(_tempConfigPath);

            // --- ACT ---
            Logger.Init(_tempConfigPath); // Second call
            var config2 = Logger.GetConfig();

            // --- ASSERT ---
            Assert.AreSame(config1, config2, "The config object should be the same instance, proving Init was not re-run.");
        }

        [TestMethod]
        public void Shutdown_ResetsStateAndClosesFiles()
        {
            // --- ARRANGE ---
            string logPath = Path.Combine(_tempTestDirectory, "shutdown_test.log");
            string jsonConfig = @"{ ""log_targets"": { ""f"": { ""type"": ""file"", ""path"": """ + logPath.Replace("\\", "\\\\") + @""" } }, ""log_tags"": { ""T"": { ""target"": ""f"" } } }";
            File.WriteAllText(_tempConfigPath, jsonConfig);
            Logger.Init(_tempConfigPath);
            Logger.Log("T", LogLevel.Info, "message");

            // --- ACT ---
            Logger.Shutdown();

            // --- ASSERT ---
            Assert.IsFalse(IsLoggerInitialized(), "Logger should be marked as uninitialized after shutdown.");
            // Verify the file handle was released by successfully deleting the file.
            // If the file were still locked, this would throw an IOException.
            File.Delete(logPath);
        }

        #endregion

        #region Logging Behavior

        [TestMethod]
        public void Log_ToFileTarget_WritesCorrectlyFormattedMessage()
        {
            // --- ARRANGE ---
            string logPath = Path.Combine(_tempTestDirectory, "file_test.log");
            string jsonConfig = @"{ ""log_targets"": { ""file1"": { ""type"": ""file"", ""path"": """ + logPath.Replace("\\", "\\\\") + @""" } }, ""log_tags"": { ""FILE_TAG"": { ""target"": ""file1"" } } }";
            File.WriteAllText(_tempConfigPath, jsonConfig);
            
            // --- ACT ---
            Logger.Init(_tempConfigPath);
            Logger.Log("FILE_TAG", LogLevel.Warning, "Test message for file.");
            Logger.Shutdown(); // Must shut down to flush and release the file handle.

            // --- ASSERT ---
            string logContent = File.ReadAllText(logPath);
            Assert.IsTrue(logContent.Contains("[Warning] [FILE_TAG] Test message for file."));
        }

        [TestMethod]
        public void Log_ObeysLogLevelFiltering()
        {
            // --- ARRANGE ---
            string jsonConfig = @"{ ""log_targets"": { ""console"": { ""type"": ""console"" } }, ""log_tags"": { ""FILTER_TAG"": { ""level"": ""Warning"", ""target"": ""console"" } } }";
            File.WriteAllText(_tempConfigPath, jsonConfig);
            
            // --- ACT ---
            Logger.Init(_tempConfigPath);
            Logger.Log("FILTER_TAG", LogLevel.Info, "This should be filtered.");
            Logger.Log("FILTER_TAG", LogLevel.Warning, "This should be logged.");
            Logger.Log("FILTER_TAG", LogLevel.Error, "This should also be logged.");

            // --- ASSERT ---
            string consoleText = _consoleOutput.ToString();
            Assert.IsFalse(consoleText.Contains("This should be filtered."));
            Assert.IsTrue(consoleText.Contains("This should be logged."));
            Assert.IsTrue(consoleText.Contains("This should also be logged."));
        }

        #endregion

        #region Concurrency

        [TestMethod]
        public async Task Log_FromMultipleThreads_ProducesNonInterleavedOutput()
        {
            // --- ARRANGE ---
            string logPath = Path.Combine(_tempTestDirectory, "concurrent.log");
            string jsonConfig = @"{ ""log_targets"": { ""f"": { ""type"": ""file"", ""path"": """ + logPath.Replace("\\", "\\\\") + @""" } }, ""log_tags"": { ""CONCURRENT"": { ""level"": ""Debug"", ""target"": ""f"" } } }";
            File.WriteAllText(_tempConfigPath, jsonConfig);
            
            Logger.Init(_tempConfigPath);

            const int taskCount = 100;
            var tasks = new List<Task>();

            // --- ACT ---
            for (int i = 0; i < taskCount; i++)
            {
                int taskId = i;
                tasks.Add(Task.Run(() => Logger.Log("CONCURRENT", LogLevel.Debug, $"Message from task {taskId}.")));
            }
            await Task.WhenAll(tasks);
            Logger.Shutdown(); // Flush and close file.

            // --- ASSERT ---
            var lines = File.ReadAllLines(logPath);
            Assert.AreEqual(taskCount, lines.Length, "The number of log entries should match the number of tasks.");

            var uniqueLines = new HashSet<string>(lines);
            Assert.AreEqual(taskCount, uniqueLines.Count, "All log entries should be unique and complete (not interleaved).");
            
            Assert.IsTrue(lines.All(line => line.EndsWith("."))); // A simple check for non-garbled lines
        }

        #endregion
    }
}
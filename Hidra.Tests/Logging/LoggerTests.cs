// Hidra.Tests/Logging/LoggerTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core.Logging;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;

namespace Hidra.Tests.Logging
{
    /// <summary>
    /// Contains unit tests for the static Logger class, verifying its initialization,
    /// configuration loading, message filtering, and thread-safe operation.
    /// </summary>
    [TestClass]
    public class LoggerTests : BaseTestClass
    {
        private const string TestConfigPath = "test_logging_config.json";
        private const string TestLogFilePath = "test_output.log";

        [TestCleanup]
        public void Cleanup()
        {
            // The logger holds file handles, so it must be shut down before we can clean up files.
            // This also resets the static state for the next test run.
            Logger.Shutdown();

            // Clean up created files after each test to ensure isolation.
            if (File.Exists(TestConfigPath))
            {
                File.Delete(TestConfigPath);
            }
            if (File.Exists(TestLogFilePath))
            {
                File.Delete(TestLogFilePath);
            }
        }
        
        #region Initialization and Configuration Tests

        /// <summary>
        /// Verifies that the Logger creates a default configuration file if one does not exist.
        /// </summary>
        [TestMethod]
        public void Init_WhenConfigFileDoesNotExist_CreatesDefaultConfig()
        {
            // Arrange
            // The BaseTestInitialize calls Init(), so we must shut down first to test Init again.
            Logger.Shutdown();
            Assert.IsFalse(File.Exists(TestConfigPath));

            // Act
            Logger.Init(TestConfigPath);

            // Assert
            Assert.IsTrue(File.Exists(TestConfigPath), "A default config file should have been created.");
            var content = File.ReadAllText(TestConfigPath);
            Assert.IsTrue(content.Contains("\"SIM_CORE\""), "Default config should contain default tags.");
        }

        /// <summary>
        /// Verifies that the Logger correctly loads and applies settings from an existing config file.
        /// </summary>
        [TestMethod]
        public void Init_WhenConfigFileExists_LoadsAndAppliesConfig()
        {
            // Arrange
            // 1. RESET: Shut down the logger to clear the state set by BaseTestInitialize.
            Logger.Shutdown();

            // Create a custom config that sets the level for TEST_TAG to Error.
            // Using a modern interpolated string and standard JSON double-quotes for robustness.
            string customConfig = $@"
            {{
                ""log_targets"": {{
                    ""test_file"": {{ ""type"": ""file"", ""path"": ""{TestLogFilePath}"" }}
                }},
                ""log_tags"": {{
                    ""TEST_TAG"": {{ ""level"": ""Error"", ""target"": ""test_file"" }}
                }}
            }}";
            File.WriteAllText(TestConfigPath, customConfig);

            // Act
            // This Init call will now execute correctly.
            Logger.Init(TestConfigPath);
            
            // Log two messages, one of which should be filtered out.
            Logger.Log("TEST_TAG", LogLevel.Info, "This message should be ignored.");
            Logger.Log("TEST_TAG", LogLevel.Error, "This message should be written.");

            // 2. RELEASE: Shut down again to flush buffers and release the file lock before reading.
            Logger.Shutdown();

            // Assert
            Assert.IsTrue(File.Exists(TestLogFilePath), "The log file specified in the config should be created.");
            var logContent = File.ReadAllText(TestLogFilePath);
            
            Assert.IsFalse(logContent.Contains("This message should be ignored."), "Info level message should have been filtered out.");
            Assert.IsTrue(logContent.Contains("This message should be written."), "Error level message should have been logged.");
        }

        #endregion

        #region Filtering and Logging Logic

        /// <summary>
        /// Verifies that the logger correctly filters messages based on the log level
        /// specified in the configuration for a given tag.
        /// </summary>
        [TestMethod]
        public void Log_FiltersMessagesBelowConfiguredLevel()
        {
            // Arrange
            // 1. RESET: Shut down the logger to clear state from the base class's setup.
            Logger.Shutdown();

            string config = $@"
            {{
                ""log_targets"": {{ ""test_file"": {{ ""type"": ""file"", ""path"": ""{TestLogFilePath}"" }} }},
                ""log_tags"": {{ ""TEST"": {{ ""level"": ""Warning"", ""target"": ""test_file"" }} }}
            }}";
            File.WriteAllText(TestConfigPath, config);
            
            // This will now correctly initialize with our test-specific config.
            Logger.Init(TestConfigPath);

            // Act
            Logger.Log("TEST", LogLevel.Debug, "Debug message."); // Should be filtered
            Logger.Log("TEST", LogLevel.Info, "Info message.");   // Should be filtered
            Logger.Log("TEST", LogLevel.Warning, "Warning message."); // Should be logged
            Logger.Log("TEST", LogLevel.Error, "Error message.");   // Should be logged

            // 2. RELEASE: Shut down again to flush the log file and release the handle.
            Logger.Shutdown();

            // Assert
            // This read will now succeed as the file has been created and closed.
            var logContent = File.ReadAllText(TestLogFilePath);
            Assert.IsFalse(logContent.Contains("Debug message."), "Debug message should be filtered.");
            Assert.IsFalse(logContent.Contains("Info message."), "Info message should be filtered.");
            Assert.IsTrue(logContent.Contains("Warning message."), "Warning message should be logged.");
            Assert.IsTrue(logContent.Contains("Error message."), "Error message should be logged.");
        }

        #endregion

        #region Thread-Safety Tests

        /// <summary>
        /// Verifies that the Logger can be initialized and used by multiple threads
        /// concurrently without throwing exceptions or corrupting the log file.
        /// </summary>
        [TestMethod]
        public async Task Log_FromMultipleThreads_IsThreadSafe()
        {
            // Arrange
            const int numTasks = 100;
            const int logsPerTask = 10;
            const int totalLogs = numTasks * logsPerTask;
            
            // 1. RESET: Shut down the logger to clear any state from the base test setup.
            Logger.Shutdown();
            
            // Use standard double-quotes for JSON.
            string config = $@"
            {{
                ""log_targets"": {{ ""test_file"": {{ ""type"": ""file"", ""path"": ""{TestLogFilePath}"" }} }},
                ""log_tags"": {{ ""THREAD_TEST"": {{ ""level"": ""Debug"", ""target"": ""test_file"" }} }}
            }}";
            File.WriteAllText(TestConfigPath, config);
            
            // This Init call will now load our test-specific configuration.
            Logger.Init(TestConfigPath);
            
            // Act
            var tasks = Enumerable.Range(0, numTasks).Select(i =>
                Task.Run(() =>
                {
                    for (int j = 0; j < logsPerTask; j++)
                    {
                        Logger.Log("THREAD_TEST", LogLevel.Info, $"Log from task {i}, message {j}");
                    }
                })
            );
            
            await Task.WhenAll(tasks);

            // 2. FLUSH & CLOSE: Shut down the logger to guarantee all writes are flushed and the file is closed.
            // This is the robust replacement for Task.Delay().
            Logger.Shutdown(); 

            // Assert
            Assert.IsTrue(File.Exists(TestLogFilePath), "Log file should have been created.");
            var lines = File.ReadAllLines(TestLogFilePath);
            
            // The "Logger initialized" message goes to the console, so we expect exactly totalLogs.
            Assert.AreEqual(totalLogs, lines.Length, "All log messages from all threads should be present in the file.");
        }
        #endregion
    }
}
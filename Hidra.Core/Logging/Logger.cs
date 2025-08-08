// Hidra.Core/Logging/Logger.cs
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Hidra.Core.Logging
{
    /// <summary>
    /// A static, thread-safe logger for the entire application.
    /// It is configured via a JSON file and supports multiple output targets (file, console)
    /// and tag-based filtering.
    /// </summary>
    public static class Logger
    {
        private static LoggerConfig _config = new();
        
        // Use ConcurrentDictionary for thread-safe reads and writes of logging targets.
        private static readonly ConcurrentDictionary<string, TextWriter> _targets = new();
        private static ConcurrentDictionary<string, TagConfig> _tagConfigs = new();

        private static readonly object _initLock = new();
        private static volatile bool _isInitialized = false;

        /// <summary>
        /// Initializes the logger from a configuration file. This method is thread-safe
        /// and will only execute its logic once, even if called by multiple threads.
        /// </summary>
        /// <param name="configPath">The path to the logging_config.json file.</param>
        public static void Init(string configPath = "logging_config.json")
        {
            // Double-check locking pattern for high-performance, thread-safe lazy initialization.
            if (_isInitialized)
            {
                return;
            }

            lock (_initLock)
            {
                if (_isInitialized)
                {
                    return;
                }

                try
                {
                    LoadConfiguration(configPath);
                    InitializeTargets();
                    _tagConfigs = new ConcurrentDictionary<string, TagConfig>(_config.LogTags);
                }
                catch (Exception ex)
                {
                    // If any part of initialization fails, fall back to a safe default console logger.
                    Console.WriteLine($"[FATAL] Logger initialization failed: {ex.Message}. Falling back to default console logger.");
                    SetupFallbackLogger();
                }

                _isInitialized = true;
                Log("LOGGER", LogLevel.Info, "Logger initialized.");
            }
        }

        /// <summary>
        /// Shuts down the logger, flushing and closing all file targets, and resetting
        /// its state to be uninitialized. This is primarily for unit testing.
        /// </summary>
        public static void Shutdown()
        {
            lock (_initLock)
            {
                if (!_isInitialized) return;

                foreach (var writer in _targets.Values)
                {
                    // Only try to dispose if it's not the console.
                    if (!ReferenceEquals(writer, Console.Out) && !ReferenceEquals(writer, Console.Error))
                    {
                        try
                        {
                            writer.Flush();
                            writer.Dispose();
                        }
                        catch
                        {
                            // Ignore errors during shutdown, as a file might have been deleted by a test.
                        }
                    }
                }

                _targets.Clear();
                _tagConfigs.Clear();
                _config = new LoggerConfig();
                _isInitialized = false;
            }
        }

        /// <summary>
        /// Logs a message with a specific tag and severity level.
        /// This method is the primary entry point for logging and is fully thread-safe.
        /// </summary>
        /// <param name="tag">A tag to categorize the message (e.g., "SIM_CORE", "PARSER").</param>
        /// <param name="level">The severity level of the message.</param>
        /// <param name="message">The message content to log.</param>
        public static void Log(string tag, LogLevel level, string message)
        {
            // This check is lock-free and ensures initialization happens on the first call.
            if (!_isInitialized)
            {
                Init();
            }

            // GetValueOrDefault on ConcurrentDictionary is not available in all frameworks.
            // This TryGetValue approach is universally safe.
            if (!_tagConfigs.TryGetValue(tag, out var tagConfig))
            {
                // Use a default config for unknown tags.
                tagConfig = new TagConfig { Level = LogLevel.Debug, Target = "console" };
            }

            if (level >= tagConfig.Level)
            {
                if (_targets.TryGetValue(tagConfig.Target, out var writer))
                {
                    string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-7}] [{tag}] {message}";
                    
                    // Locking is required here to prevent garbled console output if multiple
                    // threads try to set the color and write at the same time.
                    lock (writer)
                    {
                        // Only change color if the target is the console.
                        if (ReferenceEquals(writer, Console.Out))
                        {
                            Console.ForegroundColor = tagConfig.Color;
                            writer.WriteLine(logEntry);
                            Console.ResetColor();
                        }
                        else
                        {
                            writer.WriteLine(logEntry);
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Gets the current logger configuration. Initializes the logger if it hasn't been already.
        /// </summary>
        /// <returns>The active <see cref="LoggerConfig"/> instance.</returns>
        public static LoggerConfig GetConfig()
        {
            if (!_isInitialized)
            {
                Init();
            }
            return _config;
        }

        private static void LoadConfiguration(string configPath)
        {
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                _config = JsonConvert.DeserializeObject<LoggerConfig>(json) ?? new LoggerConfig();
            }
            else
            {
                // Create a default configuration if one doesn't exist.
                _config = new LoggerConfig();
                _config.LogTargets["default"] = new LogTargetConfig { Type = "file", Path = "hidra_log.txt" };
                _config.LogTargets["console"] = new LogTargetConfig { Type = "console" };
                _config.LogTags["SIM_CORE"] = new TagConfig { Level = LogLevel.Info, Target = "default", Color = ConsoleColor.Cyan };
                _config.LogTags["LOGGER"] = new TagConfig { Level = LogLevel.Info, Target = "console", Color = ConsoleColor.Yellow };
                File.WriteAllText(configPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
            }
        }

        private static void InitializeTargets()
        {
            foreach (var (key, targetConfig) in _config.LogTargets)
            {
                if (string.Equals(targetConfig.Type, "console", StringComparison.OrdinalIgnoreCase))
                {
                    _targets[key] = Console.Out;
                }
                else if (string.Equals(targetConfig.Type, "file", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(targetConfig.Path))
                {
                    var dir = Path.GetDirectoryName(targetConfig.Path);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    
                    // StreamWriter is not thread-safe by default. TextWriter.Synchronized provides a thread-safe wrapper.
                    var streamWriter = new StreamWriter(targetConfig.Path, append: false) { AutoFlush = true };
                    _targets[key] = TextWriter.Synchronized(streamWriter);
                }
            }
        }

        private static void SetupFallbackLogger()
        {
            // This method creates a minimal, safe logger that only writes to the console
            // in case the main initialization from file fails.
            _targets.Clear();
            _tagConfigs.Clear();
            _targets["console"] = Console.Out;
            _tagConfigs["default"] = new TagConfig { Level = LogLevel.Debug, Target = "console" };
        }
    }
}
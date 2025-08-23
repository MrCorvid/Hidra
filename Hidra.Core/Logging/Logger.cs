// Hidra.Core/Logging/Logger.cs
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hidra.Core.Logging
{
    public readonly record struct LogEntry(DateTime Timestamp, LogLevel Level, string Tag, string Message, string? ExpId = null);

    public static class Logger
    {
        private static LoggerConfig _config = new();
        private static readonly ConcurrentDictionary<string, TextWriter> _targets = new();
        private static ConcurrentDictionary<string, TagConfig> _tagConfigs = new();
        private static readonly object _initLock = new();
        private static volatile bool _isInitialized = false;

        public static void Init(string configPath = "logging_config.json")
        {
            lock (_initLock)
            {
                if (_isInitialized) return;

                try
                {
                    _targets.Clear();
                    _tagConfigs.Clear();
                    LoadConfiguration(configPath);
                    InitializeTargets();
                    _tagConfigs = new ConcurrentDictionary<string, TagConfig>(_config.LogTags);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FATAL] Logger initialization failed: {ex.Message}. Falling back to default console logger.");
                    SetupFallbackLogger();
                }

                _isInitialized = true;
                Log("LOGGER", LogLevel.Info, "Logger initialized.");
            }
        }

        public static void Shutdown()
        {
            lock (_initLock)
            {
                if (!_isInitialized) return;
                Log("LOGGER", LogLevel.Info, "Logger shutting down.");
                foreach (var writer in _targets.Values)
                {
                    if (!ReferenceEquals(writer, Console.Out) && !ReferenceEquals(writer, Console.Error))
                    {
                        try { writer.Flush(); writer.Dispose(); } catch { /* Ignore */ }
                    }
                }
                _targets.Clear();
                _tagConfigs.Clear();
                _config = new LoggerConfig();
                _isInitialized = false;
            }
        }

        public static void Log(string tag, LogLevel level, string message, string? expId = null)
        {
            if (!_isInitialized) return;
            
            if (!_tagConfigs.TryGetValue(tag, out var tagConfig))
            {
                if (!_tagConfigs.TryGetValue("default", out tagConfig))
                {
                    // This is a final fallback if no "default" tag is defined in the config.
                    tagConfig = new TagConfig { Level = LogLevel.Info, Target = "default" };
                }
            }

            if (level < tagConfig.Level) return;

            if (_targets.TryGetValue(tagConfig.Target, out var writer))
            {
                string expIdPart = string.IsNullOrEmpty(expId) ? "" : $" [{expId}]";
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-7}]{expIdPart} [{tag}] {message}";
                
                lock (writer)
                {
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
        
        public static LoggerConfig GetConfig() => _config;

        private static void LoadConfiguration(string configPath)
        {
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                _config = JsonConvert.DeserializeObject<LoggerConfig>(json) ?? new LoggerConfig();
            }
            else
            {
                _config = new LoggerConfig();
                _config.LogTargets["default_file"] = new LogTargetConfig { Type = "file", Path = "hidra_log.txt" };
                // FIX: Rename the console target to "default" to match the test's expectation and create a consistent default setup.
                _config.LogTargets["default"] = new LogTargetConfig { Type = "console" };
                // FIX: Update the default tag to point to the newly named "default" target.
                _config.LogTags["default"] = new TagConfig { Level = LogLevel.Info, Target = "default", Color = ConsoleColor.White };

                try
                {
                    var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                    File.WriteAllText(configPath, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARNING] Could not create default logger config file at '{configPath}': {ex.Message}");
                }
            }
        }

        private static void InitializeTargets()
        {
            foreach (var (key, targetConfig) in _config.LogTargets)
            {
                // NOTE: "api" target type is no longer supported by the global logger.
                if (string.Equals(targetConfig.Type, "console", StringComparison.OrdinalIgnoreCase))
                {
                    _targets[key] = Console.Out;
                }
                else if (string.Equals(targetConfig.Type, "file", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(targetConfig.Path))
                {
                    var dir = Path.GetDirectoryName(targetConfig.Path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var streamWriter = new StreamWriter(targetConfig.Path, append: false) { AutoFlush = true };
                    _targets[key] = TextWriter.Synchronized(streamWriter);
                }
            }
        }

        private static void SetupFallbackLogger()
        {
            _targets.Clear();
            _tagConfigs.Clear();
            _targets["default"] = Console.Out;
            _tagConfigs["default"] = new TagConfig { Level = LogLevel.Debug, Target = "default" };
        }
    }
}
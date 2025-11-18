// Hidra.Core/Logging/LoggingTypes.cs
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;

namespace Hidra.Core.Logging
{
    /// <summary>
    /// Represents the root configuration for the entire logging system.
    /// </summary>
    public class LoggerConfig
    {
        /// <summary>
        /// Configuration settings for saving world states during experiments.
        /// </summary>
        [JsonProperty("experiment_serialization")]
        public ExperimentSerializationConfig ExperimentSerialization { get; set; } = new();

        /// <summary>
        /// A dictionary mapping a target name (e.g., "default_file", "console_out") to its configuration.
        /// </summary>
        [JsonProperty("log_targets")]
        public Dictionary<string, LogTargetConfig> LogTargets { get; set; } = new();

        /// <summary>
        /// A dictionary mapping a log tag (e.g., "SIM_CORE", "PARSER") to its specific logging rules.
        /// </summary>
        [JsonProperty("log_tags")]
        public Dictionary<string, TagConfig> LogTags { get; set; } = new();
    }

    /// <summary>
    /// Defines the configuration for how experiment save files are named and organized.
    /// </summary>
    public class ExperimentSerializationConfig
    {
        /// <summary>
        /// The base directory where all experiment output folders will be created.
        /// </summary>
        [JsonProperty("base_output_directory")]
        public string BaseOutputDirectory { get; set; } = "Experiments";

        /// <summary>
        /// A template for generating unique directory names for each run.
        /// Supported placeholders: {name}, {count}, {date}, {time}.
        /// </summary>
        [JsonProperty("name_template")]
        public string NameTemplate { get; set; } = "{name}_{count}_{date}";
        
        /// <summary>
        /// The name of the file used to store the persistent run counter.
        /// </summary>
        [JsonProperty("run_counter_file")]
        public string RunCounterFile { get; set; } = "run_counter.txt";
    }

    /// <summary>
    /// Defines a single log output target, such as a file or the console.
    /// </summary>
    public class LogTargetConfig
    {
        /// <summary>
        /// The type of the log target. Supported values are "file" or "console".
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; } = "file";

        /// <summary>
        /// The file path for the log. This is only used if the Type is "file".
        /// </summary>
        [JsonProperty("path")]
        public string? Path { get; set; }
    }

    /// <summary>
    /// Defines the logging rules for a specific message tag.
    /// </summary>
    public class TagConfig
    {
        /// <summary>
        /// The minimum log level to record for this tag. Messages below this level will be ignored.
        /// </summary>
        [JsonProperty("level")]
        [JsonConverter(typeof(StringEnumConverter))]
        public LogLevel Level { get; set; } = LogLevel.Info;

        /// <summary>
        /// The name of the log target (defined in LoggerConfig.LogTargets) to write messages with this tag to.
        /// </summary>
        [JsonProperty("target")]
        public string Target { get; set; } = "default";

        /// <summary>
        /// The color to use when writing messages with this tag to a console target.
        /// </summary>
        [JsonProperty("color")]
        [JsonConverter(typeof(StringEnumConverter))]
        public ConsoleColor Color { get; set; } = ConsoleColor.Gray;
    }
    
    /// <summary>
    /// Defines the severity levels for log messages.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>Highly detailed diagnostic messages, such as instruction-level tracing.</summary>
        Trace = 0,
        /// <summary>Detailed messages for debugging purposes.</summary>
        Debug = 1,
        /// <summary>General informational messages about application flow.</summary>
        Info = 2,
        /// <summary>Indicates a potential issue that does not prevent current operation.</summary>
        Warning = 3,
        /// <summary>An error that prevents a specific operation from completing but allows the application to continue.</summary>
        Error = 4,
        /// <summary>A critical error that will likely cause the application to terminate.</summary>
        Fatal = 5
    }
}
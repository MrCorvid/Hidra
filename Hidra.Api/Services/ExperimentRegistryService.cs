// Hidra.API/Services/ExperimentRegistryService.cs
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Dapper; 
using System.Linq;

namespace Hidra.API.Services
{
    public enum ExperimentType
    {
        Standalone,
        EvolutionRun,
        GenerationOrganism
    }

    public class ExperimentMetadata
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public ExperimentType Type { get; set; }
        public string? ParentGroupId { get; set; }
        public string ActivityType { get; set; } = "Manual";
        public int GenerationIndex { get; set; } = 0;
        public float FitnessScore { get; set; } = 0.0f;
        public DateTime CreatedAt { get; set; }
    }

    public class ExperimentRegistryService : IDisposable
    {
        private readonly string _connectionString;

        public ExperimentRegistryService(string storagePath)
        {
            Directory.CreateDirectory(storagePath);
            string dbPath = Path.Combine(storagePath, "master_registry.db");
            _connectionString = $"Data Source={dbPath}";
            InitializeDb();
        }

        private void InitializeDb()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS Experiments (
                    Id TEXT PRIMARY KEY,
                    Name TEXT,
                    Type INTEGER,
                    ParentGroupId TEXT,
                    ActivityType TEXT,
                    GenerationIndex INTEGER,
                    FitnessScore REAL,
                    CreatedAt TEXT
                );
                CREATE INDEX IF NOT EXISTS IX_ParentGroup ON Experiments(ParentGroupId);
            ");
        }

        public void RegisterExperiment(ExperimentMetadata meta)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Execute(@"
                INSERT OR REPLACE INTO Experiments 
                (Id, Name, Type, ParentGroupId, ActivityType, GenerationIndex, FitnessScore, CreatedAt)
                VALUES 
                (@Id, @Name, @Type, @ParentGroupId, @ActivityType, @GenerationIndex, @FitnessScore, @CreatedAt)",
                meta);
        }

        /// <summary>
        /// Updates the display name of an experiment in the registry.
        /// </summary>
        public void UpdateName(string id, string newName)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Execute("UPDATE Experiments SET Name = @Name WHERE Id = @Id", new { Name = newName, Id = id });
        }

        public void DeleteExperiment(string id)
        {
            using var conn = new SqliteConnection(_connectionString);
            // Recursive delete (simple version: delete item and any children)
            conn.Execute("DELETE FROM Experiments WHERE Id = @Id OR ParentGroupId = @Id", new { Id = id });
        }

        public IEnumerable<ExperimentMetadata> GetAll()
        {
            using var conn = new SqliteConnection(_connectionString);
            return conn.Query<ExperimentMetadata>("SELECT * FROM Experiments ORDER BY CreatedAt DESC");
        }

        public IEnumerable<ExperimentMetadata> GetByGroup(string groupId)
        {
            using var conn = new SqliteConnection(_connectionString);
            return conn.Query<ExperimentMetadata>(
                "SELECT * FROM Experiments WHERE ParentGroupId = @GroupId ORDER BY FitnessScore DESC", 
                new { GroupId = groupId });
        }

        public ExperimentMetadata? Get(string id)
        {
            using var conn = new SqliteConnection(_connectionString);
            return conn.QuerySingleOrDefault<ExperimentMetadata>("SELECT * FROM Experiments WHERE Id = @Id", new { Id = id });
        }

        public void Dispose() { }
    }
}
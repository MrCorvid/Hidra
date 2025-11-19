// Hidra.API/Services/ExperimentDbService.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

// Explicit alias to resolve ambiguity between Hidra.Core.Logging and Microsoft.Extensions.Logging
using LogLevel = Hidra.Core.Logging.LogLevel;
using LogEntry = Hidra.Core.Logging.LogEntry;

namespace Hidra.API.Services
{
    public class ExperimentDbService : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _connection;
        private readonly object _dbLock = new();

        public ExperimentDbService(string folderPath, string experimentId)
        {
            Directory.CreateDirectory(folderPath);
            _dbPath = Path.Combine(folderPath, $"{experimentId}.db");
            var connectionString = $"Data Source={_dbPath};Pooling=False";
            _connection = new SqliteConnection(connectionString);
            _connection.Open();
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            lock (_dbLock)
            {
                using var command = _connection.CreateCommand();
                command.CommandText =
                @"
                    PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;

                    CREATE TABLE IF NOT EXISTS Meta (
                        Key TEXT PRIMARY KEY,
                        Value TEXT
                    );

                    CREATE TABLE IF NOT EXISTS Snapshots (
                        Tick INTEGER PRIMARY KEY,
                        Timestamp TEXT,
                        CompressedJson BLOB
                    );

                    CREATE TABLE IF NOT EXISTS Logs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Tick INTEGER,
                        Timestamp TEXT,
                        Level INTEGER,
                        Tag TEXT,
                        Message TEXT
                    );
                    CREATE INDEX IF NOT EXISTS IX_Logs_Tick ON Logs(Tick);
                ";
                command.ExecuteNonQuery();
            }
        }

        public void SaveMetadata(string key, string value)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "INSERT OR REPLACE INTO Meta (Key, Value) VALUES ($key, $val)";
                cmd.Parameters.AddWithValue("$key", key);
                cmd.Parameters.AddWithValue("$val", value);
                cmd.ExecuteNonQuery();
            }
        }

        public string? GetMetadata(string key)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT Value FROM Meta WHERE Key = $key";
                cmd.Parameters.AddWithValue("$key", key);
                var result = cmd.ExecuteScalar();
                return result?.ToString();
            }
        }

        public void SaveSnapshot(ulong tick, object stateData)
        {
            // Match deserialization settings (PreserveReferencesHandling) to ensure compatibility
            var json = JsonConvert.SerializeObject(stateData, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Formatting = Formatting.None,
                PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            });

            byte[] compressed = Compress(json);

            lock (_dbLock)
            {
                using var transaction = _connection.BeginTransaction();
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "INSERT OR REPLACE INTO Snapshots (Tick, Timestamp, CompressedJson) VALUES ($tick, $time, $blob)";
                cmd.Parameters.AddWithValue("$tick", tick);
                cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("$blob", compressed);
                cmd.ExecuteNonQuery();
                transaction.Commit();
            }
        }

        public string? LoadSnapshotJson(ulong tick)
        {
            byte[]? blob = null;
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT CompressedJson FROM Snapshots WHERE Tick = $tick";
                cmd.Parameters.AddWithValue("$tick", tick);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    blob = (byte[])reader["CompressedJson"];
                }
            }
            return blob != null ? Decompress(blob) : null;
        }

        public IEnumerable<(ulong Tick, string Json)> LoadAllSnapshots()
        {
            var blobs = new List<(ulong Tick, byte[] Blob)>();

            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT Tick, CompressedJson FROM Snapshots ORDER BY Tick ASC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    ulong tick = (ulong)reader.GetInt64(0);
                    byte[] blob = (byte[])reader["CompressedJson"];
                    blobs.Add((tick, blob));
                }
            }

            foreach (var item in blobs)
            {
                yield return (item.Tick, Decompress(item.Blob));
            }
        }

        public ulong GetLatestTick()
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT MAX(Tick) FROM Snapshots";
                var result = cmd.ExecuteScalar();
                return result != DBNull.Value ? Convert.ToUInt64(result) : 0;
            }
        }

        public void WriteLogBatch(ulong currentTick, List<LogEntry> buffer)
        {
             lock (_dbLock)
            {
                using var transaction = _connection.BeginTransaction();
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "INSERT INTO Logs (Tick, Timestamp, Level, Tag, Message) VALUES ($tick, $time, $lvl, $tag, $msg)";

                var pTick = cmd.Parameters.Add("$tick", SqliteType.Integer);
                var pTime = cmd.Parameters.Add("$time", SqliteType.Text);
                var pLvl = cmd.Parameters.Add("$lvl", SqliteType.Integer);
                var pTag = cmd.Parameters.Add("$tag", SqliteType.Text);
                var pMsg = cmd.Parameters.Add("$msg", SqliteType.Text);

                foreach (var log in buffer)
                {
                    pTick.Value = currentTick; 
                    pTime.Value = log.Timestamp.ToString("o");
                    pLvl.Value = (int)log.Level;
                    pTag.Value = log.Tag;
                    pMsg.Value = log.Message;
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
        }

        public List<LogEntry> ReadLogs(int limit = 1000)
        {
             var list = new List<LogEntry>();
             lock(_dbLock)
             {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT Timestamp, Level, Tag, Message FROM Logs ORDER BY Id DESC LIMIT $limit";
                cmd.Parameters.AddWithValue("$limit", limit);
                using var reader = cmd.ExecuteReader();
                while(reader.Read())
                {
                    var ts = DateTime.Parse(reader.GetString(0));
                    var lvl = (LogLevel)reader.GetInt32(1);
                    var tag = reader.GetString(2);
                    var msg = reader.GetString(3);
                    list.Add(new LogEntry(ts, lvl, tag, msg));
                }
             }
             list.Reverse();
             return list;
        }

        /// <summary>
        /// Forces a Checkpoint of the WAL file, moving data into the main DB file.
        /// This is critical before copying the DB file.
        /// </summary>
        public void Checkpoint()
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                // TRUNCATE ensures all data in the WAL is moved to the main DB file,
                // and the WAL file is truncated to zero length.
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Creates a copy of the current database at the destination path, 
        /// then connects to that copy to prune any data after the specified tick.
        /// </summary>
        public void CloneAndPruneTo(string destinationPath, ulong maxTick, string newName)
        {
            // 1. Ensure source is flushed to disk
            Checkpoint();

            // 2. Perform file copy
            // We assume the destination folder exists (handled by manager).
            File.Copy(_dbPath, destinationPath, overwrite: true);
            
            // Clean up any potential sidecar files at destination just in case
            string destWal = destinationPath + "-wal";
            string destShm = destinationPath + "-shm";
            if (File.Exists(destWal)) File.Delete(destWal);
            if (File.Exists(destShm)) File.Delete(destShm);

            // 3. Connect to the NEW database to prune history
            var connStr = $"Data Source={destinationPath};Pooling=False";
            using var destConn = new SqliteConnection(connStr);
            destConn.Open();

            using var transaction = destConn.BeginTransaction();
            using var cmd = destConn.CreateCommand();
            cmd.Transaction = transaction;

            // A. Prune Snapshots after maxTick
            cmd.CommandText = "DELETE FROM Snapshots WHERE Tick > $tick";
            cmd.Parameters.AddWithValue("$tick", maxTick);
            cmd.ExecuteNonQuery();

            // B. Prune Logs after maxTick
            cmd.CommandText = "DELETE FROM Logs WHERE Tick > $tick";
            cmd.ExecuteNonQuery();

            // C. Update Metadata Name
            cmd.CommandText = "INSERT OR REPLACE INTO Meta (Key, Value) VALUES ('Name', $name)";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$name", newName);
            cmd.ExecuteNonQuery();

            transaction.Commit();
            
            destConn.Close();
        }

        private static byte[] Compress(string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);
            using var msi = new MemoryStream(bytes);
            using var mso = new MemoryStream();
            using (var gs = new GZipStream(mso, CompressionMode.Compress))
            {
                msi.CopyTo(gs);
            }
            return mso.ToArray();
        }

        private static string Decompress(byte[] bytes)
        {
            using var msi = new MemoryStream(bytes);
            using var mso = new MemoryStream();
            using (var gs = new GZipStream(msi, CompressionMode.Decompress))
            {
                gs.CopyTo(mso);
            }
            return Encoding.UTF8.GetString(mso.ToArray());
        }

        public void Dispose()
        {
            _connection.Close();
            _connection.Dispose();
        }
    }
}
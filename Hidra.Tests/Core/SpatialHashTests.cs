// Hidra.Core/World/SpatialHash.cs
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hidra.Core
{
    /// <summary>
    /// A data structure for efficiently querying objects in a 3D space based on proximity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THREAD-SAFETY WARNING:</b> This class is <b>NOT THREAD-SAFE</b> by design to maximize performance.
    /// All write operations (<see cref="Insert"/>, <see cref="Clear"/>) must be synchronized externally.
    /// It is designed to be cleared and rebuilt from a single thread during each simulation step.
    /// Read operations (<see cref="FindNeighbors"/>) are safe to perform from multiple threads
    /// ONLY IF no writes are occurring concurrently.
    /// </para>
    /// <para>
    /// It uses a simple object pool for its internal <c>CellEntry</c> objects to reduce
    /// garbage collection pressure during the frequent Clear/Insert cycles.
    /// </para>
    /// </remarks>
    public class SpatialHash
    {
        // Internal linked-list entry for handling multiple neurons in the same cell.
        private class CellEntry
        {
            public Neuron? Neuron;
            public CellEntry? Next;
        }

        // ROBUST: Key cells by exact integer coordinates (no collisions).
        private readonly Dictionary<(int x, int y, int z), CellEntry> _cells = new();

        private readonly float _cellSize;
        private readonly float _inverseCellSize;

        // A simple memory pool for CellEntry objects to reduce GC pressure.
        private readonly List<CellEntry> _entryPool;
        private int _poolIndex = 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="SpatialHash"/> class.
        /// </summary>
        /// <param name="cellSize">The size of each grid cell. Should be based on the typical query radius.</param>
        /// <param name="initialPoolCapacity">The initial number of CellEntry objects to pre-allocate.</param>
        public SpatialHash(float cellSize, int initialPoolCapacity = 4096)
        {
            _cellSize = cellSize;
            _inverseCellSize = 1.0f / cellSize;

            // Pre-allocate the pool to avoid list resizing during insertion.
            _entryPool = new List<CellEntry>(initialPoolCapacity);
            for (int i = 0; i < initialPoolCapacity; i++)
            {
                _entryPool.Add(new CellEntry());
            }
        }

        /// <summary>
        /// Clears the spatial hash, returning all pooled entries to the pool for reuse.
        /// This is an O(1) operation (not counting dictionary clear time).
        /// </summary>
        public void Clear()
        {
            _cells.Clear();

            // Clean up only the entries we used in this build for GC-friendliness.
            for (int i = 0; i < _poolIndex; i++)
            {
                var e = _entryPool[i];
                e.Neuron = null;
                e.Next = null;
            }

            _poolIndex = 0; // Reset the pool index, effectively "freeing" all entries.
        }

        /// <summary>
        /// Inserts a neuron into the spatial hash.
        /// </summary>
        /// <param name="neuron">The neuron to insert.</param>
        public void Insert(Neuron neuron)
        {
            if (neuron == null) return;

            // Compute cell coordinates using floor to handle negatives correctly.
            var p = neuron.Position;
            int ix = (int)MathF.Floor(p.X * _inverseCellSize);
            int iy = (int)MathF.Floor(p.Y * _inverseCellSize);
            int iz = (int)MathF.Floor(p.Z * _inverseCellSize);
            var key = (ix, iy, iz);

            // Get a pre-allocated entry from the pool; grow on demand.
            CellEntry entry;
            if (_poolIndex < _entryPool.Count)
            {
                entry = _entryPool[_poolIndex++];
            }
            else
            {
                entry = new CellEntry();
                _entryPool.Add(entry);
                _poolIndex++;
            }

            entry.Neuron = neuron;

            // Push-front into the linked list for this cell.
            if (_cells.TryGetValue(key, out var head))
            {
                entry.Next = head;
                _cells[key] = entry;
            }
            else
            {
                entry.Next = null;
                _cells[key] = entry;
            }
        }

        /// <summary>
        /// Finds all neurons within a given radius of a central neuron.
        /// </summary>
        /// <param name="neuron">The neuron at the center of the search area.</param>
        /// <param name="radius">The search radius.</param>
        /// <returns>An enumerable collection of neighboring neurons.</returns>
        public IEnumerable<Neuron> FindNeighbors(Neuron neuron, float radius)
        {
            if (neuron == null) yield break;
            if (radius <= 0f) yield break;

            var center = neuron.Position;
            float r2 = radius * radius;

            // Inclusive bounds using floor on both ends to correctly span negatives.
            int minX = (int)MathF.Floor((center.X - radius) * _inverseCellSize);
            int maxX = (int)MathF.Floor((center.X + radius) * _inverseCellSize);
            int minY = (int)MathF.Floor((center.Y - radius) * _inverseCellSize);
            int maxY = (int)MathF.Floor((center.Y + radius) * _inverseCellSize);
            int minZ = (int)MathF.Floor((center.Z - radius) * _inverseCellSize);
            int maxZ = (int)MathF.Floor((center.Z + radius) * _inverseCellSize);

            // Defensive: avoid duplicate yields even if a caller double-inserts the same neuron.
            var emitted = new HashSet<ulong>();

            for (int ix = minX; ix <= maxX; ix++)
            {
                for (int iy = minY; iy <= maxY; iy++)
                {
                    for (int iz = minZ; iz <= maxZ; iz++)
                    {
                        if (_cells.TryGetValue((ix, iy, iz), out var entry))
                        {
                            while (entry != null)
                            {
                                var candidate = entry.Neuron;
                                if (candidate != null && candidate.Id != neuron.Id)
                                {
                                    // Precise Euclidean check; include points exactly on the radius.
                                    if (Vector3.DistanceSquared(center, candidate.Position) <= r2)
                                    {
                                        if (emitted.Add(candidate.Id))
                                            yield return candidate;
                                    }
                                }
                                entry = entry.Next;
                            }
                        }
                    }
                }
            }
        }
    }
}

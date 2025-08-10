// Hidra.Core/World/SpatialHash.cs
namespace Hidra.Core;

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hidra.Core.Logging;

/// <summary>
/// A data structure for efficiently querying objects in a 3D space based on proximity.
/// </summary>
/// <remarks>
/// <para>
/// **THREAD-SAFETY WARNING:** This class is **NOT THREAD-SAFE** by design to maximize performance.
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

    private readonly Dictionary<int, CellEntry> _cells = new();
    private readonly float _cellSize;
    private readonly float _inverseCellSize;

    // A simple memory pool for CellEntry objects to reduce GC pressure.
    private readonly List<CellEntry> _entryPool;
    private int _poolIndex;

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
        for (var i = 0; i < initialPoolCapacity; i++)
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
        _poolIndex = 0; // Reset the pool index, effectively "freeing" all entries.
    }

    /// <summary>
    /// Inserts a neuron into the spatial hash.
    /// </summary>
    /// <param name="neuron">The neuron to insert. Must not be null.</param>
    public void Insert(Neuron neuron)
    {
        int ix = (int)Math.Floor(neuron.Position.X * _inverseCellSize);
        int iy = (int)Math.Floor(neuron.Position.Y * _inverseCellSize);
        int iz = (int)Math.Floor(neuron.Position.Z * _inverseCellSize);
        var hash = GetCellHash(ix, iy, iz);

        // Get a pre-allocated entry from the pool.
        if (_poolIndex >= _entryPool.Count)
        {
            // The pool is exhausted; grow it to accommodate more entries.
            _entryPool.Add(new CellEntry());
        }
        var entry = _entryPool[_poolIndex++];
        entry.Neuron = neuron;
        
        // Insert at the head of the linked list for this cell hash.
        if (_cells.TryGetValue(hash, out var head))
        {
            entry.Next = head;
        }
        else
        {
            entry.Next = null;
        }
        _cells[hash] = entry;
    }

    /// <summary>
    /// Finds all neurons within a given radius of a central neuron.
    /// </summary>
    /// <param name="neuron">The neuron at the center of the search area.</param>
    /// <param name="radius">The search radius.</param>
    /// <returns>An enumerable collection of neighboring neurons.</returns>
    public IEnumerable<Neuron> FindNeighbors(Neuron neuron, float radius)
    {
        var radiusSq = radius * radius;
        var position = neuron.Position;

        int minX = (int)Math.Floor((position.X - radius) * _inverseCellSize);
        int maxX = (int)Math.Floor((position.X + radius) * _inverseCellSize);
        int minY = (int)Math.Floor((position.Y - radius) * _inverseCellSize);
        int maxY = (int)Math.Floor((position.Y + radius) * _inverseCellSize);
        int minZ = (int)Math.Floor((position.Z - radius) * _inverseCellSize);
        int maxZ = (int)Math.Floor((position.Z + radius) * _inverseCellSize);

        for (int ix = minX; ix <= maxX; ix++)
        {
            for (int iy = minY; iy <= maxY; iy++)
            {
                for (int iz = minZ; iz <= maxZ; iz++)
                {
                    var hash = GetCellHash(ix, iy, iz);
                    if (_cells.TryGetValue(hash, out var entry))
                    {
                        while (entry != null && entry.Neuron != null)
                        {
                            // Check for self-collision and perform a precise distance check.
                            if (entry.Neuron.Id != neuron.Id && Vector3.DistanceSquared(position, entry.Neuron.Position) <= radiusSq)
                            {
                                yield return entry.Neuron;
                            }
                            entry = entry.Next;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// A spatial hashing function to map a 3D grid cell coordinate to a single integer hash.
    /// </summary>
    /// <remarks>Uses large prime numbers to help distribute hash values and reduce collisions.</remarks>
    private static int GetCellHash(int ix, int iy, int iz)
    {
        // Constants are large prime numbers to provide a good distribution of hash values.
        const int p1 = 73856093;
        const int p2 = 19349663;
        const int p3 = 83492791;
        return (ix * p1) ^ (iy * p2) ^ (iz * p3);
    }
}
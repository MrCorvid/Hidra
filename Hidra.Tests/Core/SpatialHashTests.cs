// Hidra.Tests/Core/SpatialHashTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using System.Numerics;
using System.Linq;

namespace Hidra.Tests.Core
{
    /// <summary>
    /// Contains unit tests for the SpatialHash class, focusing on insertion,
    /// neighbor-finding accuracy, and lifecycle management (clear/reuse).
    /// <para>
    /// [THREAD-SAFETY] These tests operate on a single thread, respecting the
    /// documented non-thread-safe design of the SpatialHash.
    /// </para>
    /// </summary>
    [TestClass]
    public class SpatialHashTests : BaseTestClass
    {
        private const float CELL_SIZE = 10.0f;
        private SpatialHash _hash = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            // Use a small initial capacity to test pool growth.
            _hash = new SpatialHash(CELL_SIZE, initialPoolCapacity: 2);
        }

        /// <summary>
        /// Verifies that FindNeighbors correctly identifies neurons within the specified
        /// radius, includes those on the boundary, and excludes those outside.
        /// </summary>
        [TestMethod]
        public void FindNeighbors_ReturnsCorrectNeuronsWithinRadius()
        {
            // Arrange
            var centerNeuron = new Neuron { Id = 1, Position = new Vector3(5, 5, 5) };
            var insideNeuron = new Neuron { Id = 2, Position = new Vector3(10, 5, 5) }; // dist = 5
            var onEdgeNeuron = new Neuron { Id = 3, Position = new Vector3(11, 5, 5) }; // dist = 6
            var outsideNeuron = new Neuron { Id = 4, Position = new Vector3(12, 5, 5) };// dist = 7

            _hash.Insert(centerNeuron);
            _hash.Insert(insideNeuron);
            _hash.Insert(onEdgeNeuron);
            _hash.Insert(outsideNeuron);

            // Act
            var neighbors = _hash.FindNeighbors(centerNeuron, radius: 6.0f).ToList();

            // Assert
            Assert.AreEqual(2, neighbors.Count, "Should find two neighbors.");
            Assert.IsTrue(neighbors.Any(n => n.Id == insideNeuron.Id), "Should include the neuron clearly inside the radius.");
            Assert.IsTrue(neighbors.Any(n => n.Id == onEdgeNeuron.Id), "Should include the neuron exactly on the radius boundary.");
            Assert.IsFalse(neighbors.Any(n => n.Id == outsideNeuron.Id), "Should exclude the neuron outside the radius.");
            Assert.IsFalse(neighbors.Any(n => n.Id == centerNeuron.Id), "Should not include the source neuron in its own neighbor list.");
        }

        /// <summary>
        /// Verifies that FindNeighbors returns an empty collection when no other
        /// neurons are within the query radius.
        /// </summary>
        [TestMethod]
        public void FindNeighbors_WithNoNeuronsNearby_ReturnsEmpty()
        {
            // Arrange
            var centerNeuron = new Neuron { Id = 1, Position = Vector3.Zero };
            var farNeuron = new Neuron { Id = 2, Position = new Vector3(100, 100, 100) };
            _hash.Insert(centerNeuron);
            _hash.Insert(farNeuron);

            // Act
            var neighbors = _hash.FindNeighbors(centerNeuron, radius: 50.0f).ToList();

            // Assert
            Assert.AreEqual(0, neighbors.Count);
        }

        /// <summary>
        /// Verifies that the Clear method effectively empties the hash,
        /// ensuring that subsequent queries find no results.
        /// </summary>
        [TestMethod]
        public void Clear_ResetsTheHash_AllowingReuse()
        {
            // Arrange
            var neuron1 = new Neuron { Id = 1, Position = Vector3.Zero };
            var neuron2 = new Neuron { Id = 2, Position = Vector3.One };
            _hash.Insert(neuron1);
            _hash.Insert(neuron2);

            // Sanity check: ensure neurons are found before clearing.
            Assert.AreEqual(1, _hash.FindNeighbors(neuron1, 5.0f).Count());

            // Act
            _hash.Clear();

            // Assert
            // Querying for the same neuron should now yield no results.
            Assert.AreEqual(0, _hash.FindNeighbors(neuron1, 5.0f).Count(), "Hash should be empty after Clear().");
        }

        /// <summary>
        /// Verifies that inserting more neurons than the initial pool capacity
        /// correctly grows the internal pool without causing errors.
        /// </summary>
        [TestMethod]
        public void Insert_WithMoreThanInitialCapacity_GrowsPoolAndSucceeds()
        {
            // Arrange
            // The hash was initialized with a capacity of 2. We'll add 3.
            var neuron1 = new Neuron { Id = 1, Position = new Vector3(1, 1, 1) };
            var neuron2 = new Neuron { Id = 2, Position = new Vector3(2, 2, 2) };
            var neuron3 = new Neuron { Id = 3, Position = new Vector3(3, 3, 3) };

            // Act & Assert
            // The test succeeds if no exception is thrown during insertion.
            try
            {
                _hash.Insert(neuron1);
                _hash.Insert(neuron2);
                _hash.Insert(neuron3); // This insertion exceeds initial capacity.
            }
            catch (System.Exception ex)
            {
                Assert.Fail($"Expected no exception, but got: {ex.Message}");
            }
        }
    }
}
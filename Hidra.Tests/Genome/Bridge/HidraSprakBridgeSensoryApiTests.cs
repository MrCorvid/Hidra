// Hidra.Tests/Genome/Bridge/HidraSprakBridgeSensoryApiTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hidra.Core;
using System.Numerics;
using System.Linq;

namespace Hidra.Tests.Core.Genome
{
    [TestClass]
    public class HidraSprakBridgeSensoryApiTests : BaseTestClass
    {
        private HidraConfig _config = null!;
        private HidraWorld _world = null!;
        private Neuron _selfNeuron = null!;

        [TestInitialize]
        public override void BaseInit()
        {
            base.BaseInit();
            _config = TestDefaults.DeterministicConfig();
            // Set a predictable radius for tests involving nearest neighbors.
            _config.CompetitionRadius = 100f;
            _world = CreateWorld(_config);

            // Create the central neuron for all sensory tests.
            _selfNeuron = _world.AddNeuron(new Vector3(0, 0, 0));

            // IMPORTANT: The world may seed an initial neuron on creation.
            // Move any non-self neurons extremely far away so they cannot be counted in these tests.
            MoveAwayBaselineNeurons();
        }

        private void MoveAwayBaselineNeurons()
        {
            // Push any pre-existing, non-self neurons out of any realistic query radius.
            foreach (var n in _world.Neurons.Values.Where(n => n.Id != _selfNeuron.Id))
            {
                n.Position = new Vector3(1_000_000f, 1_000_000f, 1_000_000f);
            }
        }

        #region API_GetNeighborCount Tests

        [TestMethod]
        public void API_GetNeighborCount_WhenNeighborsExistInRadius_ReturnsCorrectCount()
        {
            // --- ARRANGE ---
            _world.AddNeuron(new Vector3(10, 0, 0)); // Inside radius 50
            _world.AddNeuron(new Vector3(0, 20, 0)); // Inside radius 50
            _world.AddNeuron(new Vector3(60, 0, 0)); // Outside radius 50
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);

            // --- ACT ---
            float count = bridge.API_GetNeighborCount(50f);

            // --- ASSERT ---
            AreClose(2f, count, message: "Should only count the two neurons within the specified radius.");
        }

        [TestMethod]
        public void API_GetNeighborCount_WithZeroRadius_ReturnsZero()
        {
            // --- ARRANGE ---
            _world.AddNeuron(new Vector3(1, 0, 0));
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);

            // --- ACT ---
            float count = bridge.API_GetNeighborCount(0f);

            // --- ASSERT ---
            IsZero(count, message: "A zero-radius search should find no neighbors.");
        }

        [TestMethod]
        public void API_GetNeighborCount_WhenSelfIsNull_ReturnsZero()
        {
            // --- ARRANGE ---
            // Create a bridge with a null 'self' context.
            var bridge = new HidraSprakBridge(_world, null, Hidra.Core.ExecutionContext.General);
            _world.AddNeuron(new Vector3(1, 1, 1));

            // --- ACT ---
            float count = bridge.API_GetNeighborCount(100f);
            
            // --- ASSERT ---
            IsZero(count, message: "Should return zero if the 'self' neuron is null.");
        }
        
        [TestMethod]
        public void API_GetNeighborCount_WhenNoNeighborsExist_ReturnsZero()
        {
            // --- ARRANGE ---
            // The world only contains _selfNeuron (others have been moved far away).
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);

            // --- ACT ---
            float count = bridge.API_GetNeighborCount(100f);

            // --- ASSERT ---
            IsZero(count, message: "Should return zero when no other neurons exist.");
        }

        #endregion

        #region API_GetNearestNeighborId Tests

        [TestMethod]
        public void API_GetNearestNeighborId_WhenNeighborsExist_ReturnsIdOfClosest()
        {
            // --- ARRANGE ---
            var farNeuron = _world.AddNeuron(new Vector3(50, 0, 0));
            var closestNeuron = _world.AddNeuron(new Vector3(10, 0, 0));
            var mediumNeuron = _world.AddNeuron(new Vector3(-20, 0, 0));
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);

            // --- ACT ---
            float nearestId = bridge.API_GetNearestNeighborId();

            // --- ASSERT ---
            AreClose((float)closestNeuron.Id, nearestId, message: "The ID of the mathematically closest neuron should be returned.");
        }
        
        [TestMethod]
        public void API_GetNearestNeighborId_WhenNoNeighborsInRadius_ReturnsZero()
        {
            // --- ARRANGE ---
            // Set a small radius that excludes the other neuron.
            _world.Config.CompetitionRadius = 5f;
            _world.AddNeuron(new Vector3(10, 0, 0));
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);

            // --- ACT ---
            float nearestId = bridge.API_GetNearestNeighborId();

            // --- ASSERT ---
            IsZero(nearestId, message: "Should return 0 if no neighbors are found within the competition radius.");
        }

        [TestMethod]
        public void API_GetNearestNeighborId_WhenSelfIsNull_ReturnsZero()
        {
            // --- ARRANGE ---
            var bridge = new HidraSprakBridge(_world, null, Hidra.Core.ExecutionContext.General);
            
            // --- ACT ---
            float nearestId = bridge.API_GetNearestNeighborId();
            
            // --- ASSERT ---
            IsZero(nearestId, message: "Should return 0 if 'self' is null.");
        }

        #endregion

        #region API_GetNearestNeighborPosition Tests

        [TestMethod]
        public void API_GetNearestNeighborPosition_ReturnsCorrectComponents()
        {
            // --- ARRANGE ---
            var nearestNeighbor = _world.AddNeuron(new Vector3(11f, -22f, 33f));
            _world.AddNeuron(new Vector3(50f, 50f, 50f)); // A distractor neuron
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);

            // --- ACT ---
            float posX = bridge.API_GetNearestNeighborPosition(0f);
            float posY = bridge.API_GetNearestNeighborPosition(1f);
            float posZ = bridge.API_GetNearestNeighborPosition(2f);
            float posInvalid = bridge.API_GetNearestNeighborPosition(99f);

            // --- ASSERT ---
            AreClose(11f, posX, message: "X-component should match the nearest neighbor's position.");
            AreClose(-22f, posY, message: "Y-component should match the nearest neighbor's position.");
            AreClose(33f, posZ, message: "Z-component should match the nearest neighbor's position.");
            IsZero(posInvalid, message: "An invalid axis should return 0.");
        }

        [TestMethod]
        public void API_GetNearestNeighborPosition_WhenNoNeighborsExist_ReturnsZeroForAllAxes()
        {
            // --- ARRANGE ---
            // The world only contains _selfNeuron (others have been moved far away).
            var bridge = new HidraSprakBridge(_world, _selfNeuron, Hidra.Core.ExecutionContext.General);

            // --- ACT ---
            float posX = bridge.API_GetNearestNeighborPosition(0f);
            float posY = bridge.API_GetNearestNeighborPosition(1f);
            float posZ = bridge.API_GetNearestNeighborPosition(2f);

            // --- ASSERT ---
            IsZero(posX);
            IsZero(posY);
            IsZero(posZ);
        }

        #endregion
    }
}

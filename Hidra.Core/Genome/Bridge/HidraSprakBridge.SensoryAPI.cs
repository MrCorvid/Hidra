// Hidra.Core/Genome/Bridge/HidraSprakBridge.SensoryAPI.cs
using ProgrammingLanguageNr1;
using System.Linq;

namespace Hidra.Core
{
    public partial class HidraSprakBridge
    {
        #region Sensory API

        [SprakAPI("Gets the number of other active neurons within a given radius of 'self'.", "radius")]
        public float API_GetNeighborCount(float radius)
        {
            if (_self == null || radius <= 0)
            {
                return 0f;
            }
            
            // GetNeighbors is an efficient call that uses the world's SpatialHash.
            // .Count() is an optimized method for collections that implement ICollection<T>.
            return _world.GetNeighbors(_self, radius).Count();
        }

        [SprakAPI("Gets the ID of the nearest active neuron. Searches within the world's default competition radius.")]
        public float API_GetNearestNeighborId()
        {
            var nearest = FindNearestNeighbor();
            return (float)(nearest?.Id ?? 0);
        }

        [SprakAPI("Gets a component of the nearest active neighbor's position.", "axis (0=X, 1=Y, 2=Z)")]
        public float API_GetNearestNeighborPosition(float axis)
        {
            var nearest = FindNearestNeighbor();
            if (nearest == null)
            {
                return 0f;
            }

            return ((int)axis) switch
            {
                0 => nearest.Position.X,
                1 => nearest.Position.Y,
                2 => nearest.Position.Z,
                _ => 0f,
            };
        }
        
        #endregion
    }
}
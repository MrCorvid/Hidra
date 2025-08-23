// Hidra.Core/Genome/Bridge/HidraSprakBridge.SensoryAPI.cs
using ProgrammingLanguageNr1;
using System.Linq;
using System.Numerics;

namespace Hidra.Core
{
    public partial class HidraSprakBridge
    {
        #region Sensory API

        [SprakAPI("Gets the number of other active neurons within a given radius of 'self'.", "radius")]
        public float API_GetNeighborCount(float radius)
        {
            LogDbg("BRIDGE.SENSE", $"API_GetNeighborCount(radius={radius})");
            if (_self == null || radius <= 0f)
            {
                LogWarn("BRIDGE.SENSE", "No self or non-positive radius; returning 0.");
                return 0f;
            }

            var count = _world.GetNeighbors(_self, radius).Count();
            LogDbg("BRIDGE.SENSE", $"NeighborCount -> {count}");
            return count;
        }

        [SprakAPI("Returns the id of the nearest active neighbor within the world's competition radius. Returns 0 if none found.", "")]
        public float API_GetNearestNeighborId()
        {
            LogDbg("BRIDGE.SENSE", "API_GetNearestNeighborId()");
            var nearest = FindNearestNeighbor();
            var idf = (float)(nearest?.Id ?? 0UL);
            LogDbg("BRIDGE.SENSE", $"NearestNeighborId -> {idf}");
            return idf;
        }

        [SprakAPI("Returns X/Y/Z (0/1/2) of the nearest active neighbor within the world's competition radius. Returns 0 if none found or axis invalid.", "axis(0=x,1=y,2=z)")]
        public float API_GetNearestNeighborPosition(float axis)
        {
            LogDbg("BRIDGE.SENSE", $"API_GetNearestNeighborPosition(axis={axis})");
            var ax = (int)axis;
            if (ax < 0 || ax > 2)
            {
                LogWarn("BRIDGE.SENSE", $"Axis {ax} invalid; returning 0.");
                return 0f;
            }

            var nearest = FindNearestNeighbor();
            if (nearest == null)
            {
                LogDbg("BRIDGE.SENSE", "No nearest neighbor; returning 0.");
                return 0f;
            }
            
            var result = ax switch
            {
                0 => nearest.Position.X,
                1 => nearest.Position.Y,
                2 => nearest.Position.Z,
                _ => 0f,
            };
            LogDbg("BRIDGE.SENSE", $"NearestNeighborPosition[{ax}] -> {result}");
            return result;
        }

        #endregion
    }
}
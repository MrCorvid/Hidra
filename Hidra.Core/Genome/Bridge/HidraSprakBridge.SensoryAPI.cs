// Hidra.Core/Genome/Bridge/HidraSprakBridge.SensoryAPI.cs
namespace Hidra.Core;

using System.Linq;
using ProgrammingLanguageNr1;

public partial class HidraSprakBridge
{
    [SprakAPI("Gets the number of other active neurons within a given radius of 'self'.", "radius")]
    public float API_GetNeighborCount(float radius)
    {
        if (_self == null || radius <= 0)
        {
            return 0f;
        }
        
        return _world.GetNeighbors(_self, radius).Count();
    }

    [SprakAPI("Gets the ID of the nearest active neuron. Searches within the world's default competition radius.")]
    public float API_GetNearestNeighborId() => (float)(FindNearestNeighbor()?.Id ?? 0);

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
}
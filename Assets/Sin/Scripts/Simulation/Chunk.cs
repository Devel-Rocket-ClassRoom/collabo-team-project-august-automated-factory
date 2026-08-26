using System.Collections.Generic;
using UnityEngine;

namespace Factory.Simulation
{
    public sealed class Chunk
    {
        public readonly Vector2Int Coord;
        public readonly HashSet<int> SegmentIds = new HashSet<int>();
        public readonly HashSet<int> BuildingIds = new HashSet<int>();

        public Chunk(Vector2Int coord)
        {
            Coord = coord;
        }
    }
}

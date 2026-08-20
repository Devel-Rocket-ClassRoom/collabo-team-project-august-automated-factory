using System.Collections.Generic;
using UnityEngine;

namespace Factory.Simulation
{
    public enum CellOccupantType
    {
        Belt,
        Miner,
        Processor,
    }

    public readonly struct CellOccupant
    {
        public readonly CellOccupantType Type;
        public readonly int InstanceIndex;

        public CellOccupant(CellOccupantType type, int instanceIndex)
        {
            Type = type;
            InstanceIndex = instanceIndex;
        }
    }

    // 월드를 고정 크기 청크로 나눠 관리하고, 셀 단위 점유 정보(무엇이 놓여 있는지)를 들고 있는다.
    // 시뮬레이션 계산 자체는 전체 청크에 대해 계속 돈다 (오프라인 진행 계산이 결국 안 보이던
    // 시간의 시뮬레이션을 재현해야 하므로 화면 밖이라고 계산을 끄면 안 된다). 컬링은 렌더링
    // 단계에서 "화면에 걸치는 청크만 갱신"하는 방식으로 적용한다 — 이번 패스는 가시 청크
    // 판정까지만 구현하고, 풀링 최적화는 이후 과제로 남긴다.
    public sealed class WorldGrid
    {
        public const int ChunkSize = 16;

        private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
        private readonly Dictionary<Vector2Int, CellOccupant> occupants = new Dictionary<Vector2Int, CellOccupant>();

        public bool IsOccupied(Vector2Int cell) => occupants.ContainsKey(cell);
        public bool TryGetOccupant(Vector2Int cell, out CellOccupant occupant) => occupants.TryGetValue(cell, out occupant);

        public static Vector2Int WorldCellToChunkCoord(Vector2Int cell)
        {
            return new Vector2Int(FloorDiv(cell.x, ChunkSize), FloorDiv(cell.y, ChunkSize));
        }

        public Chunk GetOrCreateChunk(Vector2Int chunkCoord)
        {
            if (!chunks.TryGetValue(chunkCoord, out var chunk))
            {
                chunk = new Chunk(chunkCoord);
                chunks[chunkCoord] = chunk;
            }
            return chunk;
        }

        public void RegisterSegment(Vector2Int cell, int segmentId)
        {
            GetOrCreateChunk(WorldCellToChunkCoord(cell)).SegmentIds.Add(segmentId);
            occupants[cell] = new CellOccupant(CellOccupantType.Belt, segmentId);
        }

        public void RegisterBuilding(Vector2Int cell, CellOccupantType type, int instanceIndex)
        {
            GetOrCreateChunk(WorldCellToChunkCoord(cell)).BuildingIds.Add(instanceIndex);
            occupants[cell] = new CellOccupant(type, instanceIndex);
        }

        // 카메라 시야(월드 셀 기준 사각형)와 겹치는 청크만 반환 — 렌더 갱신 대상 산정용.
        public IEnumerable<Chunk> GetVisibleChunks(RectInt viewCellBounds)
        {
            var minChunk = WorldCellToChunkCoord(new Vector2Int(viewCellBounds.xMin, viewCellBounds.yMin));
            var maxChunk = WorldCellToChunkCoord(new Vector2Int(viewCellBounds.xMax, viewCellBounds.yMax));

            for (int x = minChunk.x; x <= maxChunk.x; x++)
            {
                for (int y = minChunk.y; y <= maxChunk.y; y++)
                {
                    if (chunks.TryGetValue(new Vector2Int(x, y), out var chunk))
                    {
                        yield return chunk;
                    }
                }
            }
        }

        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if (a % b != 0 && (a < 0) != (b < 0)) q--;
            return q;
        }
    }
}

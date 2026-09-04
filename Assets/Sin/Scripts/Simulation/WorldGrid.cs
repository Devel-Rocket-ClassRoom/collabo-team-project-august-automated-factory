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

        // 건물 점유(occupants)와는 별개인 "땅" 레이어 — 채굴기는 광물 노드 위에 건물로 올라가야
        // 하므로 두 레이어가 같은 칸에 공존해야 한다(occupants에 넣으면 그 칸이 "점유됨"으로
        // 잡혀서 정작 채굴기를 못 짓게 된다).
        private readonly Dictionary<Vector2Int, int> oreDepositRuntimeIdByCell = new Dictionary<Vector2Int, int>();

        public bool IsOccupied(Vector2Int cell) => occupants.ContainsKey(cell);
        public bool TryGetOccupant(Vector2Int cell, out CellOccupant occupant) => occupants.TryGetValue(cell, out occupant);

        // 역방향 조회: (타입, 인덱스)가 차지한 칸을 찾는다. 벨트 세그먼트는 정확히 한 칸만
        // 차지하므로 첫 번째 일치를 반환한다(멀티칸 건물엔 쓰지 않는다). 벨트를 재배선한 뒤
        // 그 세그먼트의 스트립을 실제 흐름 방향으로 다시 그릴 때 쓴다(BeltDragTool).
        public bool TryGetCellOf(CellOccupantType type, int instanceIndex, out Vector2Int cell)
        {
            foreach (var kvp in occupants)
            {
                if (kvp.Value.Type != type || kvp.Value.InstanceIndex != instanceIndex) continue;
                cell = kvp.Key;
                return true;
            }
            cell = default;
            return false;
        }

        public void RegisterOreDeposit(Vector2Int cell, int oreDepositRuntimeId) => oreDepositRuntimeIdByCell[cell] = oreDepositRuntimeId;
        public bool TryGetOreDeposit(Vector2Int cell, out int oreDepositRuntimeId) => oreDepositRuntimeIdByCell.TryGetValue(cell, out oreDepositRuntimeId);

        // footprint 전체 칸이 하나도 안 겹치는지(멀티칸 배치 유효성 검사용).
        public bool IsFootprintFree(IReadOnlyList<Vector2Int> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (IsOccupied(cells[i])) return false;
            }
            return true;
        }

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

        // 코어처럼 여러 칸을 차지하는 건물용 — 같은 occupant를 footprint의 모든 칸에 등록한다.
        public void RegisterBuildingFootprint(IReadOnlyList<Vector2Int> cells, CellOccupantType type, int instanceIndex)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                RegisterBuilding(cells[i], type, instanceIndex);
            }
        }

        // 철거용 — RegisterSegment/RegisterBuilding(Footprint)이 등록한 칸을 그대로 되돌린다.
        public void UnregisterCell(Vector2Int cell)
        {
            if (!occupants.TryGetValue(cell, out var occupant)) return;

            var chunk = GetOrCreateChunk(WorldCellToChunkCoord(cell));
            if (occupant.Type == CellOccupantType.Belt) chunk.SegmentIds.Remove(occupant.InstanceIndex);
            else chunk.BuildingIds.Remove(occupant.InstanceIndex);

            occupants.Remove(cell);
        }

        // 철거 대상 occupant가 차지한 칸 전부를 찾아 지운다. footprint가 몇 칸인지(멀티칸 건물)를
        // 호출자가 따로 알 필요 없게, occupants를 직접 훑어서 (type, instanceIndex)가 일치하는
        // 칸을 전부 찾는다 — 철거는 드문 조작이라 이 정도 스캔 비용은 무방하다.
        public void UnregisterOccupant(CellOccupantType type, int instanceIndex)
        {
            List<Vector2Int> matchingCells = null;
            foreach (var kvp in occupants)
            {
                if (kvp.Value.Type != type || kvp.Value.InstanceIndex != instanceIndex) continue;
                (matchingCells ??= new List<Vector2Int>()).Add(kvp.Key);
            }

            if (matchingCells == null) return;
            for (int i = 0; i < matchingCells.Count; i++) UnregisterCell(matchingCells[i]);
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

using Factory.Simulation;
using UnityEngine;

namespace Factory.Building
{
    // "이 칸이 소스/타겟/무효 중 뭔지" 판정하는 포트 규칙 모음 — Preview와 Commit
    // 양쪽에서 그대로 가져다 쓴다(따로 구현하면 어긋나는 버그가 난다).
    public partial class BeltDragTool
    {
        // 기계 포트(고정 입력/출력면), 코어(4면 다 유효), 기존 벨트(방향대로) 각각의 규칙으로
        // 이 endpoint가 소스/타겟/무효 중 뭔지 판정한다.
        // touchingCell: 드래그 경로상 기계 칸 바로 옆 칸(이게 어느 포트에 해당하는지로 역할이 정해짐).
        // isStart: 이 endpoint가 드래그 시작 쪽인지 (코어/벨트처럼 방향 의존적인 경우에만 씀).
        // isFixed: true면 포트 방향(Facing)만으로 확정된 값이라 절대 안 바뀐다(채굴기/제련로).
        // false면 코어/벨트처럼 "어느 쪽에 이어붙이느냐"로만 정해지는 값이라, 반대쪽이 고정
        // 역할을 가지고 있으면 그걸 보고 나중에 뒤집힐 수 있다(둘 다 같은 역할로 겹치는 것 방지).
        public EndpointRole ResolveEndpointRole(CellOccupant occupant, Vector2Int touchingCell, bool isStart, out bool isFixed)
        {
            switch (occupant.Type)
            {
                case CellOccupantType.Miner:
                    // 채굴기는 입출력 포트가 없다 — 캔 자원은 벨트 없이 코어로 곧장 원격 전송된다
                    // (MinerSystem 참고). 그래서 어느 면에 닿아도 벨트 연결 대상이 될 수 없다.
                    isFixed = true;
                    return EndpointRole.None;
                case CellOccupantType.Processor:
                {
                    var processor = driver.World.Processors[occupant.InstanceIndex];

                    if (processor.IsGeneratorFuelPort)
                    {
                        isFixed = true;
                        var generatorInputs = GridUtility.GetPortCells(processor.Anchor, processor.Footprint,
                            processor.Facing, isOutputSide: false);
                        return generatorInputs.Contains(touchingCell) ? EndpointRole.Target : EndpointRole.None;
                    }

                    if (processor.RoutingRole != RoutingRole.None)
                    {
                        isFixed = true;
                        // touchingCell = 노드(1x1)에 딱 붙은 벨트 칸. 방향으로 어느 면인지 판정.
                        // 분류기: -Facing(뒤)면만 입력, 나머지 3면 출력. 합류기: +Facing(앞)면만 출력, 나머지 3면 입력.
                        Vector2Int dir = touchingCell - processor.Anchor;
                        bool inputFace = processor.RoutingRole == RoutingRole.Splitter
                            ? dir == -processor.Facing
                            : dir != processor.Facing;
                        return inputFace ? EndpointRole.Target : EndpointRole.Source;
                    }

                    if (processor.UniversalPorts)
                    {
                        isFixed = false;
                        return isStart ? EndpointRole.Source : EndpointRole.Target;
                    }
                    isFixed = true;
                    // footprint가 1칸보다 클 수 있어서(예: 2x2 합성기), 밟은 칸(machineCell)이
                    // 아니라 앵커 기준으로 포트 칸 목록을 계산한다 — 어느 footprint 칸에
                    // 닿았든 앵커만 같으면 같은 결과가 나온다.
                    var outputs = GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, isOutputSide: true);
                    if (outputs.Contains(touchingCell)) return EndpointRole.Source;
                    var inputs = GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, isOutputSide: false);
                    if (inputs.Contains(touchingCell)) return EndpointRole.Target;
                    return EndpointRole.None;
                }
                case CellOccupantType.Belt:
                {
                    isFixed = false;
                    if (isStart) return EndpointRole.Source;

                    // 드래그가 기존 벨트 칸에서 끝나는 경우, 그 벨트가 아직 아무 데서도 안
                    // 먹여지고 있는 체인의 시작일 때만 새 상류를 붙일 수 있다. 이미 다른
                    // 세그먼트(또는 기계)가 먹이고 있는 벨트에 억지로 연결하면 그 벨트 고유의
                    // 방향대로 아이템이 흘러버려서, 반대 방향으로 지어진 막다른 벨트끼리
                    // 마주보고 있을 때 아이템이 반대쪽으로 순간이동한 것처럼 보이는 버그가 생긴다.
                    var targetSegment = driver.World.Segments[occupant.InstanceIndex];
                    return MachineGhostTool.IsChainStart(driver.World.Segments, targetSegment)
                        ? EndpointRole.Target
                        : EndpointRole.None;
                }
                default:
                    isFixed = false;
                    return EndpointRole.None;
            }
        }

        public static EndpointRole Opposite(EndpointRole role) => role == EndpointRole.Source ? EndpointRole.Target : EndpointRole.Source;

        private static readonly Vector2Int[] FourDirs =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
        };

        // 드래그가 기계 칸에 직접 닿지 않고 그 바로 옆(빈 칸)에서 끝나도, 그 칸에 인접한
        // 유효 포트가 있으면 자동으로 그 기계에 연결한다 — "기계까지 드래그해야만 연결된다"는
        // 제약을 없애기 위함(사용자 요청). fromDir는 경로상 바로 이전 칸 방향이라 되돌아가는
        // 방향은 후보에서 뺀다(막 지나온 칸을 다시 "인접한 기계"로 착각하지 않게).
        // Seo님의 BeltConnectionFeedback이 "빈 칸에서 시작 가능한지" 판정할 때도 이걸 그대로
        // 물어봐야 한다 — 자기 나름으로 grid.IsOccupied만 보고 판단하면, 옆칸 자동연결이
        // 가능한데도 UI만 "빈 공간이라 시작 불가"라고 잘못 보여주는 불일치가 생긴다.
        public bool TryFindAdjacentOccupant(Vector2Int cell, Vector2Int fromCell, bool isStart,
            out CellOccupant occupant, out EndpointRole role, out bool isFixed, out Vector2Int neighborCell)
        {
            var grid = driver.World.Grid;
            for (int d = 0; d < FourDirs.Length; d++)
            {
                Vector2Int neighbor = cell + FourDirs[d];
                if (neighbor == fromCell) continue;
                if (!grid.TryGetOccupant(neighbor, out var occ)) continue;
                // 기존 벨트(코너 조각 등)도 옆칸 자동연결 대상에 포함한다 — ResolveEndpointRole의
                // Belt 분기는 touchingCell 기하와 무관하게 isStart/IsChainStart만 보므로 그대로
                // 재사용해도 안전하다. 예전엔 여기서 막아놨었는데, 그러면 "끊어진 벨트를 코너에
                // 직접 안 닿고 재연결"하는 흔한 시나리오가 아예 안 통했다(사용자 보고).
                var resolved = ResolveEndpointRole(occ, cell, isStart, out bool fixedRole);
                if (resolved == EndpointRole.None) continue;

                occupant = occ;
                role = resolved;
                isFixed = fixedRole;
                neighborCell = neighbor;
                return true;
            }
            occupant = default;
            role = EndpointRole.None;
            isFixed = false;
            neighborCell = default;
            return false;
        }

        // 이 세그먼트로 흐름이 들어오는 쪽 칸: 다른 벨트가 먹이면 그 벨트 칸, 아니면 소스 기계의
        // footprint 칸 중 이 세그먼트에 딱 붙은 칸(코어처럼 Facing 없는 기계까지 포함). 방향 계산에만
        // 쓰므로 정확한 포트 칸이 아니라 "인접한 몸통 칸"이면 충분하다. Facing 기계인데 벨트가 포트에서
        // 한 칸 떨어져 있는(인접 아님) 드문 경우는 못 찾고 false — 그럼 직선으로만 다시 그린다.
        private bool TryGetUpstreamCell(BeltSegment segment, Vector2Int segmentCell, out Vector2Int cell)
        {
            var segments = driver.World.Segments;
            var grid = driver.World.Grid;
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i] == null || segments[i].NextSegmentId != segment.Id) continue;
                if (grid.TryGetCellOf(CellOccupantType.Belt, i, out cell)) return true;
            }

            if (segment.SourceProcessorId.HasValue)
            {
                var proc = driver.World.Processors[segment.SourceProcessorId.Value];
                if (proc != null)
                {
                    var body = GridUtility.GetFootprintCells(proc.Anchor, proc.Footprint);
                    for (int i = 0; i < body.Count; i++)
                    {
                        if (Mathf.Abs(body[i].x - segmentCell.x) + Mathf.Abs(body[i].y - segmentCell.y) == 1)
                        {
                            cell = body[i];
                            return true;
                        }
                    }
                }
            }

            cell = default;
            return false;
        }

        // TryGetUpstreamCell의 하류판 — 다음 벨트(NextSegmentId)로 흐르면 그 벨트 칸, 아니면
        // 곧장 먹이는 기계(TargetProcessorId)의 footprint 칸 중 이 세그먼트에 딱 붙은 칸을
        // 찾는다. 방향 계산 전용이라 정확한 포트 칸일 필요는 없다. RerenderSegmentStrip과
        // PreviewOnBuildingBeltBend가 둘 다 이걸 쓴다 — 따로 구현하면 한쪽만 체인을 챙기고
        // 한쪽은 안 챙기는 식으로 또 어긋나는 버그가 난다.
        private bool TryGetDownstreamCell(BeltSegment segment, Vector2Int segmentCell, out Vector2Int cell)
        {
            if (segment.NextSegmentId.HasValue
                && driver.World.Grid.TryGetCellOf(CellOccupantType.Belt, segment.NextSegmentId.Value, out cell))
            {
                return true;
            }

            if (segment.TargetProcessorId.HasValue)
            {
                var proc = driver.World.Processors[segment.TargetProcessorId.Value];
                if (proc != null)
                {
                    var body = GridUtility.GetFootprintCells(proc.Anchor, proc.Footprint);
                    for (int i = 0; i < body.Count; i++)
                    {
                        if (Mathf.Abs(body[i].x - segmentCell.x) + Mathf.Abs(body[i].y - segmentCell.y) == 1)
                        {
                            cell = body[i];
                            return true;
                        }
                    }
                }
            }
            cell = default;
            return false;
        }
    }
}

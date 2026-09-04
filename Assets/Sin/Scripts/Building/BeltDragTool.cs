using System.Collections.Generic;
using Factory.Rendering;
using Factory.Simulation;
using UnityEngine;

namespace Factory.Building
{
    // 프레스+드래그로 벨트 경로를 그리고(코너는 BeltPathBuilder가 자동 처리), 릴리즈 시
    // 실제 BeltSegment로 커밋한다. 채굴기/제련로는 고정된 입력면/출력면이 있어서(Facing),
    // 벨트가 정확히 그 면에 닿아야만 연결된다 — 어느 쪽이든 드래그 방향대로 연결되던
    // 예전 방식과 달리 모호함이 없다. 코어(UniversalPorts)와 기존 벨트는 예외적으로
    // 어느 쪽에 닿아도 되고, 드래그 시작/끝 위치로 소스/타겟이 갈린다.
    public class BeltDragTool : MonoBehaviour, IBuildTool
    {
        private enum EndpointRole
        {
            None,
            Source,
            Target,
        }

        [SerializeField] private Camera targetCamera;
        [SerializeField] private SimulationDriver driver;
        [SerializeField] private float previewThickness = 0.5f;
        [SerializeField] private float committedThickness = 0.6f;
        // BeltItemVisual 프리팹의 반지름(지름 0.25의 절반)과 맞춰야 한다 — 벨트 위에 아이템이
        // "위에 얹힌" 것처럼 보이려면 벨트 두께의 절반 + 이 반지름만큼 띄워야 한다.
        [SerializeField] private float itemVisualRadius = 0.125f;
        [SerializeField] private Color previewColor = new Color(0.2f, 0.9f, 0.3f, 0.5f);
        [SerializeField] private Color committedColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        [SerializeField] private GameObject itemVisualPrefab;
        [SerializeField] private GameObject stripPrefab;

        private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        private readonly List<Vector2Int> path = new List<Vector2Int>();
        private readonly List<GameObject> previewStrips = new List<GameObject>();
        private bool dragging;

        // 에디터 SerializedObject 없이(런타임/테스트에서) 직접 배선할 때 쓴다.
        public void Initialize(Camera targetCamera, SimulationDriver driver)
        {
            this.targetCamera = targetCamera;
            this.driver = driver;
        }

        public void OnPressBegin(Vector2 screenPosition)
        {
            path.Clear();
            ClearPreview();
            dragging = true;

            if (TryScreenToCell(screenPosition, out var cell))
            {
                BeltPathBuilder.Extend(path, cell);
                RebuildPreview();
            }
        }

        public void OnDrag(Vector2 screenPosition)
        {
            if (!dragging) return;
            if (!TryScreenToCell(screenPosition, out var cell)) return;

            int before = path.Count;
            BeltPathBuilder.Extend(path, cell);
            if (path.Count != before) RebuildPreview();
        }

        public void OnReleased(Vector2 screenPosition)
        {
            if (!dragging) return;
            dragging = false;
            Commit();
            path.Clear();
            ClearPreview();
        }

        public void OnCancelled()
        {
            dragging = false;
            path.Clear();
            ClearPreview();
        }

        private bool TryScreenToCell(Vector2 screenPosition, out Vector2Int cell)
        {
            cell = default;
            if (targetCamera == null) return false;

            return GridUtility.TryRaycastToCell(targetCamera.ScreenPointToRay(screenPosition), groundPlane, out cell);
        }

        private void RebuildPreview()
        {
            ClearPreview();
            for (int k = 0; k < path.Count; k++)
            {
                ComputeCellSpan(path, k, out Vector3 entry, out Vector3 exit, out Vector3? bend);
                if (bend.HasValue)
                {
                    previewStrips.Add(BuildVisuals.CreateStrip(entry, bend.Value, previewThickness, previewColor, transform, prefab: stripPrefab));
                    previewStrips.Add(BuildVisuals.CreateStrip(bend.Value, exit, previewThickness, previewColor, transform, prefab: stripPrefab));
                }
                else
                {
                    previewStrips.Add(BuildVisuals.CreateStrip(entry, exit, previewThickness, previewColor, transform, prefab: stripPrefab));
                }
            }
        }

        // 세그먼트 하나가 정확히 자기 칸 안(경계~반대쪽 경계)에 들어차도록 진입/이탈 지점을
        // 계산한다. 진입/이탈 방향이 다르면(코너) 꺾이는 지점(bend)을 반환해서 두 조각으로 나눠 그린다.
        private static void ComputeCellSpan(List<Vector2Int> path, int k, out Vector3 entry, out Vector3 exit, out Vector3? bend)
        {
            Vector3 center = GridUtility.CellToWorldCenter(path[k], 0.5f);
            Vector3? inDir = k > 0 ? DirWorld(path[k] - path[k - 1]) : (Vector3?)null;
            Vector3? outDir = k < path.Count - 1 ? DirWorld(path[k + 1] - path[k]) : (Vector3?)null;

            if (inDir.HasValue && outDir.HasValue && inDir.Value != outDir.Value)
            {
                entry = center - inDir.Value * 0.5f;
                exit = center + outDir.Value * 0.5f;
                bend = center;
                return;
            }

            Vector3 dir = inDir ?? outDir ?? Vector3.forward;
            entry = center - dir * 0.5f;
            exit = center + dir * 0.5f;
            bend = null;
        }

        private static Vector3 DirWorld(Vector2Int step) => new Vector3(step.x, 0f, step.y);

        private void ClearPreview()
        {
            for (int i = 0; i < previewStrips.Count; i++) Destroy(previewStrips[i]);
            previewStrips.Clear();
        }

        // 기계 포트(고정 입력/출력면), 코어(4면 다 유효), 기존 벨트(방향대로) 각각의 규칙으로
        // 이 endpoint가 소스/타겟/무효 중 뭔지 판정한다.
        // touchingCell: 드래그 경로상 기계 칸 바로 옆 칸(이게 어느 포트에 해당하는지로 역할이 정해짐).
        // isStart: 이 endpoint가 드래그 시작 쪽인지 (코어/벨트처럼 방향 의존적인 경우에만 씀).
        // isFixed: true면 포트 방향(Facing)만으로 확정된 값이라 절대 안 바뀐다(채굴기/제련로).
        // false면 코어/벨트처럼 "어느 쪽에 이어붙이느냐"로만 정해지는 값이라, 반대쪽이 고정
        // 역할을 가지고 있으면 그걸 보고 나중에 뒤집힐 수 있다(둘 다 같은 역할로 겹치는 것 방지).
        private EndpointRole ResolveEndpointRole(CellOccupant occupant, Vector2Int touchingCell, bool isStart, out bool isFixed)
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

        private static EndpointRole Opposite(EndpointRole role) => role == EndpointRole.Source ? EndpointRole.Target : EndpointRole.Source;

        private void Commit()
        {
            if (driver == null || driver.World == null || path.Count < 2) return;

            var grid = driver.World.Grid;
            bool startOccupied = grid.IsOccupied(path[0]);
            bool endOccupied = grid.IsOccupied(path[path.Count - 1]);

            // 벨트는 반드시 기존 포트(기계)나 벨트에서 시작해야 한다 — 손가락을 뗀 자리가
            // 어디든(방향은 나중에 뒤집힐 수 있음) 아예 허공에서 시작해서 그릴 순 없다.
            if (!startOccupied) return;

            grid.TryGetOccupant(path[0], out var startOccupant);
            grid.TryGetOccupant(path[path.Count - 1], out var endOccupant);

            bool startFixed = false, endFixed = false;
            var startRole = startOccupied ? ResolveEndpointRole(startOccupant, path[1], true, out startFixed) : EndpointRole.None;
            var endRole = endOccupied ? ResolveEndpointRole(endOccupant, path[path.Count - 2], false, out endFixed) : EndpointRole.None;

            // 점유된 칸이 있는데 유효한 포트가 아니면(기계 옆면 등) 거부 — 잘못된 연결을
            // 어설프게 만들지 않는다.
            if (startOccupied && startRole == EndpointRole.None) return;
            if (endOccupied && endRole == EndpointRole.None) return;

            // 양쪽 다 같은 역할로 겹치면(둘 다 Source거나 둘 다 Target) 보통 코어처럼 순서
            // 의존적인(고정 아님) 쪽이 반대쪽 고정 포트 방향과 어긋난 경우다 — 예: 제련로
            // 입력면 쪽에서 시작해 코어로 드래그하면, 입력면은 Target인데 코어도 (isStart가
            // 아니라는 이유만으로) Target으로 잡혀버림. 고정 포트가 아닌 쪽을 반대 역할로
            // 바로잡는다. 둘 다 고정이거나 둘 다 유동인데 겹치면 진짜로 애매하니 거부한다.
            if (startOccupied && endOccupied && startRole == endRole)
            {
                if (!startFixed && endFixed) startRole = Opposite(endRole);
                else if (startFixed && !endFixed) endRole = Opposite(startRole);
                else return;
            }

            // 소스가 끝 쪽으로 판정됐으면(예: 제련로 입력면에서 시작해 코어 쪽으로 드래그한
            // 경우) 경로를 뒤집어서 소스가 항상 앞에 오게 한다 — 세그먼트 체인은 배열 순서를
            // 그대로 흐름 순서로 쓰기 때문. 역할은 이미 위에서 확정했으니 다시 판정하지 않고
            // 그대로 맞바꾼다(다시 판정하면 위에서 바로잡은 결과가 날아감).
            if (startRole != EndpointRole.Source && endRole == EndpointRole.Source)
            {
                path.Reverse();
                (startOccupied, endOccupied) = (endOccupied, startOccupied);
                (startOccupant, endOccupant) = (endOccupant, startOccupant);
                (startRole, endRole) = (endRole, startRole);
            }

            // 시작 칸이 "이미 다른 곳으로 흐르고 있는" 기존 벨트면 여기서 거부한다 — 안 그러면
            // 그 벨트의 NextSegmentId를 조용히 새 목적지로 덮어써서, 원래 흐르던 곳과의 연결이
            // 몰래 끊기고 두 벨트가 뜻하지 않게 하나로 합쳐진다(합류 자체는 나중에 합류기로
            // 의도적으로 할 수 있어야 하니 막지 않지만, "이미 연결된 벨트를 가로채는" 건 막는다).
            if (startOccupied && startRole == EndpointRole.Source && startOccupant.Type == CellOccupantType.Belt
                && driver.World.Segments[startOccupant.InstanceIndex].NextSegmentId.HasValue)
            {
                return;
            }

            // 시작/끝 칸이 유효한 포트(기계)나 기존 벨트와 겹치면 그 칸 자체는 새 벨트 칸으로
            // 만들지 않고 대신 그 대상에 연결한다.
            var beltCells = new List<Vector2Int>(path);
            if (endOccupied) beltCells.RemoveAt(beltCells.Count - 1);
            if (startOccupied) beltCells.RemoveAt(0);

            if (beltCells.Count == 0)
            {
                // 새로 놓을 벨트 칸이 아예 없는 경우 (예: 기존 벨트 끝이 제련로 입력면 바로
                // 옆칸이라 사이에 빈 칸이 없음) — 새 세그먼트 없이 기존 것끼리 바로 연결한다.
                TryDirectLink(startOccupied, startOccupant, startRole, endOccupied, endOccupant, endRole);
                return;
            }

            for (int i = 0; i < beltCells.Count; i++)
            {
                if (grid.IsOccupied(beltCells[i])) return; // 이미 다른 벨트/기계가 있는 칸과는 겹칠 수 없음
            }

            var createdSegments = new List<BeltSegment>(beltCells.Count);
            for (int i = 0; i < beltCells.Count; i++)
            {
                createdSegments.Add(new BeltSegment { Id = driver.World.Segments.Count + i, Length = 1f });
            }

            if (startOccupied && startRole == EndpointRole.Source)
            {
                switch (startOccupant.Type)
                {
                    // Miner는 ResolveEndpointRole에서 항상 None이라 여기 Source로 들어올 수 없다.
                    case CellOccupantType.Processor:
                        createdSegments[0].SourceProcessorId = startOccupant.InstanceIndex;
                        break;
                    case CellOccupantType.Belt:
                        // 기존 벨트 끝에 이어서 놓는 경우: 그 세그먼트가 새 첫 세그먼트로 흘러들게 연결.
                        driver.World.Segments[startOccupant.InstanceIndex].NextSegmentId = createdSegments[0].Id;
                        break;
                }
            }

            if (endOccupied && endRole == EndpointRole.Target)
            {
                if (endOccupant.Type == CellOccupantType.Processor)
                {
                    createdSegments[createdSegments.Count - 1].TargetProcessorId = endOccupant.InstanceIndex;
                }
                else if (endOccupant.Type == CellOccupantType.Belt)
                {
                    // 기존 벨트 시작 쪽에 이어붙이는 경우: 새 마지막 세그먼트가 그 세그먼트로 흘러들게 연결.
                    createdSegments[createdSegments.Count - 1].NextSegmentId = endOccupant.InstanceIndex;
                }
            }

            for (int i = 0; i < createdSegments.Count - 1; i++)
            {
                createdSegments[i].NextSegmentId = createdSegments[i + 1].Id;
            }

            // beltCells[i]는 항상 path[pathOffset + i]에 대응한다 (시작 칸을 잘라냈으면 그만큼 밀림).
            int pathOffset = startOccupied ? 1 : 0;

            for (int i = 0; i < createdSegments.Count; i++)
            {
                ComputeCellSpan(path, pathOffset + i, out Vector3 entry, out Vector3 exit, out Vector3? bend);

                driver.World.AddBeltSegment(createdSegments[i]);
                grid.RegisterSegment(beltCells[i], createdSegments[i].Id);

                SpawnCommittedVisual(entry, exit, bend, createdSegments[i].Id);
            }

            // 옆에 이어붙여서 흐름이 꺾인 기존 벨트의 스트립/화살표를 실제 방향으로 다시 그린다
            // (스트립은 생성 시점 path로 한 번만 구워지므로, 재배선하면 옛 방향 그대로 남았다 — 사용자 보고).
            if (startOccupied && startRole == EndpointRole.Source && startOccupant.Type == CellOccupantType.Belt)
                RerenderSegmentStrip(startOccupant.InstanceIndex);
            if (endOccupied && endRole == EndpointRole.Target && endOccupant.Type == CellOccupantType.Belt)
                RerenderSegmentStrip(endOccupant.InstanceIndex);
        }

        // 새 벨트 칸 없이 기존 벨트를 기존 제련로/기존 벨트에 직접 연결한다 (둘이 바로 붙어있는 경우).
        // 채굴기는 최소 한 칸의 벨트가 있어야 산출물을 실을 수 있으므로 여기서는 다루지 않는다.
        private void TryDirectLink(bool startOccupied, CellOccupant startOccupant, EndpointRole startRole, bool endOccupied, CellOccupant endOccupant, EndpointRole endRole)
        {
            if (!startOccupied || startOccupant.Type != CellOccupantType.Belt || startRole != EndpointRole.Source) return;
            if (!endOccupied || endRole != EndpointRole.Target) return;

            var startSegment = driver.World.Segments[startOccupant.InstanceIndex];
            // 이미 다른 곳으로 흐르고 있는 벨트를 여기서 또 가로채면 안 된다(위 Commit()의
            // 같은 취지 가드 참고) — 안 그러면 원래 목적지와의 연결이 조용히 끊긴다.
            if (startSegment.NextSegmentId.HasValue || startSegment.TargetProcessorId.HasValue) return;

            if (endOccupant.Type == CellOccupantType.Processor)
            {
                startSegment.TargetProcessorId = endOccupant.InstanceIndex;
            }
            else if (endOccupant.Type == CellOccupantType.Belt)
            {
                startSegment.NextSegmentId = endOccupant.InstanceIndex;
                RerenderSegmentStrip(endOccupant.InstanceIndex);
            }

            RerenderSegmentStrip(startOccupant.InstanceIndex);
        }

        // 기존 벨트 세그먼트의 스트립/화살표를, 그 세그먼트의 실제 그리드 이웃(상류·하류 벨트)에
        // 맞춰 다시 그린다. 스트립은 생성 시점의 드래그 path로 한 번만 구워지므로, 나중에
        // 재배선(옆에 이어붙이기 등)으로 흐름이 꺾이면 옛 방향/화살표가 그대로 남는다.
        // 기계로만 연결된 경우(포트 칸 규약이 배치 순서마다 달라 방향 추정이 애매함)는 건드리지
        // 않는다 — 그런 벨트는 보통 포트로 곧장 들어가는 직선이라 어긋남이 눈에 안 띈다.
        private void RerenderSegmentStrip(int segmentId)
        {
            if (segmentId < 0 || segmentId >= driver.World.Segments.Count) return;
            var segment = driver.World.Segments[segmentId];
            if (segment == null) return;

            var grid = driver.World.Grid;
            if (!grid.TryGetCellOf(CellOccupantType.Belt, segmentId, out var cell)) return;

            Vector2Int downCell = default;
            bool hasDown = segment.NextSegmentId.HasValue
                && grid.TryGetCellOf(CellOccupantType.Belt, segment.NextSegmentId.Value, out downCell);
            bool hasUp = TryGetUpstreamCell(segment, cell, out var upCell);
            if (!hasUp && !hasDown) return; // 방향을 알 수 없으면(양쪽 다 기계/없음) 건드리지 않는다

            var miniPath = new List<Vector2Int>(3);
            if (hasUp) miniPath.Add(upCell);
            miniPath.Add(cell);
            if (hasDown) miniPath.Add(downCell);

            ComputeCellSpan(miniPath, hasUp ? 1 : 0, out Vector3 entry, out Vector3 exit, out Vector3? bend);

            var existing = GameObject.Find($"Belt_{segmentId}");
            if (existing != null)
            {
                // Destroy는 프레임 끝에 처리되는데 바로 아래에서 같은 이름으로 새로 만든다 —
                // 그 사이 GameObject.Find(철거 도구 등)가 없어질 오브젝트를 집지 않게 이름부터 바꾼다.
                existing.name = $"Belt_{segmentId}_replaced";
                Destroy(existing);
            }
            SpawnCommittedVisual(entry, exit, bend, segmentId);
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

        private void SpawnCommittedVisual(Vector3 from, Vector3 to, Vector3? bend, int segmentId)
        {
            var root = new GameObject($"Belt_{segmentId}");

            // 벨트 스트립 메쉬는 from/to 지점을 중심(Y)으로 삼아 두께만큼 위아래로 걸쳐 있다.
            // 아이템 앵커까지 같은 Y를 쓰면 아이템 절반이 벨트 안에 파묻혀 버리니, 벨트 윗면
            // 위로 아이템 반지름만큼 띄워서 "위에 얹혀 굴러가는" 것처럼 보이게 한다.
            Vector3 itemHeightOffset = Vector3.up * (committedThickness * 0.2f + itemVisualRadius);

            var startAnchor = new GameObject("Start").transform;
            startAnchor.SetParent(root.transform);
            startAnchor.position = from + itemHeightOffset;

            var endAnchor = new GameObject("End").transform;
            endAnchor.SetParent(root.transform);
            endAnchor.position = to + itemHeightOffset;

            if (bend.HasValue)
            {
                // 코너 칸: 진입 절반 + 이탈 절반 두 조각으로 나눠 그려야 칸 전체가 빈틈없이 덮인다.
                BuildVisuals.CreateStrip(from, bend.Value, committedThickness, committedColor, root.transform, prefab: stripPrefab);
                BuildVisuals.CreateStrip(bend.Value, to, committedThickness, committedColor, root.transform, prefab: stripPrefab);
            }
            else
            {
                BuildVisuals.CreateStrip(from, to, committedThickness, committedColor, root.transform, prefab: stripPrefab);
            }

            var itemRenderer = root.AddComponent<BeltItemRenderer>();
            itemRenderer.Initialize(driver, segmentId, startAnchor, endAnchor, itemVisualPrefab);
        }
    }
}

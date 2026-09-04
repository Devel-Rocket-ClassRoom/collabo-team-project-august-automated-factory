using System.Collections.Generic;
using Factory.Buildings;
using Factory.Data;
using Factory.Simulation;
using UnityEngine;

namespace Factory.Building
{
    // 팔레트에서 기계를 고르면 반투명 고스트가 손가락 위(스크린 오프셋만큼 띄운 위치)에 스냅되고,
    // 확인 버튼을 눌러야 실제로 배치된다 (터치 릴리즈만으로는 확정하지 않음 — 오조작 방지).
    // 배치 전 "회전" 버튼으로 방향(Facing)을 90도씩 돌릴 수 있고, 고스트도 같이 돌아서
    // 출력 화살표가 어느 쪽을 향할지 미리 볼 수 있다.
    //
    // 기계 종류는 machineId(Bae님 MachineData.machineID랑 같은 문자열) 하나로만 식별한다 —
    // ScriptableObject(MachineDef)를 안 쓰므로, 필요한 정보(footprint 등)는 그때그때
    // GameDatabase에서 조회한다.
    public class MachineGhostTool : MonoBehaviour, IBuildTool
    {
        // 채굴기는 이제 하나뿐이고 유일하게 특별 취급되는 종류다(입출력 포트가 없어 원격
        // 전송, 광물 노드 위에만 지을 수 있음) — Bae님 데이터엔 이걸 구분할 필드가 없어서
        // 문자열 id로 직접 비교한다.
        private const string MinerMachineId = "Miner";
        // 분류기/합류기도 데이터에 구분 필드가 없어 id로 판정한다(RoutingRole은 배치 시 인스턴스에 심는다).
        private const string SplitterMachineId = "Splitter";
        private const string MergerMachineId = "Merger";

        private static readonly Vector2Int[] FourDirs =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
        };

        private static RoutingRole RoutingRoleFor(string machineId)
        {
            if (machineId == SplitterMachineId) return RoutingRole.Splitter;
            if (machineId == MergerMachineId) return RoutingRole.Merger;
            return RoutingRole.None;
        }

        [SerializeField] private Camera targetCamera;
        [SerializeField] private SimulationDriver driver;
        [SerializeField] private MachineVisualLibrary visualLibrary;
        [SerializeField] private Vector2 screenOffset = new Vector2(0f, 150f);
        [SerializeField] private Color validColor = new Color(0.3f, 0.9f, 0.4f, 0.45f);
        [SerializeField] private Color invalidColor = new Color(0.9f, 0.2f, 0.2f, 0.45f);
        // 기계 전용 프리팹도, visualLibrary에 등록된 것도 없을 때(폴백)만 쓰는 공용 박스 프리팹.
        [SerializeField] private GameObject ghostPrefab;

        private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

        private string selectedMachineId;
        private MachineRuntime selectedMachineRuntime;
        private GameObject ghost;
        private Vector2Int currentCell;
        private Vector2Int currentFacing = new Vector2Int(1, 0);
        private bool hasValidCell;

        public bool IsPlacing => selectedMachineId != null;
        public string SelectedMachineId => selectedMachineId;

        // 에디터 SerializedObject 없이(런타임/테스트에서) 직접 배선할 때 쓴다.
        public void Initialize(Camera targetCamera, SimulationDriver driver)
        {
            this.targetCamera = targetCamera;
            this.driver = driver;
        }

        public void SelectMachine(string machineId)
        {
            if (driver == null || driver.World == null) return;
            var db = driver.World.Database;
            if (!db.TryGetMachineId(machineId, out int id))
            {
                Debug.LogError($"[MachineGhostTool] '{machineId}' 기계 데이터를 찾을 수 없습니다 — " +
                    "Bae님 JSON(Machines.json)에 있는지, Bake를 다시 돌려야 하는 건 아닌지 확인하세요.");
                return;
            }

            CancelPlacement();
            selectedMachineId = machineId;
            selectedMachineRuntime = db.Machines[id];
            currentFacing = new Vector2Int(1, 0);

            // 고스트는 실제로 놓일 기계와 같은 모양(machineVisualLibrary에 등록된 종류별 전용
            // 프리팹)을 쓰고, 색만 유효/무효 색으로 덮어씌운다. 등록 안 된 종류는 공용 박스로 대체.
            GameObject shapePrefab = visualLibrary != null && visualLibrary.TryGetPrefab(machineId, out var found) ? found : null;
            ghost = shapePrefab != null
                ? Instantiate(shapePrefab, transform)
                : ghostPrefab != null
                    ? Instantiate(ghostPrefab, transform)
                    : BuildVisuals.CreateBox(Vector3.zero, new Vector3(0.9f, 0.9f, 0.9f), invalidColor, transform, withCollider: false);

            // 미리보기 전용이라 실제 동작(MachineView)과 충돌 판정은 걷어낸다.
            var view = ghost.GetComponent<MachineView>();
            if (view != null) Destroy(view);
            var collider = ghost.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            // footprint가 1칸보다 크면(예: 2x2 합성기) 고스트도 그만큼 크게 스케일한다 — 생성
            // 경로(프리팹/폴백 박스)가 원래 갖고 있던 기본 스케일에 곱해서, footprint=(1,1)일
            // 때는 기존 크기 그대로 유지된다.
            var baseGhostScale = ghost.transform.localScale;
            var footprint = selectedMachineRuntime.Footprint;
            ghost.transform.localScale = new Vector3(
                baseGhostScale.x * footprint.x, baseGhostScale.y, baseGhostScale.z * footprint.y);

            ghost.transform.rotation = FacingToRotation(currentFacing);
            ghost.SetActive(false);

            // 팔레트 버튼만 누르고 화면을 아직 탭하지 않은 상태에서도 바로 눈에 보이도록,
            // 화면 중앙 기준으로 즉시 한 번 배치해준다 (오프셋 없이 — 아직 손가락이 없으므로).
            if (targetCamera != null)
            {
                PlaceGhostAlongRay(targetCamera.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)));
            }
        }

        // 회전 버튼에서 호출. 90도씩 돌린다.
        public void RotateFacing()
        {
            currentFacing = new Vector2Int(-currentFacing.y, currentFacing.x);
            if (ghost != null) ghost.transform.rotation = FacingToRotation(currentFacing);
        }

        private static Quaternion FacingToRotation(Vector2Int facing)
        {
            Vector3 forward = new Vector3(facing.x, 0f, facing.y);
            return forward == Vector3.zero ? Quaternion.identity : Quaternion.LookRotation(forward, Vector3.up);
        }

        public void CancelPlacement()
        {
            selectedMachineId = null;
            hasValidCell = false;
            if (ghost != null) Destroy(ghost);
            ghost = null;
        }

        public void OnPressBegin(Vector2 screenPosition) => UpdateGhost(screenPosition);
        public void OnDrag(Vector2 screenPosition) => UpdateGhost(screenPosition);
        public void OnReleased(Vector2 screenPosition) => UpdateGhost(screenPosition);
        public void OnCancelled() { }

        private void UpdateGhost(Vector2 screenPosition)
        {
            if (selectedMachineId == null || targetCamera == null) return;
            PlaceGhostAlongRay(targetCamera.ScreenPointToRay(screenPosition + screenOffset));
        }

        private void PlaceGhostAlongRay(Ray ray)
        {
            if (ghost == null) return;

            if (!GridUtility.TryRaycastToCell(ray, groundPlane, out currentCell))
            {
                hasValidCell = false;
                ghost.SetActive(false);
                return;
            }

            hasValidCell = true;

            ghost.SetActive(true);
            var footprint = selectedMachineRuntime.Footprint;
            ghost.transform.position = GridUtility.GetFootprintCenter(currentCell, footprint, 0.5f);

            bool free = driver == null || driver.World == null
                || driver.World.Grid.IsFootprintFree(GridUtility.GetFootprintCells(currentCell, footprint));

            // 채굴기는 광물 노드가 있는 칸에만 지을 수 있다 — 미리보기에서도 그 조건을 반영한다.
            if (free && selectedMachineId == MinerMachineId
                && driver != null && driver.World != null && !driver.World.Grid.TryGetOreDeposit(currentCell, out _))
            {
                free = false;
            }

            BuildVisuals.Colorize(ghost, free ? validColor : invalidColor);
        }

        // 확인 버튼에서 호출.
        public bool Confirm()
        {
            if (selectedMachineId == null || !hasValidCell || driver == null || driver.World == null) return false;

            var grid = driver.World.Grid;
            var db = driver.World.Database;
            if (!db.TryGetMachineId(selectedMachineId, out int machineId)) return false;

            var runtime = db.Machines[machineId];
            var footprintCells = GridUtility.GetFootprintCells(currentCell, runtime.Footprint);
            if (!grid.IsFootprintFree(footprintCells)) return false;

            Vector3 worldPos = GridUtility.GetFootprintCenter(currentCell, runtime.Footprint, 0.5f);
            Quaternion rotation = FacingToRotation(currentFacing);

            if (selectedMachineId == MinerMachineId)
            {
                // 채굴기는 이제 하나뿐이고, 뭘 캐는지는 그 아래 광물 노드가 정한다 — 땅에
                // 노드가 없으면 애초에 지을 수 없다("광물이 있는 곳에만 채굴기를 지을 수 있다").
                if (!grid.TryGetOreDeposit(currentCell, out int oreDepositId)) return false;
                var deposit = db.OreDeposits[oreDepositId];

                // 채굴기는 입출력 포트가 없다(원격 전송) — 벨트 자동 연결 대상이 아니다.
                var miner = new MinerInstance
                {
                    MachineId = machineId,
                    OutputResourceId = deposit.ResourceId,
                    MineIntervalSeconds = deposit.MineIntervalSeconds,
                    YieldPerCycle = deposit.YieldPerCycle,
                };
                int index = driver.World.AddMiner(miner);
                grid.RegisterBuildingFootprint(footprintCells, CellOccupantType.Miner, index);
                SpawnMachineVisual(GetVisualPrefab(selectedMachineId), worldPos, rotation, MachineInstanceKind.Miner, index, new Color(0.55f, 0.4f, 0.25f), runtime.Footprint);
            }
            else
            {
                // 레시피는 자동 배정하지 않는다 — 배치 후 탭해서 직접 고른다(RecipeSelectionPanel).
                // 분류기/합류기면 RoutingRole을 심는다 — ProcessorSystem은 RecipeId<0라 안 건드리고,
                // RoutingSystem이 이 값으로 분배/병합한다.
                var processor = new ProcessorInstance(db.ResourceCount)
                {
                    MachineId = machineId,
                    RoutingRole = RoutingRoleFor(selectedMachineId),
                    Facing = currentFacing,
                    Anchor = currentCell,
                    Footprint = runtime.Footprint,
                };
                int index = driver.World.AddProcessor(processor);
                grid.RegisterBuildingFootprint(footprintCells, CellOccupantType.Processor, index);
                TryAutoConnectAdjacentBelts(processor, index);
                SpawnMachineVisual(GetVisualPrefab(selectedMachineId), worldPos, rotation, MachineInstanceKind.Processor, index, new Color(0.6f, 0.15f, 0.1f), runtime.Footprint);
            }

            CancelPlacement();
            return true;
        }

        private GameObject GetVisualPrefab(string machineId)
        {
            return visualLibrary != null && visualLibrary.TryGetPrefab(machineId, out var prefab) ? prefab : null;
        }

        // 벨트를 먼저 뻗어두고 나중에 그 옆에 기계를 놓는 순서로 지어도 연결되도록, 새로 놓인
        // 기계의 포트 칸(footprint+Facing 기준, 한 면에 여러 칸일 수 있음)에 정확히 닿은
        // 벨트가 있으면 자동으로 이어준다.
        // (벨트를 놓을 때는 반대 방향 — 옆에 있는 기계에 연결하는 것 — 을 BeltDragTool이
        // 이미 처리하고 있음. 이 메서드는 그 반대 순서를 처리하는 대응쌍이다.)
        // 채굴기는 포트가 없어서(원격 전송) 여기서 다루지 않는다 — Processor(제련로 등)만 해당.
        private void TryAutoConnectAdjacentBelts(ProcessorInstance processor, int index)
        {
            if (processor.RoutingRole != RoutingRole.None)
            {
                TryAutoConnectRoutingNode(processor, index);
                return;
            }

            var grid = driver.World.Grid;
            var segments = driver.World.Segments;

            var inputCells = GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, isOutputSide: false);
            for (int i = 0; i < inputCells.Count; i++)
            {
                if (!grid.TryGetOccupant(inputCells[i], out var inOccupant) || inOccupant.Type != CellOccupantType.Belt) continue;

                var segment = segments[inOccupant.InstanceIndex];
                bool isDeadEnd = segment.NextSegmentId == null && segment.TargetProcessorId == null;
                if (isDeadEnd) segment.TargetProcessorId = index;
            }

            var outputCells = GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, isOutputSide: true);
            for (int i = 0; i < outputCells.Count; i++)
            {
                if (!grid.TryGetOccupant(outputCells[i], out var outOccupant) || outOccupant.Type != CellOccupantType.Belt) continue;

                var segment = segments[outOccupant.InstanceIndex];
                if (IsChainStart(segments, segment)) segment.SourceProcessorId = index;
            }
        }

        // 라우팅 노드(1x1)는 포트 규약이 다르다 — 분류기는 -Facing(뒤) 1면이 입력, 나머지 3면이
        // 출력. 합류기는 +Facing(앞) 1면이 출력, 나머지 3면이 입력. 4방향 이웃 벨트를 훑어
        // 역할대로 이어준다(BeltDragTool.ResolveEndpointRole의 라우팅 분기와 같은 규칙).
        private void TryAutoConnectRoutingNode(ProcessorInstance node, int index)
        {
            var grid = driver.World.Grid;
            var segments = driver.World.Segments;

            for (int d = 0; d < FourDirs.Length; d++)
            {
                Vector2Int dir = FourDirs[d];
                if (!grid.TryGetOccupant(node.Anchor + dir, out var occ) || occ.Type != CellOccupantType.Belt) continue;

                var seg = segments[occ.InstanceIndex];
                bool inputFace = node.RoutingRole == RoutingRole.Splitter
                    ? dir == -node.Facing
                    : dir != node.Facing;

                if (inputFace)
                {
                    bool isDeadEnd = seg.NextSegmentId == null && seg.TargetProcessorId == null;
                    if (isDeadEnd) seg.TargetProcessorId = index;
                }
                else if (IsChainStart(segments, seg))
                {
                    seg.SourceProcessorId = index;
                }
            }
        }

        // BeltDragTool도 "기존 벨트에 새 상류를 붙여도 되는지" 판단할 때 이 정의를 그대로
        // 써야 한다(안 그러면 이미 완성된 체인의 끝에 억지로 연결되는 버그가 생김) — 그래서 public.
        public static bool IsChainStart(List<BeltSegment> segments, BeltSegment segment)
        {
            return segment.SourceProcessorId == null && !IsTargetOfAnySegment(segments, segment.Id);
        }

        private static bool IsTargetOfAnySegment(List<BeltSegment> segments, int segmentId)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i] == null) continue; // 철거로 비워진 슬롯(SimulationWorld.RemoveSegment 참고).
                if (segments[i].NextSegmentId == segmentId) return true;
            }
            return false;
        }

        private void SpawnMachineVisual(GameObject prefab, Vector3 position, Quaternion rotation, MachineInstanceKind kind, int index, Color color, Vector2Int footprint)
        {
            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, position, rotation);
            }
            else
            {
                go = BuildVisuals.CreateBox(position, Vector3.one, color, null);
                go.transform.rotation = rotation;
                go.AddComponent<MachineView>();
            }
            go.name = $"{kind}_{index}";

            // footprint가 1칸보다 크면(예: 2x2 합성기) 실제 오브젝트도 그만큼 크게 스케일한다.
            var baseScale = go.transform.localScale;
            go.transform.localScale = new Vector3(baseScale.x * footprint.x, baseScale.y, baseScale.z * footprint.y);

            var view = go.GetComponent<MachineView>();
            view.Initialize(kind, index, driver);
        }
    }
}

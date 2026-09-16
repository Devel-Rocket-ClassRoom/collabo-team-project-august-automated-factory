using System;
using System.Collections.Generic;
using Factory.Buildings;
using Factory.Data;
using Factory.Rendering;
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
        public static Func<string, Vector2Int, bool> PlacementPermission { get; set; }
        // 채굴기는 이제 하나뿐이고 유일하게 특별 취급되는 종류다(입출력 포트가 없어 원격
        // 전송, 광물 노드 위에만 지을 수 있음) — Bae님 데이터엔 이걸 구분할 필드가 없어서
        // 문자열 id로 직접 비교한다.
        private const string MinerMachineId = "Miner";
        // 미니 코어: 메인 코어랑 똑같이 UniversalPorts지만, 별도 창고가 아니라 메인 코어의
        // InputBuffer/OutputBuffer를 그대로 참조(엔더상자처럼 내용물 공유) — Confirm()에서
        // LinkToMainCore로 배선한다.
        private const string MiniCoreMachineId = "MiniCore";

        private static readonly Vector2Int[] FourDirs =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
        };

        [SerializeField] private Camera targetCamera;
        [SerializeField] private SimulationDriver driver;
        [SerializeField] private MachineVisualLibrary visualLibrary;
        [SerializeField] private Vector2 screenOffset = new Vector2(0f, 150f);
        [SerializeField] private Color validColor = new Color(0.3f, 0.9f, 0.4f, 0.8f);
        [SerializeField] private Color invalidColor = new Color(0.9f, 0.2f, 0.2f, 0.8f);
        // 기계 전용 프리팹도, visualLibrary에 등록된 것도 없을 때(폴백)만 쓰는 공용 박스 프리팹.
        [SerializeField] private GameObject ghostPrefab;

        private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

        private string selectedMachineId;
        private MachineRuntime selectedMachineRuntime;
        private GameObject ghost;
        private Vector2Int currentCell;
        private Vector2Int currentFacing = new Vector2Int(1, 0);
        private bool hasValidCell;
        // Addressables 모델은 비동기로 늦게 얹히는데, 그 시점에 다시 칠하려면 "마지막으로
        // 계산된 유효/무효 상태"가 필요하다 — PlaceGhostAlongRay가 매번 갱신한다.
        private bool lastPlacementFree = true;

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

            // 고스트는 실제로 놓일 기계와 같은 모양이어야 유효/무효 색이 자연스럽다 — 실제 배치
            // (SpawnMachineVisual)와 같은 우선순위: Addressables 키(있으면) > 라이브러리 프리팹 > 공용 박스.
            var footprint = selectedMachineRuntime.Footprint;
            string addressableKey = selectedMachineRuntime.PrefabName;
            GameObject shapePrefab = visualLibrary != null && visualLibrary.TryGetPrefab(machineId, out var found) ? found : null;

            if (!string.IsNullOrEmpty(addressableKey))
            {
                // 박스로 시작해서, 실제 배치와 동일하게 Addressables 모델이 로드되면 그걸로 바뀐다.
                // 여기선 root를 footprint로 스케일하지 않는다 — 배리언트가 이미 자기 footprint에
                // 맞게 만들어져 있고(placeholder도 footprint 크기로 직접 만듦), 또 곱하면 커진다.
                ghost = new GameObject("Ghost");
                ghost.transform.SetParent(transform, false);
                var placeholder = BuildVisuals.CreateBox(Vector3.zero, new Vector3(footprint.x, 1f, footprint.y), invalidColor, ghost.transform, withCollider: false);
                var mount = ghost.AddComponent<AddressableModelMount>();
                var capturedGhost = ghost; // 콜백이 나중에(비동기) 불릴 때 그 사이 ghost 필드가 다른 걸 가리킬 수 있으니 지금 값을 붙잡아둔다.
                mount.Mount(addressableKey, placeholder, onLoaded: () =>
                {
                    // 실제 모델이 막 얹혔을 때 원래 색(칠 안 된 상태)으로 한 프레임이라도 보이면
                    // "설치된 것처럼" 보인다 — 로드 완료 즉시 마지막 유효/무효 상태로 다시 칠한다.
                    if (capturedGhost != null) BuildVisuals.TintPreserveShape(capturedGhost, lastPlacementFree ? validColor : invalidColor);
                });
            }
            else if (shapePrefab != null)
            {
                ghost = Instantiate(shapePrefab, transform);
                var baseScale = ghost.transform.localScale;
                ghost.transform.localScale = new Vector3(baseScale.x * footprint.x, baseScale.y, baseScale.z * footprint.y);
            }
            else if (ghostPrefab != null)
            {
                ghost = Instantiate(ghostPrefab, transform);
                var baseScale = ghost.transform.localScale;
                ghost.transform.localScale = new Vector3(baseScale.x * footprint.x, baseScale.y, baseScale.z * footprint.y);
            }
            else
            {
                ghost = BuildVisuals.CreateBox(Vector3.zero, new Vector3(0.9f * footprint.x, 0.9f, 0.9f * footprint.y), invalidColor, transform, withCollider: false);
            }

            // 미리보기 전용이라 실제 동작(MachineView)과 충돌 판정은 걷어낸다(자식 포함 — Addressables
            // 모델이 콜라이더를 들고 있을 수 있음).
            foreach (var view in ghost.GetComponentsInChildren<MachineView>()) Destroy(view);
            foreach (var col in ghost.GetComponentsInChildren<Collider>()) Destroy(col);

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

            lastPlacementFree = free; // Addressables 모델이 나중에 로드됐을 때 다시 칠할 기준값.

            // 실제 모델의 텍스처/모양은 유지하고 색조만 유효/무효 색으로 — 자식 렌더러(다중 파츠
            // 모델 포함) 전부 처리해서 일부만 안 바뀌는 문제 없게.
            BuildVisuals.TintPreserveShape(ghost, free ? validColor : invalidColor);
        }

        // 확인 버튼에서 호출.
        public bool Confirm()
        {
            if (selectedMachineId == null || !hasValidCell || driver == null || driver.World == null) return false;
            if (PlacementPermission != null && !PlacementPermission(selectedMachineId, currentCell)) return false;

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
                SpawnMachineVisual(GetVisualPrefab(selectedMachineId), runtime.PrefabName, worldPos, rotation, MachineInstanceKind.Miner, index, new Color(0.55f, 0.4f, 0.25f), runtime.Footprint);
            }
            else
            {
                // 레시피는 자동 배정하지 않는다 — 배치 후 탭해서 직접 고른다(RecipeSelectionPanel).
                // 분류기/합류기면 RoutingRole을 심는다 — ProcessorSystem은 RecipeId<0라 안 건드리고,
                // RoutingSystem이 이 값으로 분배/병합한다.
                var processor = new ProcessorInstance(db.ResourceCount)
                {
                    MachineId = machineId,
                    RoutingRole = RoutingRoles.For(selectedMachineId),
                    Facing = currentFacing,
                    Anchor = currentCell,
                    Footprint = runtime.Footprint,
                };
                if (selectedMachineId == MiniCoreMachineId) LinkToMainCore(processor);
                int index = driver.World.AddProcessor(processor);
                grid.RegisterBuildingFootprint(footprintCells, CellOccupantType.Processor, index);
                TryAutoConnectAdjacentBelts(processor, index);
                SpawnMachineVisual(GetVisualPrefab(selectedMachineId), runtime.PrefabName, worldPos, rotation, MachineInstanceKind.Processor, index, new Color(0.6f, 0.15f, 0.1f), runtime.Footprint);
            }

            CancelPlacement();
            return true;
        }

        // 미니 코어를 메인 코어와 같은 창고를 보는 "창구"로 만든다 — 새로 할당된 자기 전용
        // 버퍼 배열을 버리고, 메인 코어의 InputBuffer/OutputBuffer를 그대로(참조로) 물린다.
        // int[]는 참조 타입이라 이렇게만 해도 한쪽에서 넣은 게 다른 쪽에서도 그대로 보인다
        // (마인크래프트 엔더상자와 같은 원리). Capacity도 코어 값(9999)을 그대로 맞춰서,
        // 수용 가능 여부 판정이 어느 창구로 넣든 항상 같은 기준이 되게 한다.
        // 코어가 아직 없는 경우(이론상 불가 — CoreSpawner가 게임 시작 시 항상 먼저 만듦)엔
        // 그냥 독립된 자기 버퍼로 남겨서 최소한 자체적으로는 동작하게 둔다.
        private void LinkToMainCore(ProcessorInstance processor)
        {
            processor.UniversalPorts = true;

            int coreIndex = driver.World.CoreProcessorIndex;
            if (coreIndex < 0 || coreIndex >= driver.World.Processors.Count) return;
            var core = driver.World.Processors[coreIndex];
            if (core == null) return;

            processor.InputBuffer = core.InputBuffer;
            processor.OutputBuffer = core.OutputBuffer;
            processor.Capacity = core.Capacity;
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

            // 코어/미니 코어(UniversalPorts)는 고정 Facing이 없어서 GetPortCells(고정 입/출력면
            // 기준)를 쓸 수 없다 — 라우팅 노드처럼 4방향을 직접 훑되, 입/출력 구분은 Facing이
            // 아니라 그 벨트 자신의 상태(막다른 끝=입력, 상류 없음=출력)로만 정한다.
            if (processor.UniversalPorts)
            {
                TryAutoConnectUniversalPorts(processor, index);
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

        // UniversalPorts(코어/미니 코어) 전용 자동 연결. footprint 1x1 전제(현재 이 값을 쓰는
        // 건 미니 코어뿐 — 메인 코어는 게임 시작 시 벨트가 없는 상태에서 CoreSpawner로 미리
        // 놓이므로 이 경로를 탈 일이 없다).
        private void TryAutoConnectUniversalPorts(ProcessorInstance processor, int index)
        {
            var grid = driver.World.Grid;
            var segments = driver.World.Segments;

            for (int d = 0; d < FourDirs.Length; d++)
            {
                if (!grid.TryGetOccupant(processor.Anchor + FourDirs[d], out var occ) || occ.Type != CellOccupantType.Belt) continue;

                var segment = segments[occ.InstanceIndex];
                bool isDeadEnd = segment.NextSegmentId == null && segment.TargetProcessorId == null;
                if (isDeadEnd)
                {
                    segment.TargetProcessorId = index;
                }
                else if (IsChainStart(segments, segment))
                {
                    segment.SourceProcessorId = index;
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

        // 우선순위: Addressables 키(있으면) > 임시 다리(MachineVisualLibrary 직접 참조) > 폴백 박스.
        // Addressables 경로는 루트에 폴백 박스를 먼저 띄우고 AddressableModelMount가 모델을 비동기로
        // 얹는다(로드 실패 시 박스 유지). 탭 판정 콜라이더는 항상 루트에 footprint 크기로 붙는다.
        private void SpawnMachineVisual(GameObject libraryPrefab, string addressableKey, Vector3 position, Quaternion rotation, MachineInstanceKind kind, int index, Color color, Vector2Int footprint)
        {
            GameObject go;
            Transform visual = null; // 콜라이더 없는 순수 시각 전용 자식 — 있으면 작동 중 들썩임 대상.
            if (!string.IsNullOrEmpty(addressableKey) || libraryPrefab == null)
            {
                go = new GameObject();
                go.transform.SetPositionAndRotation(position, rotation);

                var boxCollider = go.AddComponent<BoxCollider>();
                boxCollider.center = new Vector3(0f, 0.5f, 0f);
                boxCollider.size = new Vector3(footprint.x, 1f, footprint.y);

                // 탭 판정 콜라이더는 루트(go)에 고정, 실제로 보이는 모델은 이 자식(visual) 밑에만
                // 둔다 — 작동 중 들썩임으로 이 자식 스케일만 흔들어도 콜라이더/배치 판정엔 영향 없음.
                visual = new GameObject("Visual").transform;
                visual.SetParent(go.transform, false);

                var placeholder = BuildVisuals.CreateBox(position, new Vector3(footprint.x, 1f, footprint.y), color, visual, withCollider: false);
                placeholder.name = "Placeholder";

                visual.gameObject.AddComponent<AddressableModelMount>().Mount(addressableKey, placeholder);
            }
            else
            {
                go = Instantiate(libraryPrefab, position, rotation);
                var baseScale = go.transform.localScale;
                go.transform.localScale = new Vector3(baseScale.x * footprint.x, baseScale.y, baseScale.z * footprint.y);
            }
            go.name = $"{kind}_{index}";

            var view = go.GetComponent<MachineView>() ?? go.AddComponent<MachineView>();
            view.Initialize(kind, index, driver);

            // 작동 중(레시피 처리 중인 기계, 항상 캐는 채굴기) 표시 — 분류기/합류기/코어는
            // IsProcessing이 절대 안 켜져서 자동으로 가만히 있는다. visual이 없는(라이브러리
            // 프리팹 폴백) 경로는 콜라이더랑 안 분리돼 있어서 건너뛴다.
            if (visual != null)
            {
                go.AddComponent<MachineActivityIndicator>().Initialize(visual, kind, index, driver);
            }
        }
    }
}

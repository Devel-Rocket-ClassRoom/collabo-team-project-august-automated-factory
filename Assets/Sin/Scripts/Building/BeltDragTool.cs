using System.Collections.Generic;
using Factory.Rendering;
using Factory.Simulation;
using UnityEngine;

namespace Factory.Building
{
    // 프레스+드래그로 벨트 경로를 그리고(코너는 BeltPathBuilder가 자동 처리), 릴리즈 시
    // 실제 BeltSegment로 커밋한다. 경로의 시작/끝 칸이 이미 놓인 기계와 겹치면 그 기계에
    // 자동으로 연결(SourceMinerId/SourceProcessorId/TargetProcessorId)하고, 이미 있는
    // 벨트 칸과 겹치면 그 세그먼트의 NextSegmentId로 이어붙여 컨베이어를 연장할 수 있다.
    public class BeltDragTool : MonoBehaviour, IBuildTool
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private SimulationDriver driver;
        [SerializeField] private float previewThickness = 0.5f;
        [SerializeField] private float committedThickness = 0.6f;
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
        // 계산한다. 예전엔 "이 칸 중심 -> 다음 칸 중심"으로 그려서 중심이 칸 경계에 걸쳐
        // 있었다 — 그러면 벨트가 한 칸에 딱 맞지 않고 두 칸에 걸친 것처럼 보인다.
        // 진입/이탈 방향이 다르면(코너) 꺾이는 지점(bend)을 반환해서 두 조각으로 나눠 그린다.
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

        private void Commit()
        {
            if (driver == null || driver.World == null || path.Count < 2) return;

            var grid = driver.World.Grid;
            grid.TryGetOccupant(path[0], out var startOccupant);
            grid.TryGetOccupant(path[path.Count - 1], out var endOccupant);
            bool startOccupied = grid.IsOccupied(path[0]);
            bool endOccupied = grid.IsOccupied(path[path.Count - 1]);

            // 우리 기계는 고정된 입력/출력 면이 없다 (모바일에서 칸마다 특정 면을 정확히
            // 짚게 하는 건 오조작을 유발함) — 그래서 방향은 순수하게 드래그 방향을 따른다:
            // 시작 칸에 닿은 대상은 소스, 끝 칸에 닿은 대상은 타겟. "제련로에서 시작하면
            // 무조건 입력일 것"이라고 임의로 뒤집지 않는다 — 제련로도 자기 산출물을 다른
            // 곳으로 보내려고 거기서부터 드래그하는 정당한 경우가 있기 때문. 타입이 안 맞는
            // 연결(예: 채굴기를 타겟으로)은 그냥 그 쪽 연결이 안 걸릴 뿐, 자동으로 뒤집지 않는다.

            // 시작/끝 칸이 기계나 "이미 있는 벨트"와 겹치면 그 칸 자체는 새 벨트 칸으로 만들지
            // 않고(중복 점유 방지) 대신 그 대상에 연결한다. 그 외의 겹치는 칸은 그냥 막는다.
            var beltCells = new List<Vector2Int>(path);
            if (endOccupied) beltCells.RemoveAt(beltCells.Count - 1);
            if (startOccupied) beltCells.RemoveAt(0);

            if (beltCells.Count == 0)
            {
                // 새로 놓을 벨트 칸이 아예 없는 경우 (예: 기존 벨트 끝이 제련로 바로 옆칸이라
                // 사이에 빈 칸이 없음) — 새 세그먼트 없이 기존 것끼리 바로 연결을 시도한다.
                TryDirectLink(startOccupied, startOccupant, endOccupied, endOccupant);
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

            if (startOccupied)
            {
                switch (startOccupant.Type)
                {
                    case CellOccupantType.Miner:
                        createdSegments[0].SourceMinerId = startOccupant.InstanceIndex;
                        break;
                    case CellOccupantType.Processor:
                        createdSegments[0].SourceProcessorId = startOccupant.InstanceIndex;
                        break;
                    case CellOccupantType.Belt:
                        // 기존 벨트 끝에 이어서 놓는 경우: 그 세그먼트가 새 첫 세그먼트로 흘러들게 연결.
                        driver.World.Segments[startOccupant.InstanceIndex].NextSegmentId = createdSegments[0].Id;
                        break;
                }
            }

            if (endOccupied && endOccupant.Type == CellOccupantType.Processor)
            {
                createdSegments[createdSegments.Count - 1].TargetProcessorId = endOccupant.InstanceIndex;
            }
            else if (endOccupied && endOccupant.Type == CellOccupantType.Belt)
            {
                // 기존 벨트 시작 쪽에 이어붙이는 경우: 새 마지막 세그먼트가 그 세그먼트로 흘러들게 연결.
                createdSegments[createdSegments.Count - 1].NextSegmentId = endOccupant.InstanceIndex;
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
        }

        // 새 벨트 칸 없이 기존 벨트를 기존 제련로/기존 벨트에 직접 연결한다 (둘이 바로 붙어있는 경우).
        // 채굴기는 최소 한 칸의 벨트가 있어야 산출물을 실을 수 있으므로 여기서는 다루지 않는다.
        private void TryDirectLink(bool startOccupied, CellOccupant startOccupant, bool endOccupied, CellOccupant endOccupant)
        {
            if (!startOccupied || startOccupant.Type != CellOccupantType.Belt || !endOccupied) return;

            var startSegment = driver.World.Segments[startOccupant.InstanceIndex];
            if (endOccupant.Type == CellOccupantType.Processor)
            {
                startSegment.TargetProcessorId = endOccupant.InstanceIndex;
            }
            else if (endOccupant.Type == CellOccupantType.Belt)
            {
                startSegment.NextSegmentId = endOccupant.InstanceIndex;
            }
        }

        private void SpawnCommittedVisual(Vector3 from, Vector3 to, Vector3? bend, int segmentId)
        {
            var root = new GameObject($"Belt_{segmentId}");

            var startAnchor = new GameObject("Start").transform;
            startAnchor.SetParent(root.transform);
            startAnchor.position = from;

            var endAnchor = new GameObject("End").transform;
            endAnchor.SetParent(root.transform);
            endAnchor.position = to;

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

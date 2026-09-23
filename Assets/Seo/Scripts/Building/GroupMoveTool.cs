using System.Collections.Generic;
using Factory.Building;
using Factory.Simulation;
using UnityEngine;

namespace Seo.Building
{
    public sealed class GroupMoveTool : MonoBehaviour, IBuildTool
    {
        private static readonly Plane Ground = new Plane(Vector3.up, Vector3.zero);
        private static readonly Color ValidTint = new Color(0.3f, 1f, 0.65f, 1f);
        private static readonly Color InvalidTint = new Color(1f, 0.3f, 0.25f, 1f);
        private readonly List<Transform> originals = new List<Transform>();
        private readonly List<Renderer> previews = new List<Renderer>();
        private readonly List<Renderer> markers = new List<Renderer>();
        private MaterialPropertyBlock tint;
        private Camera targetCamera;
        private BuildInputRouter router;
        private SimulationDriver driver;
        private FactoryMoveSelection selection;
        private GameObject previewRoot;
        private Vector2Int offset;
        private int quarterTurns;
        private Vector2Int grabCell;
        private bool dragging;
        private bool? lastTintValid;
        private float nextValidation;
        private string summary;
        public bool CanConfirm { get; private set; }
        public string Status { get; private set; } = "이동할 기계·벨트를 선택하세요";

        public static GroupMoveTool ActiveFor(BuildInputRouter router) =>
            router != null ? router.ExternalTool as GroupMoveTool : null;

        // 생성·선택 캡처·입력 연결은 Seo가 담당한다. 공용 라우터는 IBuildTool만 알면 된다.
        public static bool BeginSelectionMove(BuildInputRouter router, DemolishTool source, out string message)
        {
            message = "편집 모드에서 이동할 영역을 먼저 선택하세요";
            if (router == null || router.CurrentMode != BuildInputRouter.Mode.Demolish) return false;
            var tool = router.GetComponent<GroupMoveTool>() ?? router.gameObject.AddComponent<GroupMoveTool>();
            bool started = tool.Begin(source);
            message = tool.Status;
            if (!started) return false;
            tool.DetachRouter();
            tool.router = router;
            router.SetExternalTool(tool);
            router.ModeChanged += tool.HandleModeChanged;
            return true;
        }

        private void HandleModeChanged(BuildInputRouter.Mode mode)
        {
            if (ActiveFor(router) == this) return;
            CancelMove();
            DetachRouter();
        }

        private void DetachRouter()
        {
            if (router != null) router.ModeChanged -= HandleModeChanged;
            router = null;
        }

        public bool Begin(DemolishTool source)
        {
            CancelMove();
            if (source == null || source.Driver == null || source.Driver.World == null) return false;
            targetCamera = source.TargetCamera;
            driver = source.Driver;
            selection = new FactoryMoveSelection(driver.World, source.Selected);
            if (selection.Entries.Count == 0)
            {
                Status = "이동할 기계·벨트가 없습니다 · 코어와 전력 시설은 제외됩니다";
                selection = null;
                return false;
            }
            int machines = 0;
            var beltCells = new HashSet<Vector2Int>();
            foreach (var entry in selection.Entries)
            {
                var root = GameObject.Find(entry.VisualName);
                if (root == null)
                {
                    CancelMove();
                    Status = "설치물 표시가 준비되지 않았습니다. 다시 선택하세요";
                    return false;
                }
                originals.Add(root.transform);
                if (entry.Type == CellOccupantType.Belt) beltCells.Add(entry.Anchor);
                else machines++;
            }
            summary = $"기계 {machines}개 · 벨트 {beltCells.Count}칸";
            CreatePreview();
            RefreshValidity();
            return true;
        }

        private void CreatePreview()
        {
            previewRoot = new GameObject("GroupMovePreview");
            previewRoot.transform.SetParent(transform, true);
            // 시각용 메쉬만 복사한다. 원본의 생산·입력·Addressables 컴포넌트는 복제하지 않는다.
            foreach (var original in originals)
            {
                foreach (var source in original.GetComponentsInChildren<MeshFilter>())
                {
                    var renderer = source.GetComponent<MeshRenderer>();
                    if (renderer == null || !renderer.enabled || source.sharedMesh == null || IsBeltItem(source.transform, original)) continue;
                    var visual = new GameObject("PreviewMesh", typeof(MeshFilter), typeof(MeshRenderer));
                    visual.transform.SetParent(previewRoot.transform, false);
                    visual.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                    visual.transform.localScale = source.transform.lossyScale;
                    visual.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                    var copy = visual.GetComponent<MeshRenderer>();
                    copy.sharedMaterials = renderer.sharedMaterials;
                    copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    copy.receiveShadows = false;
                    previews.Add(copy);
                }
            }
            foreach (var entry in selection.Entries)
            {
                if (entry.Crossing) continue;
                var marker = BuildVisuals.CreateBox(GridUtility.GetFootprintCenter(entry.Anchor, entry.Footprint, 0.04f),
                    new Vector3(entry.Footprint.x, 0.08f, entry.Footprint.y), new Color(1f, 1f, 1f, 0.3f),
                    previewRoot.transform, withCollider: false);
                markers.Add(marker.GetComponent<Renderer>());
            }
        }

        private static bool IsBeltItem(Transform child, Transform root)
        {
            for (var node = child; node != null && node != root; node = node.parent)
                if (node.name == "BeltItem") return true;
            return false;
        }

        public void OnPressBegin(Vector2 screenPosition)
        {
            if (selection == null || !TryCell(screenPosition, out var cell)) return;
            var displayedBounds = selection.GetTargetBounds(offset, quarterTurns);
            grabCell = displayedBounds.Contains(cell) ? cell - offset : selection.Bounds.position;
            dragging = true;
            SetOffset(cell - grabCell);
        }

        public void OnDrag(Vector2 screenPosition)
        {
            if (dragging && TryCell(screenPosition, out var cell)) SetOffset(cell - grabCell);
        }

        public void OnReleased(Vector2 screenPosition)
        {
            OnDrag(screenPosition);
            dragging = false;
        }

        // 두 손가락 카메라 조작은 드래그만 중지하고 미리보기는 보존한다.
        public void OnCancelled() { dragging = false; }

        private bool TryCell(Vector2 screenPosition, out Vector2Int cell)
        {
            cell = default;
            return targetCamera != null && GridUtility.TryRaycastToCell(targetCamera.ScreenPointToRay(screenPosition), Ground, out cell);
        }

        private void SetOffset(Vector2Int value)
        {
            offset = value;
            UpdatePreviewTransform();
            RefreshValidity();
        }

        public void Rotate()
        {
            if (selection == null) return;
            dragging = false;
            quarterTurns = (quarterTurns + 1) % 4;
            UpdatePreviewTransform();
            RefreshValidity();
        }

        private void UpdatePreviewTransform()
        {
            if (previewRoot == null || selection == null) return;
            previewRoot.transform.SetPositionAndRotation(
                selection.TransformPosition(Vector3.zero, offset, quarterTurns) + Vector3.up * 0.03f,
                Quaternion.Euler(0f, -quarterTurns * 90f, 0f));
        }

        private void Update()
        {
            if (selection == null || Time.unscaledTime < nextValidation) return;
            nextValidation = Time.unscaledTime + 0.15f;
            RefreshValidity();
        }

        private void RefreshValidity()
        {
            CanConfirm = false;
            string reason = "공장 상태가 변경됐습니다. 다시 선택하세요";
            if (selection != null && driver != null && ReferenceEquals(driver.World, selection.World))
                CanConfirm = selection.Validate(offset, out reason, BeltDragTool.ExternalCellBlocked, MachineGhostTool.PlacementPermission, quarterTurns);
            Status = summary + $" · 회전 {quarterTurns * 90}°\n" + (CanConfirm ? "이동 확정 · 묶음 내부 연결 유지 / 바깥 연결 해제" : reason);
            if (lastTintValid == CanConfirm && tint != null) return;
            // Unity 네이티브 객체는 MonoBehaviour 필드 초기화가 아닌 실행 시점에 생성한다.
            tint ??= new MaterialPropertyBlock();
            Color color = CanConfirm ? ValidTint : InvalidTint;
            tint.SetColor("_BaseColor", color);
            tint.SetColor("_Color", color);
            foreach (var renderer in previews) if (renderer != null) renderer.SetPropertyBlock(tint);
            color.a = 0.25f;
            tint.SetColor("_BaseColor", color);
            tint.SetColor("_Color", color);
            foreach (var renderer in markers) if (renderer != null) renderer.SetPropertyBlock(tint);
            lastTintValid = CanConfirm;
        }

        public bool Confirm()
        {
            RefreshValidity();
            if (!CanConfirm) return false;
            foreach (var visual in originals)
                if (visual == null) { Status = "설치물 표시가 바뀌었습니다. 다시 선택하세요"; CanConfirm = false; return false; }
            if (!selection.TryCommit(offset, out string reason, BeltDragTool.ExternalCellBlocked, MachineGhostTool.PlacementPermission, quarterTurns))
            { Status = reason; return false; }
            var rotation = Quaternion.Euler(0f, -quarterTurns * 90f, 0f);
            // 미리보기와 동일한 변환으로 Start/End/Bend, 아이템 표시, 기계 방향을 함께 돌린다.
            foreach (var visual in originals)
                visual.SetPositionAndRotation(selection.TransformPosition(visual.position, offset, quarterTurns),
                    rotation * visual.rotation);
            string completed = summary + " 이동 완료 · 바깥쪽 벨트는 다시 연결하세요";
            CancelMove();
            Status = completed;
            return true;
        }

        public void CancelMove()
        {
            selection = null;
            dragging = false;
            CanConfirm = false;
            offset = Vector2Int.zero;
            quarterTurns = 0;
            lastTintValid = null;
            originals.Clear();
            previews.Clear();
            markers.Clear();
            if (previewRoot != null)
            {
                previewRoot.SetActive(false);
                Destroy(previewRoot);
            }
            previewRoot = null;
        }

        private void OnDisable()
        {
            if (ActiveFor(router) == this) router.SetMode(BuildInputRouter.Mode.None);
            DetachRouter();
            CancelMove();
        }
    }
}

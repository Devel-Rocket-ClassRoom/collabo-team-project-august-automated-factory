using Factory.Building;
using Factory.Buildings;
using Factory.Simulation;
using Factory.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Seo.UI
{
    public sealed class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [SerializeField] private float refreshInterval = 0.2f;
        [SerializeField] private float discoveryInterval = 0.5f;

        private SimulationDriver driver;
        private BuildInputRouter buildInputRouter;
        private MachineGhostTool machineGhostTool;
        private Camera targetCamera;
        private MachineInfoPanel machineInfoPanel;
        private GhostPortPreview ghostPortPreview;
        private MachineInstanceKind selectedKind;
        private int selectedIndex = -1;
        private float nextRefreshTime;
        private float nextDiscoveryTime;
        private bool selectionPending;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRuntimeInstance()
        {
            if (FindFirstObjectByType<UIManager>() != null) return;
            new GameObject("[Seo] UIManager").AddComponent<UIManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DiscoverSceneContext();
            EnsurePanel();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextDiscoveryTime)
            {
                DiscoverSceneContext();
                AttachWorldIndicators();
                nextDiscoveryTime = Time.unscaledTime + discoveryInterval;
            }

            HandleMachineSelection();

            if (machineInfoPanel == null || !machineInfoPanel.IsOpen || Time.unscaledTime < nextRefreshTime) return;
            RefreshSelected();
            nextRefreshTime = Time.unscaledTime + refreshInterval;
        }

        private void LateUpdate()
        {
            if (!selectionPending) return;
            selectionPending = false;

            // 기존 MachineView는 프로세서 탭 시 레시피 창을 즉시 연다. 공동 코드를 수정하지
            // 않고 유지하되, 일반 탭에서는 상세 정보가 먼저 보이도록 같은 프레임 끝에 닫는다.
            RecipeSelectionPanel.Instance?.Close();
            RefreshSelected();
        }

        public void ShowMachine(MachineInstanceKind kind, int instanceIndex)
        {
            selectedKind = kind;
            selectedIndex = instanceIndex;
            selectionPending = true;
        }

        public void CloseMachineInfo()
        {
            selectedIndex = -1;
            if (machineInfoPanel != null) machineInfoPanel.Close();
        }

        private void DiscoverSceneContext()
        {
            if (driver == null) driver = FindFirstObjectByType<SimulationDriver>();
            if (buildInputRouter == null) buildInputRouter = FindFirstObjectByType<BuildInputRouter>();
            if (machineGhostTool == null) machineGhostTool = FindFirstObjectByType<MachineGhostTool>();
            if (targetCamera == null) targetCamera = Camera.main;

            if (machineGhostTool != null)
            {
                if (ghostPortPreview == null) ghostPortPreview = gameObject.AddComponent<GhostPortPreview>();
                ghostPortPreview.Initialize(machineGhostTool);
            }
        }

        private void HandleMachineSelection()
        {
            var pointer = Pointer.current;
            if (pointer == null || !pointer.press.wasPressedThisFrame) return;
            if (driver == null || driver.World == null || targetCamera == null) return;
            if (buildInputRouter != null && buildInputRouter.IsToolActive) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Ray ray = targetCamera.ScreenPointToRay(pointer.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) return;

            var view = hit.collider.GetComponentInParent<MachineView>();
            if (view == null || !MachineViewAdapter.TryRead(view, out var selection)) return;
            ShowMachine(selection.Kind, selection.InstanceIndex);
        }

        private void AttachWorldIndicators()
        {
            if (driver == null || driver.World == null) return;

            var views = FindObjectsByType<MachineView>(FindObjectsSortMode.None);
            for (int i = 0; i < views.Length; i++)
            {
                var view = views[i];
                var indicator = view.GetComponent<MachineWorldIndicator>();
                if (indicator != null && indicator.IsInitialized) continue;
                if (!MachineViewAdapter.TryRead(view, out var selection)) continue;

                if (indicator == null) indicator = view.gameObject.AddComponent<MachineWorldIndicator>();
                indicator.Initialize(selection.Kind, selection.InstanceIndex, selection.Driver);
            }
        }

        private void RefreshSelected()
        {
            EnsurePanel();
            if (!MachineInfoPresenter.TryBuild(driver, selectedKind, selectedIndex, out var data))
            {
                CloseMachineInfo();
                return;
            }

            machineInfoPanel.Render(data);
            machineInfoPanel.Open();
        }

        private void EnsurePanel()
        {
            if (machineInfoPanel != null) return;

            Canvas canvas = null;
            var namedCanvas = GameObject.Find("HUDCanvas");
            if (namedCanvas != null) canvas = namedCanvas.GetComponent<Canvas>();

            if (canvas == null)
            {
                var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                for (int i = 0; i < canvases.Length; i++)
                {
                    if (canvases[i].renderMode == RenderMode.WorldSpace) continue;
                    canvas = canvases[i];
                    break;
                }
            }

            if (canvas == null)
            {
                var canvasObject = new GameObject(
                    "[Seo] HUDCanvas",
                    typeof(Canvas),
                    typeof(UnityEngine.UI.CanvasScaler),
                    typeof(UnityEngine.UI.GraphicRaycaster));
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            machineInfoPanel = MachineInfoPanel.CreateRuntime(canvas.transform);
            machineInfoPanel.CloseRequested += CloseMachineInfo;
            machineInfoPanel.RecipeRequested += OpenRecipeSelection;
        }

        private void OpenRecipeSelection()
        {
            if (driver == null || driver.World == null || selectedKind != MachineInstanceKind.Processor) return;
            if (selectedIndex < 0 || selectedIndex >= driver.World.Processors.Count) return;

            var processor = driver.World.Processors[selectedIndex];
            if (processor == null || processor.UniversalPorts) return;

            string machineKey = driver.World.Database.Machines[processor.MachineId].Key;
            machineInfoPanel.Close();
            RecipeSelectionPanel.Instance?.Open(selectedIndex, machineKey);
        }
    }
}

using Factory.Building;
using Factory.Data;
using Factory.Simulation;
using Factory.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 에셋 없는 프로토타입 씬을 기본 도형으로 자동 구성한다.
// Tools > Factory Prototype > Build Tech Tree Scene 메뉴로 실행하거나
// -executeMethod SceneBootstrapper.BuildPrototypeScene 로 배치 실행 가능.
// 실행 전에 Tools > Factory Prototype > Seed Sample Game Data로 예시 데이터를 먼저 만들어야 한다.
//
// 플레이 시작 시 그리드는 비어 있고, 하단 팔레트에서 채굴기/제련로/벨트를 골라 직접 놓아야 한다
// (예전처럼 하드코딩된 데모 라인이 자동으로 생기지 않음).
public static class SceneBootstrapper
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string MachinesPath = "Assets/Sin/Resources/GameData/Machines";
    private const string PrefabsPath = "Assets/Sin/Prefabs";
    // 48dp 최소 터치 타겟은 유지하면서(캔버스 스케일 기준 대략 매칭) 예전(160x100)보다 작게.
    private static readonly Vector2 ButtonSize = new Vector2(120f, 72f);

    [MenuItem("Tools/Factory Prototype/Build Tech Tree Scene")]
    public static void BuildPrototypeScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        RemoveLegacyObjects();

        var driverGO = EnsureEmpty("SimulationDriver", Vector3.zero);
        var driver = EnsureComponentOn<SimulationDriver>(driverGO);

        var tapGO = EnsureEmpty("TapInputManager", Vector3.zero);
        var tapInput = EnsureComponentOn<TapInputManager>(tapGO);
        SetRef(tapInput, "targetCamera", Camera.main);

        var buildSystemGO = EnsureEmpty("BuildSystem", Vector3.zero);
        var beltTool = EnsureComponentOn<BeltDragTool>(buildSystemGO);
        var machineTool = EnsureComponentOn<MachineGhostTool>(buildSystemGO);
        var cameraRig = EnsureComponentOn<TouchCameraRig>(buildSystemGO);
        var router = EnsureComponentOn<BuildInputRouter>(buildSystemGO);

        SetRef(beltTool, "targetCamera", Camera.main);
        SetRef(beltTool, "driver", driver);
        SetRef(machineTool, "targetCamera", Camera.main);
        SetRef(machineTool, "driver", driver);
        SetRef(cameraRig, "targetCamera", Camera.main);
        SetRef(router, "beltTool", beltTool);
        SetRef(router, "machineTool", machineTool);
        SetRef(router, "cameraRig", cameraRig);

        // 기계별 외형은 이제 코드가 아니라 MachineDef.visualPrefab(DataSeeder가 채움)이 들고
        // 있어서, MachineGhostTool에는 종류 무관 폴백용 ghostPrefab만 넘기면 된다.
        var itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsPath}/BeltItemVisual.prefab");
        var corePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsPath}/CoreVisual.prefab");
        var ghostPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsPath}/MachineGhost.prefab");
        var stripPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsPath}/BeltStripVisual.prefab");
        if (itemPrefab == null || corePrefab == null || ghostPrefab == null || stripPrefab == null)
        {
            Debug.LogWarning("[SceneBootstrapper] Prefab(s) not found — run Tools > Factory Prototype > Build Prefabs first.");
        }

        SetRef(beltTool, "itemVisualPrefab", itemPrefab);
        SetRef(beltTool, "stripPrefab", stripPrefab);
        SetRef(machineTool, "ghostPrefab", ghostPrefab);

        var coreSpawnerGO = EnsureEmpty("CoreSpawner", Vector3.zero);
        var coreSpawner = EnsureComponentOn<CoreSpawner>(coreSpawnerGO);
        SetRef(coreSpawner, "driver", driver);
        SetRef(coreSpawner, "corePrefab", corePrefab);

        var oreDepositVisualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsPath}/OreDepositVisual.prefab");
        var oreDepositSpawnerGO = EnsureEmpty("OreDepositSpawner", Vector3.zero);
        var oreDepositSpawner = EnsureComponentOn<OreDepositSpawner>(oreDepositSpawnerGO);
        SetRef(oreDepositSpawner, "driver", driver);
        SetRef(oreDepositSpawner, "oreDepositVisualPrefab", oreDepositVisualPrefab);

        var minerDef = AssetDatabase.LoadAssetAtPath<MachineDef>($"{MachinesPath}/Miner.asset");
        var smelterDef = AssetDatabase.LoadAssetAtPath<MachineDef>($"{MachinesPath}/Smelter.asset");
        var assemblerDef = AssetDatabase.LoadAssetAtPath<MachineDef>($"{MachinesPath}/Assembler.asset");
        if (minerDef == null || smelterDef == null || assemblerDef == null)
        {
            Debug.LogWarning("[SceneBootstrapper] Machine def(s) not found — run Tools > Factory Prototype > Seed Sample Game Data first.");
        }

        BuildPalette(router, machineTool, minerDef, smelterDef, assemblerDef);
        BuildHud(driver);
        BuildRecipePanel(driver);
        BuildGround();
        FrameCamera();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[SceneBootstrapper] Prototype scene built and saved: " + scene.path);
    }

    private static readonly string[] LegacyObjectNames =
    {
        "OreNode", "StorageDepot", "ResourceManager", "RawOreText", "RefinedOreText",
        "Miner", "Smelter", "BeltNodeA", "BeltNodeB", "BeltNodeC",
        "BeltStrip_Segment0", "BeltStrip_Segment1", "BeltRenderer_Segment0", "BeltRenderer_Segment1",
        "DemoSceneSetup",
        "FacingLabel", // 예전 실행에서 캔버스 루트에 잘못 붙였던 버전을 지우고 버튼 자식으로 다시 만든다.
        "PaletteButton_CopperMiner", // 채굴기가 하나로 통합되면서 없어짐(광물 노드 태그로 대체).
    };

    private static void RemoveLegacyObjects()
    {
        foreach (var name in LegacyObjectNames)
        {
            var go = GameObject.Find(name);
            if (go != null) Object.DestroyImmediate(go);
        }
    }

    private const float GroundSize = 40f; // 월드 단위. 원점 중심이라 -20..20 범위를 덮음.

    private static void BuildGround()
    {
        var go = GameObject.Find("Ground");
        if (go == null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Ground";
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        // 기본 Plane은 10x10, 원점 중심으로 -5..5 범위. 스케일 GroundSize/10을 곱하면
        // 왼쪽 가장자리가 -GroundSize/2(정수)에 오게 되어, 타일 경계가 GridUtility의
        // 셀 경계(정수 좌표)와 정확히 맞아떨어진다.
        go.transform.position = new Vector3(0f, -0.02f, 0f);
        go.transform.localScale = new Vector3(GroundSize / 10f, 1f, GroundSize / 10f);

        var texture = GridTextureFactory.CreateGridLineTexture(64, 2, new Color(0.30f, 0.30f, 0.33f), new Color(0.48f, 0.48f, 0.52f));
        var material = BuildVisuals.CreateTiledMaterial(texture, new Vector2(GroundSize, GroundSize));
        go.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static T EnsureComponentOn<T>(GameObject go) where T : Component
    {
        return go.GetComponent<T>() ?? go.AddComponent<T>();
    }

    private static GameObject EnsureEmpty(string name, Vector3 position)
    {
        var go = GameObject.Find(name);
        if (go == null)
        {
            go = new GameObject(name);
            go.transform.position = position;
        }
        return go;
    }

    private static void FrameCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;

        // 기획서상 장르가 "탑다운 시점"이고, 완전 수직으로 내려다보면 바닥 레이캐스트가
        // 항상 카메라 높이만큼의 고정 거리로 안정적이다 (비스듬한 각도에서 화면 위쪽/지평선
        // 근처를 클릭하면 광선이 바닥과 거의 평행해져 교차 거리가 튀는 문제가 있었음).
        cam.orthographic = true;
        cam.orthographicSize = 8f;
        cam.transform.position = new Vector3(0f, 15f, 0f);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private static void BuildPalette(BuildInputRouter router, MachineGhostTool machineTool, MachineDef minerDef, MachineDef smelterDef, MachineDef assemblerDef)
    {
        var canvasGO = GameObject.Find("HUDCanvas");
        if (canvasGO == null)
        {
            canvasGO = new GameObject("HUDCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
        }

        EnsureEventSystem();

        var minerButton = EnsureButton(canvasGO.transform, "PaletteButton_Miner", "채굴기", new Vector2(20, 40));
        WirePaletteButton(minerButton, router, machineTool, minerDef);

        var smelterButton = EnsureButton(canvasGO.transform, "PaletteButton_Smelter", "제련로", new Vector2(150, 40));
        WirePaletteButton(smelterButton, router, machineTool, smelterDef);

        var assemblerButton = EnsureButton(canvasGO.transform, "PaletteButton_Assembler", "조립기", new Vector2(280, 40));
        WirePaletteButton(assemblerButton, router, machineTool, assemblerDef);

        var beltButton = EnsureButton(canvasGO.transform, "PaletteButton_Belt", "벨트", new Vector2(410, 40));
        WirePaletteButton(beltButton, router, machineTool, null, isBeltButton: true);

        // 팔레트(1행, y=40)와 같은 줄에 두면 기계 종류가 늘어날 때마다 배치/확정 버튼과
        // 자리다툼이 난다(실제로 조립기 추가하면서 회전 버튼과 겹쳤음) — 그래서 배치 액션
        // 버튼들은 팔레트 위 2행(y=140)에 따로 둬서 팔레트가 늘어나도 절대 안 겹치게 한다.
        // 방향 표시는 화면 글자 대신 기계 위 화살표(OutputArrow)만 쓰기로 해서 별도 라벨 없음.
        var rotateButton = EnsureButton(canvasGO.transform, "RotateButton", "회전", new Vector2(-270, 140), rightAnchored: true);
        var rotate = rotateButton.gameObject.GetComponent<RotatePlacementButton>() ?? rotateButton.gameObject.AddComponent<RotatePlacementButton>();
        SetRef(rotate, "machineTool", machineTool);
        SetRef(rotate, "button", rotateButton);

        var confirmButton = EnsureButton(canvasGO.transform, "ConfirmButton", "확정", new Vector2(-140, 140), rightAnchored: true);
        var confirm = confirmButton.gameObject.GetComponent<ConfirmPlacementButton>() ?? confirmButton.gameObject.AddComponent<ConfirmPlacementButton>();
        SetRef(confirm, "machineTool", machineTool);
        SetRef(confirm, "router", router);
        SetRef(confirm, "button", confirmButton);
    }

    private static void BuildRecipePanel(SimulationDriver driver)
    {
        var canvasGO = GameObject.Find("HUDCanvas");
        if (canvasGO == null) return;

        var panelGO = GameObject.Find("RecipeSelectionPanel");
        if (panelGO == null)
        {
            panelGO = new GameObject("RecipeSelectionPanel", typeof(RectTransform), typeof(Image));
            panelGO.transform.SetParent(canvasGO.transform, false);
            var rt = panelGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(380f, 700f);
            panelGO.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.92f);
        }

        var containerGO = GameObject.Find("RecipeButtonContainer");
        if (containerGO == null)
        {
            containerGO = new GameObject("RecipeButtonContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
            containerGO.transform.SetParent(panelGO.transform, false);
            var rt = containerGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(20f, 20f);
            rt.offsetMax = new Vector2(-20f, -20f);

            var layout = containerGO.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        var panelComponent = panelGO.GetComponent<RecipeSelectionPanel>() ?? panelGO.AddComponent<RecipeSelectionPanel>();
        SetRef(panelComponent, "panelRoot", panelGO);
        SetRef(panelComponent, "buttonContainer", containerGO.transform);
        SetRef(panelComponent, "driver", driver);
    }

    private static void WirePaletteButton(Button button, BuildInputRouter router, MachineGhostTool machineTool, MachineDef machineDef, bool isBeltButton = false)
    {
        var paletteButton = button.gameObject.GetComponent<BuildPaletteButton>() ?? button.gameObject.AddComponent<BuildPaletteButton>();
        SetRef(paletteButton, "router", router);
        SetRef(paletteButton, "machineTool", machineTool);
        SetRef(paletteButton, "machineDef", machineDef);
        SetBoolField(paletteButton, "isBeltButton", isBeltButton);
        SetRef(paletteButton, "button", button);
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;

        var go = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }

    private static Button EnsureButton(Transform parent, string name, string label, Vector2 anchoredPos, bool rightAnchored = false)
    {
        var existing = GameObject.Find(name);
        GameObject go;
        bool isNew = existing == null;

        if (isNew)
        {
            go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
        }
        else
        {
            go = existing;
        }

        // 위치/앵커/크기는 순전히 코드가 결정하는 레이아웃이라, 이미 있는 버튼이어도 매번
        // 최신 값으로 맞춘다 — 안 그러면(예전엔 여기서 바로 return) 팔레트에 버튼을 새로
        // 추가할 때 기존 버튼이 옛 자리에 그대로 남아서 새 버튼과 겹쳐버린다.
        var rt = go.GetComponent<RectTransform>();
        if (rightAnchored)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
        }
        else
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
        }
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = ButtonSize;

        if (!isNew) return go.GetComponent<Button>();

        var image = go.GetComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 0.85f);

        var textGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textGO.transform.SetParent(go.transform, false);
        var textRt = textGO.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        var text = textGO.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 22;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 12;
        text.resizeTextMaxSize = 22;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = label;

        return go.GetComponent<Button>();
    }

    private static void BuildHud(SimulationDriver driver)
    {
        var canvasGO = GameObject.Find("HUDCanvas");
        if (canvasGO == null) return;

        var line1 = EnsureText(canvasGO.transform, "HudLine1", new Vector2(40, -60));
        var line2 = EnsureText(canvasGO.transform, "HudLine2", new Vector2(40, -110));

        var hudGO = GameObject.Find("ResourceHUD") ?? new GameObject("ResourceHUD");
        var hud = EnsureComponentOn<ResourceHUD>(hudGO);
        SetRef(hud, "line1", line1);
        SetRef(hud, "line2", line2);

        var bridgeGO = GameObject.Find("SimulationHudBridge") ?? new GameObject("SimulationHudBridge");
        var bridge = EnsureComponentOn<SimulationHudBridge>(bridgeGO);
        SetRef(bridge, "driver", driver);
        SetRef(bridge, "hud", hud);
    }


    private static Text EnsureText(Transform parent, string name, Vector2 anchoredPos)
    {
        var existing = GameObject.Find(name);
        if (existing != null) return existing.GetComponent<Text>();

        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(500f, 60f);

        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 40;
        text.color = Color.white;
        text.text = name;
        return text;
    }

    private static void SetRef(Object target, string fieldName, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(fieldName).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetBoolField(Object target, string fieldName, bool value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(fieldName).boolValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}

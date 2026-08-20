using Factory.Buildings;
using Factory.Building;
using UnityEditor;
using UnityEngine;

// 반복 생성되는 오브젝트(채굴기/제련로, 벨트 위 아이템, 배치 고스트)를 프리팹으로 만든다.
// 에셋 없는 프로토타입이라 여전히 기본 도형이지만, 매번 CreatePrimitive로 새로 만드는 대신
// 프리팹을 Instantiate하는 구조로 바꿔서 나중에 진짜 아트로 교체하기 쉽게 한다.
//
// 이미 있는 프리팹은 절대 건드리지 않는다 — 한 번 만든 뒤 색을 직접 칠하거나 수정했을 수
// 있으므로, 다시 실행해도 "없는 것만" 새로 만든다 (DataSeeder의 CreateOrLoad 패턴과 동일).
public static class PrefabBuilder
{
    private const string PrefabsPath = "Assets/Sin/Prefabs";

    [MenuItem("Tools/Factory Prototype/Build Prefabs")]
    public static void BuildPrefabs()
    {
        EnsureFolder(PrefabsPath);

        BuildBeltItemPrefab();
        BuildMachinePrefab("MinerVisual", new Color(0.55f, 0.4f, 0.25f));
        BuildMachinePrefab("ProcessorVisual", new Color(0.6f, 0.15f, 0.1f));
        BuildGhostPrefab();
        BuildBeltStripPrefab();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PrefabBuilder] Prefabs ready under " + PrefabsPath + " (기존 프리팹은 건드리지 않음).");
    }

    private static void BuildBeltItemPrefab()
    {
        if (AlreadyExists("BeltItemVisual")) return;

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * 0.25f;
        BuildVisuals.Colorize(go, new Color(0.85f, 0.85f, 0.85f));

        SaveAndDestroy(go, "BeltItemVisual");
    }

    private static void BuildMachinePrefab(string name, Color color)
    {
        if (AlreadyExists(name)) return;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        BuildVisuals.Colorize(go, color);
        go.AddComponent<MachineView>();

        SaveAndDestroy(go, name);
    }

    private static void BuildGhostPrefab()
    {
        if (AlreadyExists("MachineGhost")) return;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
        BuildVisuals.Colorize(go, new Color(0.9f, 0.2f, 0.2f, 0.45f));

        SaveAndDestroy(go, "MachineGhost");
    }

    private static void BuildBeltStripPrefab()
    {
        if (AlreadyExists("BeltStripVisual")) return;

        // 크기/회전은 BuildVisuals.CreateStrip이 배치 때마다 다시 계산해서 덮어쓴다 —
        // 프리팹은 메쉬/머티리얼 템플릿 역할만 한다.
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        BuildVisuals.Colorize(go, new Color(0.15f, 0.15f, 0.15f));

        SaveAndDestroy(go, "BeltStripVisual");
    }

    private static bool AlreadyExists(string name)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsPath}/{name}.prefab") != null;
    }

    private static void SaveAndDestroy(GameObject go, string name)
    {
        go.name = name;
        string path = $"{PrefabsPath}/{name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        AssetDatabase.CreateFolder("Assets/Sin", "Prefabs");
    }
}

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
    private const string MaterialsPath = "Assets/Sin/Materials";

    [MenuItem("Tools/Factory Prototype/Build Prefabs")]
    public static void BuildPrefabs()
    {
        EnsureFolder(PrefabsPath);
        EnsureFolder(MaterialsPath);

        BuildBeltItemPrefab();
        // 기계 종류마다 전용 프리팹을 둬서(색이라도 다르게) 눈으로 구분되게 한다.
        // 채굴기는 이제 하나뿐이다(뭘 캐는지는 아래 광물 노드가 정함) — 입출력 포트가
        // 없으므로(원격 전송) 출력 화살표를 붙이지 않는다.
        BuildMachinePrefab("MinerVisual", new Color(0.55f, 0.4f, 0.25f), hasOutputPort: false);
        BuildMachinePrefab("SmelterVisual", new Color(0.6f, 0.15f, 0.1f), hasOutputPort: true);
        BuildMachinePrefab("FormerVisual", new Color(0.2f, 0.5f, 0.55f), hasOutputPort: true);
        BuildMachinePrefab("SynthesizerVisual", new Color(0.45f, 0.25f, 0.65f), hasOutputPort: true);
        BuildCorePrefab();
        BuildOreDepositVisualPrefab();
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
        ApplyPersistedMaterial(go, "BeltItemVisual_Mat", new Color(0.85f, 0.85f, 0.85f));

        SaveAndDestroy(go, "BeltItemVisual");
    }

    private static void BuildMachinePrefab(string name, Color color, bool hasOutputPort)
    {
        if (!AlreadyExists(name))
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ApplyPersistedMaterial(go, $"{name}_Body_Mat", color);
            go.AddComponent<MachineView>();
            if (hasOutputPort) AttachOutputArrow(go);

            SaveAndDestroy(go, name);
            return;
        }

        // 이미 있는 프리팹은 색은 그대로 두고, 화살표 표식 유무만 포트 여부에 맞춰 정리한다
        // (기존 커스텀 색칠은 건드리지 않는다).
        EnsureOutputArrowState(name, hasOutputPort);
    }

    private static void EnsureOutputArrowState(string name, bool shouldHaveArrow)
    {
        string path = $"{PrefabsPath}/{name}.prefab";
        var contents = PrefabUtility.LoadPrefabContents(path);
        var existingArrow = contents.transform.Find("OutputArrow");
        bool dirty = false;

        if (shouldHaveArrow)
        {
            if (existingArrow == null)
            {
                AttachOutputArrow(contents);
                dirty = true;
            }
            else
            {
                // 이전 실행에서 즉석 Material로 구워져 null(마젠타)로 저장된 화살표가 있으면
                // 영속 머티리얼로 다시 연결해준다 (오브젝트 자체는 그대로 두고 머티리얼만 복구).
                var renderer = existingArrow.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.sharedMaterial == null)
                {
                    ApplyPersistedMaterial(existingArrow.gameObject, "OutputArrow_Mat", new Color(1f, 0.85f, 0.2f));
                    dirty = true;
                }
            }
        }
        else if (existingArrow != null)
        {
            // 채굴기는 입출력 포트가 없다(원격 전송) — 예전에 잘못 붙였던 화살표를 뗀다.
            Object.DestroyImmediate(existingArrow.gameObject, true);
            dirty = true;
        }

        if (dirty) PrefabUtility.SaveAsPrefabAsset(contents, path);
        PrefabUtility.UnloadPrefabContents(contents);
    }

    private static readonly Vector3 CoreScale = new Vector3(2f, 1.4f, 2f);

    private static void BuildCorePrefab()
    {
        if (!AlreadyExists("CoreVisual"))
        {
            // 시작부터 놓여있는 거점이라 다른 기계보다 한눈에 띄게 크고(2x2 칸) 색도 다르게 한다.
            // 화살표는 안 붙인다 — 코어는 4면 다 입출력 가능해서 특정 방향을 가리킬 필요가 없음.
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.localScale = CoreScale;
            ApplyPersistedMaterial(go, "CoreVisual_Mat", new Color(0.25f, 0.45f, 0.6f));
            go.AddComponent<MachineView>();

            SaveAndDestroy(go, "CoreVisual");
            return;
        }

        // 이전엔 1.4 크기(장식용)로 만들었는데 이제 실제로 2x2 칸을 차지하게 됐으므로
        // 크기만 맞춰준다 (색은 손대지 않음).
        EnsureCoreScale();
    }

    private static void EnsureCoreScale()
    {
        const string path = PrefabsPath + "/CoreVisual.prefab";
        var contents = PrefabUtility.LoadPrefabContents(path);
        bool dirty = false;

        if (contents.transform.localScale != CoreScale)
        {
            contents.transform.localScale = CoreScale;
            dirty = true;
        }

        // 예전(-nographics 배치 모드) 실행에서 즉석 Material로 구워져 null(마젠타)로 남은
        // 경우만 복구한다 — 이미 색이 있으면(직접 칠했으면) 절대 건드리지 않는다.
        var renderer = contents.GetComponent<MeshRenderer>();
        if (renderer != null && renderer.sharedMaterial == null)
        {
            ApplyPersistedMaterial(contents, "CoreVisual_Mat", new Color(0.25f, 0.45f, 0.6f));
            dirty = true;
        }

        if (dirty) PrefabUtility.SaveAsPrefabAsset(contents, path);
        PrefabUtility.UnloadPrefabContents(contents);
    }

    private static void BuildOreDepositVisualPrefab()
    {
        if (AlreadyExists("OreDepositVisual")) return;

        // 순수 시각 표식(탭 불가) — 실제 색은 OreDepositSpawner가 자원 색으로 매 인스턴스마다
        // 다시 칠한다(BuildVisuals.Colorize, 런타임). 여기 머티리얼은 그 전까지의 기본값일 뿐.
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.localScale = new Vector3(0.95f, 0.05f, 0.95f);
        ApplyPersistedMaterial(go, "OreDepositVisual_Mat", new Color(0.5f, 0.5f, 0.5f));

        SaveAndDestroy(go, "OreDepositVisual");
    }

    // 기계의 로컬 +Z(출력 쪽)에 화살표(삼각형) 표식을 붙인다. 부모가 Facing만큼 회전되면
    // 화살표도 같이 돌아서 항상 실제 출력 방향을 가리킨다. 입력 쪽은 화살표가 없는 반대편이다.
    private static void AttachOutputArrow(GameObject parent)
    {
        var arrowGO = new GameObject("OutputArrow", typeof(MeshFilter), typeof(MeshRenderer));
        arrowGO.transform.SetParent(parent.transform, false);
        arrowGO.transform.localPosition = new Vector3(0f, 0.51f, 0f);

        arrowGO.GetComponent<MeshFilter>().sharedMesh = CreateArrowMesh();
        ApplyPersistedMaterial(arrowGO, "OutputArrow_Mat", new Color(1f, 0.85f, 0.2f));
    }

    private static Mesh CreateArrowMesh()
    {
        // 위(+Y, 탑다운 카메라 쪽)에서 봤을 때 로컬 +Z를 가리키는 평평한 삼각형(">").
        // 앞뒤 양면(반대 winding)을 다 넣어서 컬링 방향을 신경 안 써도 항상 보이게 한다.
        var mesh = new Mesh { name = "ArrowIndicator" };
        var vertices = new[]
        {
            new Vector3(0f, 0f, 0.34f),
            new Vector3(-0.16f, 0f, 0.06f),
            new Vector3(0.16f, 0f, 0.06f),
        };
        var triangles = new[] { 0, 1, 2, 0, 2, 1 };
        var normals = new[] { Vector3.up, Vector3.up, Vector3.up };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.normals = normals;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void BuildGhostPrefab()
    {
        if (AlreadyExists("MachineGhost")) return;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
        ApplyPersistedMaterial(go, "MachineGhost_Mat", new Color(0.9f, 0.2f, 0.2f, 0.45f));

        SaveAndDestroy(go, "MachineGhost");
    }

    private static void BuildBeltStripPrefab()
    {
        if (AlreadyExists("BeltStripVisual")) return;

        // 크기/회전은 BuildVisuals.CreateStrip이 배치 때마다 다시 계산해서 덮어쓴다 —
        // 프리팹은 메쉬/머티리얼 템플릿 역할만 한다.
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        ApplyPersistedMaterial(go, "BeltStripVisual_Mat", new Color(0.15f, 0.15f, 0.15f));

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

    // 배치 모드(특히 LoadPrefabContents로 기존 프리팹에 자식을 덧붙이는 경로)에서는 즉석으로
    // 만든 Material 객체가 프리팹에 제대로 직렬화되지 않고 null(마젠타)로 저장되는 문제가
    // 있었다. 그래서 프리팹 굽기용 머티리얼은 항상 실제 .mat 에셋으로 영속화해서 참조한다 —
    // 부수 효과로 사용자가 직접 클릭해서 색을 칠하기도 편해진다. 이미 있으면(=전에 직접
    // 칠했을 수 있으면) 그 색을 그대로 쓰고 절대 덮어쓰지 않는다.
    private static void ApplyPersistedMaterial(GameObject go, string materialName, Color defaultColor)
    {
        var renderer = go.GetComponent<Renderer>();
        if (renderer == null) return;

        string path = $"{MaterialsPath}/{materialName}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(FindDefaultShader()) { color = defaultColor };
            if (defaultColor.a < 1f) ConfigureTransparent(mat);
            AssetDatabase.CreateAsset(mat, path);
        }

        renderer.sharedMaterial = mat;
    }

    private static void ConfigureTransparent(Material mat)
    {
        mat.SetFloat("_Surface", 1f); // 0=Opaque, 1=Transparent
        mat.SetFloat("_Blend", 0f); // Alpha blend
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private static Shader FindDefaultShader() => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int lastSlash = path.LastIndexOf('/');
        string parent = path.Substring(0, lastSlash);
        string folderName = path.Substring(lastSlash + 1);
        AssetDatabase.CreateFolder(parent, folderName);
    }
}

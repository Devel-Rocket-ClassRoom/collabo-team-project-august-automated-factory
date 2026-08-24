using System.IO;
using Factory.Data;
using UnityEditor;
using UnityEngine;

// 예시 게임 데이터(자원/기계/레시피) 애셋을 Assets/Sin/Resources/GameData 아래에 만든다.
// 여기 만든 애셋들은 데이터일 뿐이고, 새 자원/레시피를 추가하려면 이 스크립트를 고칠 필요 없이
// 같은 폴더에 애셋만 새로 만들면 GameDatabase가 자동으로 인식한다.
public static class DataSeeder
{
    private const string ResourcesPath = "Assets/Sin/Resources/GameData/Resources";
    private const string RecipesPath = "Assets/Sin/Resources/GameData/Recipes";
    private const string MachinesPath = "Assets/Sin/Resources/GameData/Machines";
    private const string OreDepositsPath = "Assets/Sin/Resources/GameData/OreDeposits";
    private const string PrefabsPath = "Assets/Sin/Prefabs";

    [MenuItem("Tools/Factory Prototype/Seed Sample Game Data")]
    public static void SeedSampleData()
    {
        EnsureFolder(ResourcesPath);
        EnsureFolder(RecipesPath);
        EnsureFolder(MachinesPath);
        EnsureFolder(OreDepositsPath);

        // 에셋 없는 프로토타입이라 자원별 프리팹 대신 벨트 위 아이템 색으로 구분한다.
        var ironOre = CreateOrLoadResource("IronOre", "철광석", new Color(0.45f, 0.32f, 0.22f));
        var ironPlate = CreateOrLoadResource("IronPlate", "철판", new Color(0.75f, 0.75f, 0.78f));
        var gear = CreateOrLoadResource("Gear", "기어", new Color(0.85f, 0.7f, 0.15f));
        var copperOre = CreateOrLoadResource("CopperOre", "구리광석", new Color(0.85f, 0.45f, 0.2f));

        // 채굴기는 이제 하나뿐이다 — 뭘 캐는지는 땅 위 광물 노드(OreDepositDef)가 정한다.
        CreateOrLoadMachine("Miner", "채굴기", MachineCategory.Miner);
        CreateOrLoadOreDeposit("IronOreDeposit", ironOre);
        CreateOrLoadOreDeposit("CopperOreDeposit", copperOre);

        var smelterDef = CreateOrLoadMachine("Smelter", "제련로", MachineCategory.Smelter);
        // 조립기는 서로 다른 두 자원을 각자 다른 벨트로 동시에 받아야 진짜 "조립"이라, 2x2
        // 블록에 입력/출력 포트를 각 면 2칸씩 둔다(GridUtility.GetPortCells 참고).
        CreateOrLoadMachine("Assembler", "조립기", MachineCategory.Assembler, new Vector2Int(2, 2));
        CreateOrLoadMachine("Core", "코어", MachineCategory.Storage);

        CreateOrLoadRecipe("SmeltIron", ironOre, 2, ironPlate, 1, 2f, MachineCategory.Smelter);
        // 3단계 생산 트리(광석 -> 철판/구리광석 -> 기어): 조립기가 서로 다른 두 자원(철판 +
        // 구리광석)을 각각 다른 입력 포트로 받아서 소비한다 — 구리는 별도 제련 없이 광석
        // 그대로 투입해서 스코프를 최소화한다.
        CreateOrLoadRecipe("AssembleGear", ironPlate, 1, copperOre, 1, gear, 1, 3f, MachineCategory.Assembler);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[DataSeeder] Sample game data seeded under Assets/Sin/Resources/GameData/.");
    }

    private static ResourceDef CreateOrLoadResource(string id, string displayName, Color? color = null)
    {
        string path = $"{ResourcesPath}/{id}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<ResourceDef>(path);
        if (existing != null)
        {
            if (color.HasValue && existing.color != color.Value)
            {
                existing.color = color.Value;
                EditorUtility.SetDirty(existing);
            }
            return existing;
        }

        var def = ScriptableObject.CreateInstance<ResourceDef>();
        def.resourceId = id;
        def.displayName = displayName;
        if (color.HasValue) def.color = color.Value;
        AssetDatabase.CreateAsset(def, path);
        return def;
    }

    private static MachineDef CreateOrLoadMachine(string id, string displayName, MachineCategory category, Vector2Int? footprint = null)
    {
        string path = $"{MachinesPath}/{id}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<MachineDef>(path);
        if (existing != null)
        {
            // footprint는 순전히 설계값이라(사용자가 손으로 칠하는 색 같은 게 아님) 코드에서
            // 지정한 값과 다르면 바로잡는다.
            if (footprint.HasValue && existing.footprint != footprint.Value)
            {
                existing.footprint = footprint.Value;
                EditorUtility.SetDirty(existing);
            }
            AssignVisualPrefabIfMissing(existing, id);
            return existing;
        }

        var def = ScriptableObject.CreateInstance<MachineDef>();
        def.machineId = id;
        def.displayName = displayName;
        def.category = category;
        if (footprint.HasValue) def.footprint = footprint.Value;
        AssetDatabase.CreateAsset(def, path);
        AssignVisualPrefabIfMissing(def, id);
        return def;
    }

    private static OreDepositDef CreateOrLoadOreDeposit(string id, ResourceDef resource, float mineIntervalSeconds = -1f, int yieldPerCycle = 1)
    {
        string path = $"{OreDepositsPath}/{id}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<OreDepositDef>(path);
        if (existing != null) return existing;

        var def = ScriptableObject.CreateInstance<OreDepositDef>();
        def.depositId = id;
        def.resource = resource;
        if (mineIntervalSeconds > 0f) def.mineIntervalSeconds = mineIntervalSeconds;
        def.yieldPerCycle = yieldPerCycle;
        AssetDatabase.CreateAsset(def, path);
        return def;
    }

    // 기계 id와 같은 이름의 프리팹("{id}Visual")을 PrefabBuilder가 만들어뒀으면 연결해준다
    // (Tools > Factory Prototype > Build Prefabs를 먼저/나중에 실행해도 재실행 시 알아서 채워짐).
    private static void AssignVisualPrefabIfMissing(MachineDef def, string id)
    {
        if (def.visualPrefab != null) return;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsPath}/{id}Visual.prefab");
        if (prefab == null) return;

        def.visualPrefab = prefab;
        EditorUtility.SetDirty(def);
    }

    private static RecipeDef CreateOrLoadRecipe(string id, ResourceDef input, int inputAmount, ResourceDef output, int outputAmount, float seconds, MachineCategory category)
    {
        string path = $"{RecipesPath}/{id}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<RecipeDef>(path);
        if (existing != null) return existing;

        var def = ScriptableObject.CreateInstance<RecipeDef>();
        def.recipeId = id;
        def.inputs = new[] { new RecipeIngredient { resource = input, amount = inputAmount } };
        def.outputs = new[] { new RecipeIngredient { resource = output, amount = outputAmount } };
        def.processSeconds = seconds;
        def.requiredCategory = category;
        AssetDatabase.CreateAsset(def, path);
        return def;
    }

    // 서로 다른 두 자원을 입력으로 받는 레시피(조립기용).
    private static RecipeDef CreateOrLoadRecipe(string id, ResourceDef inputA, int amountA, ResourceDef inputB, int amountB, ResourceDef output, int outputAmount, float seconds, MachineCategory category)
    {
        string path = $"{RecipesPath}/{id}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<RecipeDef>(path);
        if (existing != null) return existing;

        var def = ScriptableObject.CreateInstance<RecipeDef>();
        def.recipeId = id;
        def.inputs = new[]
        {
            new RecipeIngredient { resource = inputA, amount = amountA },
            new RecipeIngredient { resource = inputB, amount = amountB },
        };
        def.outputs = new[] { new RecipeIngredient { resource = output, amount = outputAmount } };
        def.processSeconds = seconds;
        def.requiredCategory = category;
        AssetDatabase.CreateAsset(def, path);
        return def;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folderName = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folderName);
    }
}

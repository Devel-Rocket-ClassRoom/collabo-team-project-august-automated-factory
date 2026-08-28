using System.Collections.Generic;
using System.IO;
using Bae.EditorScripts;
using Bae.SO;
using Factory.Data;
using UnityEditor;
using UnityEngine;

// Bae님의 데이터 파이프라인(ItemSO/MachineSO/RecipeSO -> JSON Bake -> DataManager)에 게임이
// 실제로 돌아가는 데 필요한 예시 데이터를 채운다.
//
// 주의: Bae.EditorScripts.DataExporter.BakeDataToJSON()은 Items/Machines/Recipes를
// 한꺼번에 다시 굽는다 — 그래서 Items.json에 이미 있던 실제 데이터(IronOre/IronIngot/
// IronPlate/Coal, Bae님이 채워둔 것)를 만든 ItemSO 원본 애셋이 지금 디스크에 없는 상태에서
// 그냥 Bake를 돌리면 "지금 있는 ItemSO 0개"로 다시 구워져서 그 데이터가 통째로 날아간다.
// 그래서 이 스크립트는 먼저 Items.json 내용 그대로 ItemSO를 복원한 다음 기계/레시피를 채운다.
// 새 아이템은 팀 확인된 것만 추가한다 — SteelIngot(강철 주괴)은 2026-08-27 합의됨(2티어 합성 체인).
// Gear/CopperOre 등 미확인 아이템은 여전히 추가하지 않는다.
public static class BaeDataSeeder
{
    private const string ItemsPath = "Assets/Bae/Data/Items";
    private const string MachinesPath = "Assets/Bae/Data/Machines";
    private const string RecipesPath = "Assets/Bae/Data/Recipes";
    private const string OreDepositsPath = "Assets/Sin/Resources/GameData/OreDeposits";

    [MenuItem("Tools/Factory Prototype/Seed Sample Game Data (Bae Format)")]
    public static void SeedSampleData()
    {
        EnsureFolder(ItemsPath);
        EnsureFolder(MachinesPath);
        EnsureFolder(RecipesPath);
        EnsureFolder(OreDepositsPath);

        // Items.json에 이미 있던 내용 그대로 복원(Bake가 지우지 않게) — Bae님이 값을
        // 바꾸셨으면 이 시더도 다시 실행해서 맞춰야 한다는 뜻이라, 값이 다르면 덮어쓴다.
        CreateOrUpdateItem("IronOre", "철광석", "기본 구조재의 원료가 되는 광석입니다.", "Icon_IronOre", "Prefab_Item_IronOre");
        CreateOrUpdateItem("IronIngot", "철 주괴", "철광석을 제련하여 만든 주괴입니다.", "Icon_IronIngot", "Prefab_Item_IronIngot");
        CreateOrUpdateItem("IronPlate", "철판", "건축과 기계 부품에 폭넓게 쓰이는 기초 재료입니다.", "Icon_IronPlate", "Prefab_Item_IronPlate");
        CreateOrUpdateItem("Coal", "석탄", "전력 발전과 합금 제작에 쓰이는 연료입니다.", "Icon_Coal", "Prefab_Item_Coal");
        CreateOrUpdateItem("SteelIngot", "강철 주괴", "철 주괴와 석탄을 합성해 만든 고강도 주괴입니다.", "Icon_SteelIngot", "Prefab_Item_SteelIngot");

        CreateOrLoadOreDeposit("IronOreDeposit", "IronOre");
        CreateOrLoadOreDeposit("CoalDeposit", "Coal");

        CreateOrLoadMachine("Miner", "채굴기", 1, 1, inputSlots: 0, outputSlots: 1);
        CreateOrLoadMachine("Smelter", "제련로", 1, 1, inputSlots: 1, outputSlots: 1);
        // 성형기: 제련로와 완전히 같은 I/O 모양(1입력/1출력)이라 시뮬레이션 코드는 그대로
        // 재사용되고 데이터만 추가하면 된다 — 주괴를 단일 부품(철판 등)으로 성형한다.
        CreateOrLoadMachine("Former", "성형기", 1, 1, inputSlots: 1, outputSlots: 1);
        // 합성기(구 조립기): 2x2에 입력 포트가 2칸이라 서로 다른 두 자원을 각각 다른 벨트로
        // 받아 합금으로 합성한다.
        CreateOrLoadMachine("Synthesizer", "합성기", 2, 2, inputSlots: 2, outputSlots: 1);
        // 코어는 레시피 개념이 없는 순수 저장소(UniversalPorts, CoreSpawner.cs에서 하드코딩)라
        // 여기 슬롯 수는 실제로 안 쓰이지만, machineID 조회는 돼야 하니 등록은 해둔다.
        CreateOrLoadMachine("Core", "코어", 2, 2, inputSlots: 0, outputSlots: 0);

        // 이름이 바뀐 옛 애셋 정리 — 안 그러면 t:MachineSO 스캔에 옛 "Assembler"가 계속 잡혀
        // Machines.json에 유령 항목으로 다시 구워진다.
        DeleteAssetIfExists($"{MachinesPath}/Assembler.asset");

        // V2 스펙 티어 체인: 철광석 -(제련로)-> 철 주괴 -(성형기)-> 철판 (전부 단일 입력).
        CreateOrLoadRecipe("SmeltIronOre", "Smelter", 2f,
            new List<string> { "IronOre" }, new List<string> { "IronIngot" });
        CreateOrLoadRecipe("FormIronPlate", "Former", 2f,
            new List<string> { "IronIngot" }, new List<string> { "IronPlate" });
        // 합성기는 서로 다른 두 자원(철 주괴 + 석탄)을 각각 다른 벨트로 받아 강철 주괴로 합성한다.
        CreateOrLoadRecipe("SynthesizeSteelIngot", "Synthesizer", 2f,
            new List<string> { "IronIngot", "Coal" }, new List<string> { "SteelIngot" });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        DataExporter.BakeDataToJSON();

        Debug.Log("[BaeDataSeeder] Sample game data seeded (Bae format) and baked to JSON.");
    }

    private static void CreateOrUpdateItem(string id, string displayName, string description, string iconName, string prefabName)
    {
        string path = $"{ItemsPath}/{id}.asset";
        var so = AssetDatabase.LoadAssetAtPath<ItemSO>(path);
        bool isNew = so == null;
        if (isNew) so = ScriptableObject.CreateInstance<ItemSO>();

        so.itemID = id;
        so.itemName = displayName;
        so.description = description;
        so.iconName = iconName;
        so.prefabName = prefabName;

        if (isNew) AssetDatabase.CreateAsset(so, path);
        else EditorUtility.SetDirty(so);
    }

    private static MachineSO CreateOrLoadMachine(string id, string displayName, int gridWidth, int gridHeight, int inputSlots, int outputSlots)
    {
        string path = $"{MachinesPath}/{id}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<MachineSO>(path);
        if (existing != null)
        {
            // 크기/슬롯은 순전히 설계값이라(사용자가 손으로 칠하는 값이 아님) 코드에서 지정한
            // 값과 다르면 바로잡는다.
            bool dirty = false;
            if (existing.gridWidth != gridWidth) { existing.gridWidth = gridWidth; dirty = true; }
            if (existing.gridHeight != gridHeight) { existing.gridHeight = gridHeight; dirty = true; }
            if (existing.inputSlots != inputSlots) { existing.inputSlots = inputSlots; dirty = true; }
            if (existing.outputSlots != outputSlots) { existing.outputSlots = outputSlots; dirty = true; }
            if (dirty) EditorUtility.SetDirty(existing);
            return existing;
        }

        var so = ScriptableObject.CreateInstance<MachineSO>();
        so.machineID = id;
        so.machineName = displayName;
        so.gridWidth = gridWidth;
        so.gridHeight = gridHeight;
        so.inputSlots = inputSlots;
        so.outputSlots = outputSlots;
        AssetDatabase.CreateAsset(so, path);
        return so;
    }

    private static RecipeSO CreateOrLoadRecipe(string id, string machineId, float timeToCraft, List<string> inputs, List<string> outputs)
    {
        string path = $"{RecipesPath}/{id}.asset";
        var so = AssetDatabase.LoadAssetAtPath<RecipeSO>(path);
        bool isNew = so == null;
        if (isNew) so = ScriptableObject.CreateInstance<RecipeSO>();

        // 재료 구성처럼 순전히 설계값인 건 코드에서 지정한 값과 다르면 바로잡는다 — 안 그러면
        // (실제로 겪음) 레시피 재료를 여기서 바꿔도 이미 있는 애셋엔 예전 값이 그대로 남는다.
        so.recipeID = id;
        so.machineID = machineId;
        so.timeToCraft = timeToCraft;
        so.inputItems = inputs;
        so.outputItems = outputs;

        if (isNew) AssetDatabase.CreateAsset(so, path);
        else EditorUtility.SetDirty(so);
        return so;
    }

    private static OreDepositDef CreateOrLoadOreDeposit(string id, string resourceId, float mineIntervalSeconds = -1f, int yieldPerCycle = 1)
    {
        string path = $"{OreDepositsPath}/{id}.asset";
        var def = AssetDatabase.LoadAssetAtPath<OreDepositDef>(path);
        bool isNew = def == null;
        if (isNew) def = ScriptableObject.CreateInstance<OreDepositDef>();

        def.depositId = id;
        def.resourceId = resourceId;
        if (mineIntervalSeconds > 0f) def.mineIntervalSeconds = mineIntervalSeconds;
        def.yieldPerCycle = yieldPerCycle;

        if (isNew) AssetDatabase.CreateAsset(def, path);
        else EditorUtility.SetDirty(def);
        return def;
    }

    private static void DeleteAssetIfExists(string assetPath)
    {
        if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)))
        {
            AssetDatabase.DeleteAsset(assetPath);
        }
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

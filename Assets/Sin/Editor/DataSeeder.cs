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

    [MenuItem("Tools/Factory Prototype/Seed Sample Game Data")]
    public static void SeedSampleData()
    {
        EnsureFolder(ResourcesPath);
        EnsureFolder(RecipesPath);
        EnsureFolder(MachinesPath);

        var ironOre = CreateOrLoadResource("IronOre", "철광석");
        var ironPlate = CreateOrLoadResource("IronPlate", "철판");
        var copperOre = CreateOrLoadResource("CopperOre", "구리광석");
        var copperPlate = CreateOrLoadResource("CopperPlate", "구리판");

        var minerDef = CreateOrLoadMachine("Miner", "채굴기", MachineCategory.Miner);
        if (minerDef.minerOutput == null)
        {
            minerDef.minerOutput = ironOre;
            EditorUtility.SetDirty(minerDef);
        }

        var smelterDef = CreateOrLoadMachine("Smelter", "제련로", MachineCategory.Smelter);

        CreateOrLoadRecipe("SmeltIron", ironOre, 2, ironPlate, 1, 2f, MachineCategory.Smelter);
        CreateOrLoadRecipe("SmeltCopper", copperOre, 2, copperPlate, 1, 2f, MachineCategory.Smelter);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[DataSeeder] Sample game data seeded under Assets/Sin/Resources/GameData/.");
    }

    private static ResourceDef CreateOrLoadResource(string id, string displayName)
    {
        string path = $"{ResourcesPath}/{id}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<ResourceDef>(path);
        if (existing != null) return existing;

        var def = ScriptableObject.CreateInstance<ResourceDef>();
        def.resourceId = id;
        def.displayName = displayName;
        AssetDatabase.CreateAsset(def, path);
        return def;
    }

    private static MachineDef CreateOrLoadMachine(string id, string displayName, MachineCategory category)
    {
        string path = $"{MachinesPath}/{id}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<MachineDef>(path);
        if (existing != null) return existing;

        var def = ScriptableObject.CreateInstance<MachineDef>();
        def.machineId = id;
        def.displayName = displayName;
        def.category = category;
        AssetDatabase.CreateAsset(def, path);
        return def;
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

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folderName = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folderName);
    }
}

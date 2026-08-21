using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Factory.Data
{
    public readonly struct ResourceRuntime
    {
        public readonly string Key;
        public readonly string DisplayName;

        public ResourceRuntime(string key, string displayName)
        {
            Key = key;
            DisplayName = displayName;
        }
    }

    public readonly struct RecipeRuntime
    {
        public readonly string Key;
        public readonly ResourceAmount[] Inputs;
        public readonly ResourceAmount[] Outputs;
        public readonly float ProcessSeconds;
        public readonly MachineCategory RequiredCategory;

        public RecipeRuntime(string key, ResourceAmount[] inputs, ResourceAmount[] outputs, float processSeconds, MachineCategory requiredCategory)
        {
            Key = key;
            Inputs = inputs;
            Outputs = outputs;
            ProcessSeconds = processSeconds;
            RequiredCategory = requiredCategory;
        }
    }

    public readonly struct MachineRuntime
    {
        public readonly string Key;
        public readonly MachineCategory Category;
        public readonly Vector2Int Footprint;
        public readonly float SpeedMultiplier;
        public readonly int MinerOutputResourceId; // Category != Miner이면 -1

        public MachineRuntime(string key, MachineCategory category, Vector2Int footprint, float speedMultiplier, int minerOutputResourceId)
        {
            Key = key;
            Category = category;
            Footprint = footprint;
            SpeedMultiplier = speedMultiplier;
            MinerOutputResourceId = minerOutputResourceId;
        }
    }

    // 레시피/자원/기계 정의 데이터베이스. ScriptableObject 애셋(디자인 타임 데이터)을
    // 시뮬레이션이 실제로 순회할 평범한 배열(런타임 데이터)로 1회 변환해서 들고 있는다.
    // 틱 루프는 이 클래스의 배열만 인덱싱하고, ScriptableObject/Dictionary는 로드 시점에만 쓰인다.
    public sealed class GameDatabase
    {
        public static GameDatabase Instance { get; private set; }

        public IReadOnlyList<ResourceRuntime> Resources => resources;
        public IReadOnlyList<RecipeRuntime> Recipes => recipes;
        public IReadOnlyList<MachineRuntime> Machines => machines;

        public int ResourceCount => resources.Length;

        private readonly ResourceRuntime[] resources;
        private readonly RecipeRuntime[] recipes;
        private readonly MachineRuntime[] machines;

        private readonly Dictionary<string, int> resourceIdByKey;
        private readonly Dictionary<string, int> recipeIdByKey;
        private readonly Dictionary<string, int> machineIdByKey;

        private GameDatabase(ResourceRuntime[] resources, RecipeRuntime[] recipes, MachineRuntime[] machines,
            Dictionary<string, int> resourceIdByKey, Dictionary<string, int> recipeIdByKey, Dictionary<string, int> machineIdByKey)
        {
            this.resources = resources;
            this.recipes = recipes;
            this.machines = machines;
            this.resourceIdByKey = resourceIdByKey;
            this.recipeIdByKey = recipeIdByKey;
            this.machineIdByKey = machineIdByKey;
        }

        public int GetResourceId(string key) => resourceIdByKey[key];
        public int GetRecipeId(string key) => recipeIdByKey[key];
        public int GetMachineId(string key) => machineIdByKey[key];

        public bool TryGetResourceId(string key, out int id) => resourceIdByKey.TryGetValue(key, out id);
        public bool TryGetRecipeId(string key, out int id) => recipeIdByKey.TryGetValue(key, out id);
        public bool TryGetMachineId(string key, out int id) => machineIdByKey.TryGetValue(key, out id);

        // 레시피 선택 UI에서 이 기계 카테고리로 고를 수 있는 레시피 목록을 보여줄 때 쓴다.
        public List<int> GetRecipeIdsForCategory(MachineCategory category)
        {
            var result = new List<int>();
            for (int i = 0; i < recipes.Length; i++)
            {
                if (recipes[i].RequiredCategory == category) result.Add(i);
            }
            return result;
        }

        public void MakeGlobal() => Instance = this;

        // Resources.LoadAll은 Unity 매직 폴더(Assets/Resources/...) 아래를 스캔한다.
        // 새 레시피/자원/기계 애셋을 이 폴더에 추가하는 것만으로 코드 수정 없이 인식된다.
        public static GameDatabase LoadFromResources(string rootPath = "GameData")
        {
            var resourceDefs = UnityEngine.Resources.LoadAll<ResourceDef>($"{rootPath}/Resources");
            var recipeDefs = UnityEngine.Resources.LoadAll<RecipeDef>($"{rootPath}/Recipes");
            var machineDefs = UnityEngine.Resources.LoadAll<MachineDef>($"{rootPath}/Machines");
            return Build(resourceDefs, recipeDefs, machineDefs);
        }

        public static GameDatabase Build(ResourceDef[] resourceDefs, RecipeDef[] recipeDefs, MachineDef[] machineDefs)
        {
            // Resources.LoadAll의 순서는 플랫폼/빌드마다 보장되지 않으므로, id 문자열로 정렬해
            // 실행마다 동일한 int id가 배정되도록 한다 (결정적 동작, 저장 데이터 안정성).
            var sortedResources = resourceDefs.OrderBy(r => r.resourceId, StringComparer.Ordinal).ToArray();
            var sortedMachines = machineDefs.OrderBy(m => m.machineId, StringComparer.Ordinal).ToArray();
            var sortedRecipes = recipeDefs.OrderBy(r => r.recipeId, StringComparer.Ordinal).ToArray();

            var resourceIdByKey = new Dictionary<string, int>(sortedResources.Length);
            var resources = new ResourceRuntime[sortedResources.Length];
            for (int i = 0; i < sortedResources.Length; i++)
            {
                var def = sortedResources[i];
                resourceIdByKey[def.resourceId] = i;
                resources[i] = new ResourceRuntime(def.resourceId, def.displayName);
            }

            var machineIdByKey = new Dictionary<string, int>(sortedMachines.Length);
            var machines = new MachineRuntime[sortedMachines.Length];
            for (int i = 0; i < sortedMachines.Length; i++)
            {
                var def = sortedMachines[i];
                machineIdByKey[def.machineId] = i;
                int minerOutputResourceId = def.minerOutput != null && resourceIdByKey.TryGetValue(def.minerOutput.resourceId, out int rid) ? rid : -1;
                machines[i] = new MachineRuntime(def.machineId, def.category, def.footprint, def.speedMultiplier, minerOutputResourceId);
            }

            var recipeIdByKey = new Dictionary<string, int>(sortedRecipes.Length);
            var recipes = new RecipeRuntime[sortedRecipes.Length];
            for (int i = 0; i < sortedRecipes.Length; i++)
            {
                var def = sortedRecipes[i];
                recipeIdByKey[def.recipeId] = i;
                recipes[i] = new RecipeRuntime(
                    def.recipeId,
                    ResolveIngredients(def.inputs, resourceIdByKey),
                    ResolveIngredients(def.outputs, resourceIdByKey),
                    def.processSeconds,
                    def.requiredCategory);
            }

            return new GameDatabase(resources, recipes, machines, resourceIdByKey, recipeIdByKey, machineIdByKey);
        }

        private static ResourceAmount[] ResolveIngredients(RecipeIngredient[] ingredients, Dictionary<string, int> resourceIdByKey)
        {
            if (ingredients == null || ingredients.Length == 0) return Array.Empty<ResourceAmount>();

            var resolved = new ResourceAmount[ingredients.Length];
            for (int i = 0; i < ingredients.Length; i++)
            {
                var ingredient = ingredients[i];
                int resourceId = resourceIdByKey[ingredient.resource.resourceId];
                resolved[i] = new ResourceAmount(resourceId, ingredient.amount);
            }
            return resolved;
        }
    }
}

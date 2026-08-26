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
        public readonly Color Color;

        public ResourceRuntime(string key, string displayName, Color color)
        {
            Key = key;
            DisplayName = displayName;
            Color = color;
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

        public MachineRuntime(string key, MachineCategory category, Vector2Int footprint, float speedMultiplier)
        {
            Key = key;
            Category = category;
            Footprint = footprint;
            SpeedMultiplier = speedMultiplier;
        }
    }

    // 땅 위 광물 노드 하나의 런타임 정보 — 채굴기는 이 노드 위에 지어져야만 생기고, 이 노드의
    // 자원/속도/산출량을 그대로 물려받는다(OreDepositDef 참고).
    public readonly struct OreDepositRuntime
    {
        public readonly string Key;
        public readonly int ResourceId;
        public readonly float MineIntervalSeconds;
        public readonly int YieldPerCycle;

        public OreDepositRuntime(string key, int resourceId, float mineIntervalSeconds, int yieldPerCycle)
        {
            Key = key;
            ResourceId = resourceId;
            MineIntervalSeconds = mineIntervalSeconds;
            YieldPerCycle = yieldPerCycle;
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
        public IReadOnlyList<OreDepositRuntime> OreDeposits => oreDeposits;

        public int ResourceCount => resources.Length;

        private readonly ResourceRuntime[] resources;
        private readonly RecipeRuntime[] recipes;
        private readonly MachineRuntime[] machines;
        private readonly OreDepositRuntime[] oreDeposits;

        private readonly Dictionary<string, int> resourceIdByKey;
        private readonly Dictionary<string, int> recipeIdByKey;
        private readonly Dictionary<string, int> machineIdByKey;
        private readonly Dictionary<string, int> oreDepositIdByKey;

        private GameDatabase(ResourceRuntime[] resources, RecipeRuntime[] recipes, MachineRuntime[] machines, OreDepositRuntime[] oreDeposits,
            Dictionary<string, int> resourceIdByKey, Dictionary<string, int> recipeIdByKey, Dictionary<string, int> machineIdByKey, Dictionary<string, int> oreDepositIdByKey)
        {
            this.resources = resources;
            this.recipes = recipes;
            this.machines = machines;
            this.oreDeposits = oreDeposits;
            this.resourceIdByKey = resourceIdByKey;
            this.recipeIdByKey = recipeIdByKey;
            this.machineIdByKey = machineIdByKey;
            this.oreDepositIdByKey = oreDepositIdByKey;
        }

        public int GetResourceId(string key) => resourceIdByKey[key];
        public int GetRecipeId(string key) => recipeIdByKey[key];
        public int GetMachineId(string key) => machineIdByKey[key];
        public int GetOreDepositId(string key) => oreDepositIdByKey[key];

        public bool TryGetResourceId(string key, out int id) => resourceIdByKey.TryGetValue(key, out id);
        public bool TryGetRecipeId(string key, out int id) => recipeIdByKey.TryGetValue(key, out id);
        public bool TryGetMachineId(string key, out int id) => machineIdByKey.TryGetValue(key, out id);
        public bool TryGetOreDepositId(string key, out int id) => oreDepositIdByKey.TryGetValue(key, out id);

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
            var oreDepositDefs = UnityEngine.Resources.LoadAll<OreDepositDef>($"{rootPath}/OreDeposits");
            return Build(resourceDefs, recipeDefs, machineDefs, oreDepositDefs);
        }

        public static GameDatabase Build(ResourceDef[] resourceDefs, RecipeDef[] recipeDefs, MachineDef[] machineDefs, OreDepositDef[] oreDepositDefs = null)
        {
            oreDepositDefs ??= Array.Empty<OreDepositDef>();

            // Resources.LoadAll의 순서는 플랫폼/빌드마다 보장되지 않으므로, id 문자열로 정렬해
            // 실행마다 동일한 int id가 배정되도록 한다 (결정적 동작, 저장 데이터 안정성).
            var (resources, resourceIdByKey) = BuildIndexed(
                resourceDefs, r => r.resourceId,
                def => new ResourceRuntime(def.resourceId, def.displayName, def.color));

            var (machines, machineIdByKey) = BuildIndexed(
                machineDefs, m => m.machineId,
                def => new MachineRuntime(def.machineId, def.category, def.footprint, def.speedMultiplier));

            // 레시피/광물노드는 resourceIdByKey가 먼저 완성되어 있어야 재료를 id로 풀 수 있다.
            var (recipes, recipeIdByKey) = BuildIndexed(
                recipeDefs, r => r.recipeId,
                def => new RecipeRuntime(
                    def.recipeId,
                    ResolveIngredients(def.inputs, resourceIdByKey),
                    ResolveIngredients(def.outputs, resourceIdByKey),
                    def.processSeconds,
                    def.requiredCategory));

            var (oreDeposits, oreDepositIdByKey) = BuildIndexed(
                oreDepositDefs, d => d.depositId,
                def => new OreDepositRuntime(def.depositId, resourceIdByKey[def.resource.resourceId], def.mineIntervalSeconds, def.yieldPerCycle));

            return new GameDatabase(resources, recipes, machines, oreDeposits, resourceIdByKey, recipeIdByKey, machineIdByKey, oreDepositIdByKey);
        }

        // id 문자열로 정렬 -> 배열 인덱스를 그 정렬 순서로 배정 -> 키→id 사전을 함께 만드는,
        // Resources/Machines/Recipes/OreDeposits 네 가지에서 반복되던 패턴 하나로 통합.
        private static (TRuntime[] runtime, Dictionary<string, int> idByKey) BuildIndexed<TDef, TRuntime>(
            TDef[] defs, Func<TDef, string> keyOf, Func<TDef, TRuntime> makeRuntime)
        {
            var sorted = defs.OrderBy(keyOf, StringComparer.Ordinal).ToArray();
            var idByKey = new Dictionary<string, int>(sorted.Length);
            var runtime = new TRuntime[sorted.Length];
            for (int i = 0; i < sorted.Length; i++)
            {
                idByKey[keyOf(sorted[i])] = i;
                runtime[i] = makeRuntime(sorted[i]);
            }
            return (runtime, idByKey);
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

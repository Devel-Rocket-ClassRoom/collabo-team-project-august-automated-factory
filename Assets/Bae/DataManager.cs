using System.Collections.Generic;
using UnityEngine;
using System.IO;

namespace Bae.Data
{
    public class DataManager : MonoBehaviour
    {
        public static DataManager Instance { get; private set; }

        // 빠른 검색을 위한 딕셔너리
        public Dictionary<string, ItemData> itemDict = new Dictionary<string, ItemData>();
        public Dictionary<string, MachineData> machineDict = new Dictionary<string, MachineData>();
        public Dictionary<string, RecipeData> recipeDict = new Dictionary<string, RecipeData>();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                LoadAllData();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        // 게임 시작 시 JSON 데이터를 읽어와서 딕셔너리에 세팅합니다.
        public void LoadAllData()
        {
            LoadItems();
            LoadMachines();
            LoadRecipes();
            Debug.Log("모든 JSON 데이터 로드 완료!");
        }

        private void LoadItems()
        {
            // Assets/Bae/Resources/JSON/Items.json 을 읽어옵니다.
            TextAsset jsonAsset = Resources.Load<TextAsset>("JSON/Items");
            if (jsonAsset != null)
            {
                ItemDatabase db = JsonUtility.FromJson<ItemDatabase>(jsonAsset.text);
                foreach (var item in db.items)
                {
                    itemDict[item.itemID] = item;
                }
            }
            else
            {
                Debug.LogError("Items.json 을 찾을 수 없습니다! (경로: Assets/Bae/Resources/JSON/Items.json)");
            }
        }

        private void LoadMachines()
        {
            TextAsset jsonAsset = Resources.Load<TextAsset>("JSON/Machines");
            if (jsonAsset != null)
            {
                MachineDatabase db = JsonUtility.FromJson<MachineDatabase>(jsonAsset.text);
                foreach (var machine in db.machines)
                {
                    machineDict[machine.machineID] = machine;
                }
            }
            else
            {
                Debug.LogError("Machines.json 을 찾을 수 없습니다!");
            }
        }

        private void LoadRecipes()
        {
            TextAsset jsonAsset = Resources.Load<TextAsset>("JSON/Recipes");
            if (jsonAsset != null)
            {
                RecipeDatabase db = JsonUtility.FromJson<RecipeDatabase>(jsonAsset.text);
                foreach (var recipe in db.recipes)
                {
                    recipeDict[recipe.recipeID] = recipe;
                }
            }
            else
            {
                Debug.LogError("Recipes.json 을 찾을 수 없습니다!");
            }
        }

        // 특정 기계(machineID)에서 사용 가능한 모든 레시피를 가져오는 헬퍼 함수
        public List<RecipeData> GetRecipesForMachine(string machineID)
        {
            List<RecipeData> availableRecipes = new List<RecipeData>();
            foreach (var recipe in recipeDict.Values)
            {
                if (recipe.machineID == machineID)
                {
                    availableRecipes.Add(recipe);
                }
            }
            return availableRecipes;
        }
    }
}

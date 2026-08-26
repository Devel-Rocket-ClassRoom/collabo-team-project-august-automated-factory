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

        private string GetJsonText(string fileName)
        {
            // 1. 모바일 기기(또는 PC)에서 접근 가능한 외부 폴더 경로
            string externalPath = Path.Combine(Application.persistentDataPath, fileName + ".json");

            // 2. 외부 폴더에 수정된 JSON 파일이 존재하면 그것을 최우선으로 읽어옵니다. (실시간 수정 기능)
            if (File.Exists(externalPath))
            {
                return File.ReadAllText(externalPath);
            }
            
            // 3. 외부에 파일이 없다면, 앱(APK) 내부에 빌드된 기본 데이터를 읽어옵니다.
            TextAsset jsonAsset = Resources.Load<TextAsset>("JSON/" + fileName);
            if (jsonAsset != null)
            {
                // 다음번엔 외부에서 수정할 수 있도록 복사본을 밖으로 빼둡니다.
                File.WriteAllText(externalPath, jsonAsset.text);
                return jsonAsset.text;
            }

            Debug.LogError($"JSON 데이터를 찾을 수 없습니다: {fileName}");
            return null;
        }

        private void LoadItems()
        {
            string jsonText = GetJsonText("Items");
            if (!string.IsNullOrEmpty(jsonText))
            {
                ItemDatabase db = JsonUtility.FromJson<ItemDatabase>(jsonText);
                foreach (var item in db.items) itemDict[item.itemID] = item;
            }
        }

        private void LoadMachines()
        {
            string jsonText = GetJsonText("Machines");
            if (!string.IsNullOrEmpty(jsonText))
            {
                MachineDatabase db = JsonUtility.FromJson<MachineDatabase>(jsonText);
                foreach (var machine in db.machines) machineDict[machine.machineID] = machine;
            }
        }

        private void LoadRecipes()
        {
            string jsonText = GetJsonText("Recipes");
            if (!string.IsNullOrEmpty(jsonText))
            {
                RecipeDatabase db = JsonUtility.FromJson<RecipeDatabase>(jsonText);
                foreach (var recipe in db.recipes) recipeDict[recipe.recipeID] = recipe;
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

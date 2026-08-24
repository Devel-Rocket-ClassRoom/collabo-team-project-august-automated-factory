using System.Collections.Generic;
using Factory.Data;
using Factory.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Factory.UI
{
    // 제련로/조립기를 탭했을 때 뜨는 레시피 선택 패널. 카테고리에 맞는 레시피 버튼을 동적으로
    // 채우고, 고르면 그 프로세서 인스턴스에 RecipeId를 대입한다. 사용자가 설명한 대로 —
    // "레시피를 선택해서 지정하면 거기서 필요한 자원의 정보를 전달받는" 구조.
    public class RecipeSelectionPanel : MonoBehaviour
    {
        // 씬에 하나만 있는 패널이라 MachineView가 개별 배선 없이 바로 열 수 있게 static으로 노출.
        public static RecipeSelectionPanel Instance { get; private set; }

        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Transform buttonContainer;
        [SerializeField] private SimulationDriver driver;

        private readonly List<GameObject> spawnedButtons = new List<GameObject>();
        private int targetProcessorIndex = -1;

        private void Awake()
        {
            Instance = this;
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        public void Open(int processorIndex, MachineCategory category)
        {
            if (driver == null || driver.World == null) return;

            targetProcessorIndex = processorIndex;
            ClearButtons();

            var db = driver.World.Database;
            var recipeIds = db.GetRecipeIdsForCategory(category);
            for (int i = 0; i < recipeIds.Count; i++)
            {
                CreateRecipeButton(recipeIds[i], db.Recipes[recipeIds[i]].Key);
            }

            if (panelRoot != null) panelRoot.SetActive(true);
        }

        public void Close()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
            targetProcessorIndex = -1;
        }

        private void CreateRecipeButton(int recipeId, string label)
        {
            var go = new GameObject($"Recipe_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(buttonContainer, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(320f, 90f);
            go.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 0.95f);

            var textGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textGO.transform.SetParent(go.transform, false);
            var rt = textGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var text = textGO.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.fontSize = 28;
            text.text = label;

            go.GetComponent<Button>().onClick.AddListener(() => SelectRecipe(recipeId));
            spawnedButtons.Add(go);
        }

        private void SelectRecipe(int recipeId)
        {
            if (targetProcessorIndex >= 0 && targetProcessorIndex < driver.World.Processors.Count)
            {
                driver.World.Processors[targetProcessorIndex].RecipeId = recipeId;
            }
            Close();
        }

        private void ClearButtons()
        {
            for (int i = 0; i < spawnedButtons.Count; i++) Destroy(spawnedButtons[i]);
            spawnedButtons.Clear();
        }
    }
}

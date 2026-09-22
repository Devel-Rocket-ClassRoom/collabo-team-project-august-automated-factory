using System.Reflection;
using Factory.Building;
using Factory.Buildings;
using Factory.UI;
using Seo.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Choi.Research
{
    public sealed class CoreResearchButtonBridge : MonoBehaviour
    {
        private Button researchButton;
        private float nextRefresh;

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .25f;
            EnsureCoreButton();
            RefreshUnlockButtons();
        }

        private void EnsureCoreButton()
        {
            UIManager ui = UIManager.Instance;
            bool coreOpen = ui != null && ui.IsMachineInfoOpen && ui.SelectedKind == MachineInstanceKind.Processor
                && IsSelectedCore(ui);
            if (researchButton == null)
            {
                GameObject panel = GameObject.Find("MachineInfoPanel");
                Transform footer = panel != null ? panel.transform.Find("ActionFooter") : null;
                if (footer == null) return;
                researchButton = SeoUIFactory.CreateButton(footer, "ResearchButton", "연구소", () => ResearchController.Instance?.Open(), new Color(.13f, .48f, .58f, 1f));
                SeoUIFactory.SetRect(researchButton.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(246f, 20f), new Vector2(126f, 58f));
            }
            researchButton.gameObject.SetActive(coreOpen);
        }

        private static bool IsSelectedCore(UIManager ui)
        {
            var field = typeof(UIManager).GetField("driver", BindingFlags.Instance | BindingFlags.NonPublic);
            var driver = field?.GetValue(ui) as Factory.Simulation.SimulationDriver;
            return driver != null && driver.World != null && ui.SelectedIndex == driver.World.CoreProcessorIndex;
        }

        private static void RefreshUnlockButtons()
        {
            var research = ResearchController.Instance;
            if (research == null) return;
            // 카테고리 창이 닫혀 비활성화된 버튼도 포함해야 다음에 창을 열 때 해금 상태가 보인다.
            foreach (var palette in Resources.FindObjectsOfTypeAll<BuildPaletteButton>())
            {
                if (palette == null || !palette.gameObject.scene.IsValid()) continue;
                var idField = typeof(BuildPaletteButton).GetField("machineId", BindingFlags.Instance | BindingFlags.NonPublic);
                string id = idField?.GetValue(palette) as string;
                Button button = palette.GetComponent<Button>();
                if (button != null && !string.IsNullOrEmpty(id))
                {
                    bool unlocked = research.IsMachineUnlocked(id);
                    SeoUIFactory.SetResearchLocked(button, !unlocked);
                }
            }
            var recipePanel = RecipeSelectionPanel.Instance;
            if (recipePanel == null) return;
            foreach (Button button in recipePanel.GetComponentsInChildren<Button>(true))
            {
                if (!button.name.StartsWith("Recipe_")) continue;
                bool unlocked = research.IsRecipeUnlocked(button.name.Substring(7));
                SeoUIFactory.SetResearchLocked(button, !unlocked);
            }
        }
    }
}
